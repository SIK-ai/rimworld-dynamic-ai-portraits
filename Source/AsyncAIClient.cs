using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace AIPortraits
{
    public class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner instance;
        public static CoroutineRunner Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("AIPortraits_CoroutineRunner");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<CoroutineRunner>();
                }
                return instance;
            }
        }
    }

    public static class AsyncAIClient
    {
        public delegate void PortraitCallback(Texture2D texture, byte[] rawBytes, string promptUsed, string error);

        private const int RequestTimeoutSeconds = 120;

        public static void QueueGeneration(PawnState state, AIPortraitsSettings settings, string continuityToken, PortraitCallback callback)
        {
            // If Gemini Flash prompt generation is enabled and a key is available, run that path.
            if (settings.useLLMPrompt && !string.IsNullOrEmpty(GetLLMApiKey(settings)))
            {
                CoroutineRunner.Instance.StartCoroutine(GenerateLLMThenDispatch(state, settings, continuityToken, callback));
                return;
            }

            // Standard compiled-template path
            string positivePrompt = PromptCompiler.CompilePositivePrompt(state, settings, continuityToken);
            string negativePrompt = PromptCompiler.CompileNegativePrompt(settings);
            Log.Message("[Dynamic AI Portraits] Prompt: " + positivePrompt);
            DispatchImageBackend(positivePrompt, negativePrompt, settings, state, callback);
        }

        /// <summary>Routes a compiled prompt to the configured image backend.</summary>
        private static void DispatchImageBackend(string positivePrompt, string negativePrompt,
                                                 AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            switch (settings.backendType)
            {
                case BackendType.HuggingFace:
                    CoroutineRunner.Instance.StartCoroutine(GenerateHuggingFace(positivePrompt, negativePrompt, settings, state, callback));
                    break;
                case BackendType.Pollinations:
                    CoroutineRunner.Instance.StartCoroutine(GeneratePollinations(positivePrompt, settings, state, callback));
                    break;
                case BackendType.GoogleImagen:
                    CoroutineRunner.Instance.StartCoroutine(GenerateGoogleImagen(positivePrompt, negativePrompt, settings, state, callback));
                    break;
                case BackendType.LocalA1111:
                    CoroutineRunner.Instance.StartCoroutine(GenerateLocalA1111(positivePrompt, negativePrompt, settings, state, callback));
                    break;
                case BackendType.Cloudflare:
                    CoroutineRunner.Instance.StartCoroutine(GenerateCloudflare(positivePrompt, negativePrompt, settings, state, callback));
                    break;
                case BackendType.DeepInfra:
                    CoroutineRunner.Instance.StartCoroutine(GenerateDeepInfra(positivePrompt, negativePrompt, settings, state, callback));
                    break;
                default:
                    callback(null, null, null, "Backend type " + settings.backendType + " is not implemented.");
                    break;
            }
        }

        /// <summary>
        /// Resolves which API key to use for Gemini Flash.
        /// Prefers the dedicated llmApiKey; falls back to apiKey when using GoogleImagen
        /// (same Google AI Studio key works for both Imagen and Gemini Flash).
        /// </summary>
        private static string GetLLMApiKey(AIPortraitsSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.llmApiKey)) return settings.llmApiKey;
            if (settings.backendType == BackendType.GoogleImagen && !string.IsNullOrEmpty(settings.apiKey))
                return settings.apiKey;
            return null;
        }

        /// <summary>
        /// Calls Gemini Flash to generate the image prompt from pawn metadata, then
        /// dispatches to the configured image backend with the result.
        /// Falls back to the compiled template on any failure so portrait generation
        /// always succeeds even without a valid LLM key.
        /// </summary>
        private static IEnumerator GenerateLLMThenDispatch(PawnState state, AIPortraitsSettings settings,
                                                            string continuityToken, PortraitCallback callback)
        {
            string llmKey    = GetLLMApiKey(settings);
            string pawnDesc  = PromptCompiler.CompilePawnStateDescription(state, settings);
            string sysPrompt = PromptCompiler.GetLLMSystemPrompt(settings.portraitStyle, state.framing);

            // gemini-3.1-flash-lite is free-tier and very fast (~1 s round-trip).
            string llmUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent?key=" + llmKey;

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"system_instruction\":{\"parts\":[{\"text\":\"")
                .Append(EscapeJson(sysPrompt)).Append("\"}]},");
            json.Append("\"contents\":[{\"parts\":[{\"text\":\"")
                .Append(EscapeJson(pawnDesc)).Append("\"}]}],");
            json.Append("\"generationConfig\":{\"maxOutputTokens\":400,\"temperature\":0.75}");
            json.Append("}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());
            string generatedPrompt = null;

            using (UnityWebRequest request = new UnityWebRequest(llmUrl, "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 30;

                Log.Message("[Dynamic AI Portraits] Calling Gemini Flash for " + (state.name ?? "pawn") + "...");
                yield return request.SendWebRequest();

                if (IsSuccess(request))
                {
                    generatedPrompt = ExtractGeminiText(request.downloadHandler.text);
                    if (!string.IsNullOrEmpty(generatedPrompt))
                        Log.Message("[Dynamic AI Portraits] LLM prompt: " + generatedPrompt);
                    else
                        Log.Warning("[Dynamic AI Portraits] Gemini returned empty text. Raw: " +
                                    Truncate(request.downloadHandler.text, 400));
                }
                else
                {
                    Log.Warning("[Dynamic AI Portraits] Gemini Flash error: " + request.error +
                                " | " + Truncate(request.downloadHandler.text, 200));
                }
            }

            // Fall back to compiled template if LLM failed
            if (string.IsNullOrEmpty(generatedPrompt))
            {
                Log.Warning("[Dynamic AI Portraits] Falling back to compiled template.");
                generatedPrompt = PromptCompiler.CompilePositivePrompt(state, settings, continuityToken);
            }

            string negativePrompt = PromptCompiler.CompileNegativePrompt(settings);
            DispatchImageBackend(generatedPrompt, negativePrompt, settings, state, callback);
        }

        /// <summary>
        /// Parses the text value from a Gemini API JSON response, correctly handling
        /// all standard JSON escape sequences. Returns null on any parse failure.
        /// </summary>
        private static string ExtractGeminiText(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                // Navigate: candidates[0] -> content -> parts[0] -> text
                int idx = json.IndexOf("\"candidates\"");
                if (idx < 0) return null;
                idx = json.IndexOf("\"parts\"", idx);
                if (idx < 0) return null;
                idx = json.IndexOf("\"text\"", idx);
                if (idx < 0) return null;
                idx = json.IndexOf(':', idx) + 1;
                // Skip whitespace
                while (idx < json.Length && json[idx] != '"') idx++;
                if (idx >= json.Length) return null;
                idx++; // skip opening quote

                var sb = new StringBuilder();
                while (idx < json.Length)
                {
                    char c = json[idx];
                    if (c == '\\' && idx + 1 < json.Length)
                    {
                        char next = json[idx + 1];
                        idx += 2;
                        switch (next)
                        {
                            case '"':  sb.Append('"');  break;
                            case '\\': sb.Append('\\'); break;
                            case 'n':  sb.Append(' ');  break; // newlines → spaces
                            case 'r':                   break;
                            case 't':  sb.Append(' ');  break;
                            default:   sb.Append(next);  break;
                        }
                    }
                    else if (c == '"') { break; }
                    else { sb.Append(c); idx++; }
                }

                string result = sb.ToString().Trim();
                return result.Length > 0 ? result : null;
            }
            catch (Exception) { return null; }
        }

        // ── HuggingFace ──────────────────────────────────────────────────────────────
        private static IEnumerator GenerateHuggingFace(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string modelId = string.IsNullOrEmpty(settings.modelName) ? "stabilityai/stable-diffusion-xl-base-1.0" : settings.modelName;
            string baseUrl = string.IsNullOrEmpty(settings.apiUrl) ? "https://api-inference.huggingface.co" : settings.apiUrl.TrimEnd('/');
            string url = baseUrl + "/models/" + modelId;

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"inputs\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"parameters\":{");
            json.Append("\"negative_prompt\":\"").Append(EscapeJson(negativePrompt)).Append("\",");
            json.Append("\"num_inference_steps\":").Append(settings.steps).Append(",");
            json.Append("\"guidance_scale\":").Append(settings.cfgScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            json.Append("}}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = RequestTimeoutSeconds;
                if (!string.IsNullOrEmpty(settings.apiKey))
                    request.SetRequestHeader("Authorization", "Bearer " + settings.apiKey);

                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    callback(null, null, null, "HuggingFace API Error: " + request.error + " - " + Truncate(request.downloadHandler.text, 400));
                    yield break;
                }

                byte[] imgBytes = request.downloadHandler.data;
                if (imgBytes == null || imgBytes.Length == 0)
                {
                    callback(null, null, null, "Empty response body from HuggingFace.");
                    yield break;
                }

                DeliverImage(imgBytes, prompt, "HuggingFace", state, callback);
            }
        }

        // ── Pollinations ─────────────────────────────────────────────────────────────
        private static IEnumerator GeneratePollinations(string prompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string baseUrl = string.IsNullOrEmpty(settings.apiUrl) ? "https://image.pollinations.ai" : settings.apiUrl.TrimEnd('/');
            // Pollinations consolidated to "sana" — "flux" no longer exists on their endpoint.
            string model = string.IsNullOrEmpty(settings.modelName) ? "sana" : settings.modelName;

            // Pollinations puts the prompt in the URL path. URLs much over ~4KB get rejected, so
            // we hard-cap the encoded prompt length.
            string encodedPrompt = Uri.EscapeDataString(prompt);
            if (encodedPrompt.Length > 3500) encodedPrompt = encodedPrompt.Substring(0, 3500);
            string url = baseUrl + "/prompt/" + encodedPrompt + "?width=512&height=512&model=" + Uri.EscapeDataString(model) + "&nologo=true&private=true";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = RequestTimeoutSeconds;
                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    callback(null, null, null, "Pollinations API Error: " + request.error);
                    yield break;
                }

                byte[] imgBytes = request.downloadHandler.data;
                if (imgBytes == null || imgBytes.Length == 0)
                {
                    callback(null, null, null, "Empty response body from Pollinations.");
                    yield break;
                }

                DeliverImage(imgBytes, prompt, "Pollinations", state, callback);
            }
        }

        // ── Google Imagen ────────────────────────────────────────────────────────────
        private static IEnumerator GenerateGoogleImagen(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string baseUrl = string.IsNullOrEmpty(settings.apiUrl) ? "https://generativelanguage.googleapis.com" : settings.apiUrl.TrimEnd('/');
            string model = string.IsNullOrEmpty(settings.modelName) ? "imagen-4.0-fast-generate-001" : settings.modelName;
            string url = baseUrl + "/v1beta/models/" + model + ":predict";

            string fullPrompt = PromptCompiler.CompileImagenSystemPrompt(settings.portraitStyle) + "\n\n" + prompt;

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"instances\":[{");
            json.Append("\"prompt\":\"").Append(EscapeJson(fullPrompt)).Append("\"");
            json.Append("}],");
            json.Append("\"parameters\":{");
            json.Append("\"sampleCount\":1,");
            json.Append("\"aspectRatio\":\"1:1\",");
            json.Append("\"outputMimeType\":\"image/png\"");
            json.Append("}}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-goog-api-key", settings.apiKey ?? "");
                request.timeout = RequestTimeoutSeconds;

                Log.Message("[Dynamic AI Portraits] Google Imagen URL: " + url);

                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    callback(null, null, null, "Google Imagen API Error: " + request.error + " | " + Truncate(request.downloadHandler.text, 400));
                    yield break;
                }

                string text = request.downloadHandler.text;
                Log.Message("[Dynamic AI Portraits] Google Imagen raw response (first 300): " + Truncate(text, 300));

                try
                {
                    // Response: {"predictions":[{"bytesBase64Encoded":"...","mimeType":"image/png"}]}
                    int keyIdx = text.IndexOf("\"bytesBase64Encoded\"");
                    if (keyIdx == -1)
                    {
                        callback(null, null, null, "Could not find 'bytesBase64Encoded' in response. Full response: " + Truncate(text, 400));
                        yield break;
                    }

                    int colonIdx = text.IndexOf(':', keyIdx);
                    int openQuote = text.IndexOf('"', colonIdx + 1);
                    int closeQuote = text.IndexOf('"', openQuote + 1);
                    if (openQuote == -1 || closeQuote == -1)
                    {
                        callback(null, null, null, "Malformed bytesBase64Encoded value.");
                        yield break;
                    }

                    string base64 = text.Substring(openQuote + 1, closeQuote - openQuote - 1);
                    byte[] imgBytes = Convert.FromBase64String(base64);

                    DeliverImage(imgBytes, prompt, "Google Imagen", state, callback);
                }
                catch (Exception ex)
                {
                    callback(null, null, null, "Google Imagen parse exception: " + ex.Message);
                }
            }
        }

        // ── Cloudflare Workers AI (FLUX.1 Schnell + others) ─────────────────────────
        private static IEnumerator GenerateCloudflare(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string combined = (settings.apiKey ?? "").Trim();
            int colonIdx = combined.IndexOf(':');
            if (colonIdx <= 0 || colonIdx >= combined.Length - 1)
            {
                callback(null, null, null, "Cloudflare needs API Key in format 'account_id:token'. Get both at dash.cloudflare.com → AI → Workers AI.");
                yield break;
            }
            string accountId = combined.Substring(0, colonIdx).Trim();
            string apiToken  = combined.Substring(colonIdx + 1).Trim();

            string model = string.IsNullOrEmpty(settings.modelName)
                ? "@cf/black-forest-labs/flux-1-schnell"
                : settings.modelName;
            string url = "https://api.cloudflare.com/client/v4/accounts/" + accountId + "/ai/run/" + model;

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"prompt\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"steps\":4");
            json.Append("}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiToken);
                request.timeout = RequestTimeoutSeconds;

                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    callback(null, null, null, "Cloudflare API Error: " + request.error + " - " + Truncate(request.downloadHandler.text, 400));
                    yield break;
                }

                byte[] imgBytes = null;
                string parseErr = null;

                if (model.ToLower().Contains("flux"))
                {
                    // FLUX on Cloudflare returns JSON: { "result": { "image": "<base64>" } }
                    string text = request.downloadHandler.text;
                    int keyIdx = text.IndexOf("\"image\"");
                    if (keyIdx == -1)
                    {
                        parseErr = "Cloudflare response missing image bytes.";
                    }
                    else
                    {
                        int colonIndex = text.IndexOf(':', keyIdx);
                        int openQuote = text.IndexOf('"', colonIndex + 1);
                        int closeQuote = text.IndexOf('"', openQuote + 1);
                        if (openQuote == -1 || closeQuote == -1)
                        {
                            parseErr = "Malformed image base64 value in Cloudflare response.";
                        }
                        else
                        {
                            string b64 = text.Substring(openQuote + 1, closeQuote - openQuote - 1);
                            try { imgBytes = Convert.FromBase64String(b64); }
                            catch (Exception ex) { parseErr = "Cloudflare base64 decode failed: " + ex.Message; }
                        }
                    }
                }
                else
                {
                    // Raw binary (some non-FLUX Cloudflare models)
                    imgBytes = request.downloadHandler.data;
                }

                if (parseErr != null) { callback(null, null, null, parseErr); yield break; }
                DeliverImage(imgBytes, prompt, "Cloudflare", state, callback);
            }
        }

        // ── DeepInfra (OpenAI-compatible image inference) ───────────────────────────
        // Ultra-cheap (~$0.0005/image at 512×512), GitHub OAuth signup, single token.
        // POST /v1/inference/<model> with prompt + dimensions, response has base64 PNGs.
        private static IEnumerator GenerateDeepInfra(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string apiToken = (settings.apiKey ?? "").Trim();
            if (string.IsNullOrEmpty(apiToken))
            {
                callback(null, null, null, "DeepInfra requires an API token. Sign up free at deepinfra.com.");
                yield break;
            }

            string model = string.IsNullOrEmpty(settings.modelName)
                ? "black-forest-labs/FLUX-1-schnell"
                : settings.modelName;
            string baseUrl = string.IsNullOrEmpty(settings.apiUrl)
                ? "https://api.deepinfra.com"
                : settings.apiUrl.TrimEnd('/');
            string url = baseUrl + "/v1/inference/" + model;

            // FLUX Schnell wants num_inference_steps=4; SDXL wants ~20-30
            int steps = model.ToLower().Contains("schnell")
                ? 4
                : System.Math.Max(20, System.Math.Min(30, settings.steps));

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"prompt\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"width\":512,");
            json.Append("\"height\":512,");
            json.Append("\"num_inference_steps\":").Append(steps);
            json.Append("}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiToken);
                request.timeout = RequestTimeoutSeconds;

                Log.Message("[Dynamic AI Portraits] DeepInfra URL: " + url);
                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    callback(null, null, null, "DeepInfra API Error: " + request.error +
                                               " | " + Truncate(request.downloadHandler.text, 400));
                    yield break;
                }

                // Response: {"images":["data:image/png;base64,..."], ...}
                // OR:       {"image_url":"data:image/png;base64,..."}
                string text = request.downloadHandler.text ?? "";
                string b64 = null;
                string parseErr = null;

                int marker = text.IndexOf("base64,");
                if (marker >= 0)
                {
                    int closeQuote = text.IndexOf('"', marker + 7);
                    if (closeQuote == -1) parseErr = "Malformed DeepInfra base64 payload.";
                    else b64 = text.Substring(marker + 7, closeQuote - (marker + 7));
                }
                else
                {
                    parseErr = "DeepInfra response had no 'base64,' marker: " + Truncate(text, 400);
                }

                if (parseErr != null) { callback(null, null, null, parseErr); yield break; }

                byte[] imgBytes;
                try { imgBytes = Convert.FromBase64String(b64); }
                catch (Exception ex)
                {
                    callback(null, null, null, "DeepInfra base64 decode failed: " + ex.Message);
                    yield break;
                }

                DeliverImage(imgBytes, prompt, "DeepInfra", state, callback);
            }
        }

        // ── Local A1111 (AUTOMATIC1111 / Forge / SD.Next / ComfyUI A1111-compat) ───
        private static IEnumerator GenerateLocalA1111(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string baseUrl = string.IsNullOrEmpty(settings.apiUrl) ? "http://127.0.0.1:7860" : settings.apiUrl.TrimEnd('/');
            string url = baseUrl + "/sdapi/v1/txt2img";

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"prompt\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"negative_prompt\":\"").Append(EscapeJson(negativePrompt)).Append("\",");
            json.Append("\"steps\":").Append(settings.steps).Append(",");
            json.Append("\"width\":512,");
            json.Append("\"height\":512,");
            json.Append("\"cfg_scale\":").Append(settings.cfgScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(",");
            json.Append("\"sampler_name\":\"DPM++ 2M Karras\",");
            json.Append("\"seed\":-1");
            if (!string.IsNullOrEmpty(settings.modelName))
            {
                json.Append(",\"override_settings\":{\"sd_model_checkpoint\":\"")
                    .Append(EscapeJson(settings.modelName)).Append("\"}");
            }
            json.Append("}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                // Local server can be slow on first generation (model load). Allow 5 min.
                request.timeout = 300;

                Log.Message("[Dynamic AI Portraits] Local A1111 URL: " + url);

                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    string hint = " | Is your local server running at " + baseUrl + "? " +
                                  "Start AUTOMATIC1111/Forge with --api flag before generating.";
                    callback(null, null, null, "Local A1111 API Error: " + request.error + hint +
                                         " | " + Truncate(request.downloadHandler.text, 200));
                    yield break;
                }

                string text = request.downloadHandler.text;

                string base64 = null;
                string parseErr = null;
                try
                {
                    // Response: {"images":["base64data", ...], "parameters":{...}, "info":"..."}
                    int keyIdx = text.IndexOf("\"images\"");
                    if (keyIdx == -1) { parseErr = "No 'images' field in A1111 response: " + Truncate(text, 300); }
                    else
                    {
                        int bracketIdx = text.IndexOf('[', keyIdx);
                        int openQuote  = text.IndexOf('"', bracketIdx + 1);
                        int closeQuote = text.IndexOf('"', openQuote + 1);
                        if (openQuote == -1 || closeQuote == -1)
                            parseErr = "Malformed images array in A1111 response.";
                        else
                            base64 = text.Substring(openQuote + 1, closeQuote - openQuote - 1);
                    }
                }
                catch (Exception ex)
                {
                    parseErr = "Local A1111 parse exception: " + ex.Message;
                }

                if (parseErr != null)
                {
                    callback(null, null, null, parseErr);
                    yield break;
                }

                byte[] imgBytes;
                try { imgBytes = Convert.FromBase64String(base64); }
                catch (Exception ex)
                {
                    callback(null, null, null, "Local A1111 base64 decode failed: " + ex.Message);
                    yield break;
                }

                DeliverImage(imgBytes, prompt, "Local A1111", state, callback);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        // Decodes the raw image bytes, runs background removal, and hands the result
        // back to the caller. If the BackgroundRemover produces a new texture (i.e.
        // the image had an opaque background), the original decode is destroyed and
        // the new texture + re-encoded PNG bytes are returned so the cache + disk
        // save reflect the cleaned image.
        private static void DeliverImage(byte[] imgBytes, string promptUsed, string backendName, PawnState state, PortraitCallback callback)
        {
            if (imgBytes == null || imgBytes.Length == 0)
            {
                callback(null, null, null, backendName + ": empty image bytes.");
                return;
            }

            Texture2D raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(raw, imgBytes))
            {
                UnityEngine.Object.Destroy(raw);
                callback(null, null, null, backendName + ": failed to decode image bytes.");
                return;
            }

            Texture2D processed;
            byte[] finalBytes;
            try
            {
                if (state != null && state.framing == "special")
                {
                    processed = raw;
                    finalBytes = imgBytes;
                }
                else
                {
                    processed = BackgroundRemover.Process(raw);
                    if (processed != raw)
                    {
                        // Background was removed — re-encode to PNG so the saved file
                        // and cached image both have the cleaned transparent version.
                        finalBytes = ImageConversion.EncodeToPNG(processed);
                        UnityEngine.Object.Destroy(raw);
                    }
                    else
                    {
                        // Already transparent or removal was skipped — use original.
                        finalBytes = imgBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                // If background removal blows up for any reason, fall back to the raw image
                Log.Warning("[Dynamic AI Portraits] BackgroundRemover failed (" + backendName + "): " + ex.Message + ". Using raw image.");
                processed = raw;
                finalBytes = imgBytes;
            }

            callback(processed, finalBytes, promptUsed, null);
        }

        private static bool IsSuccess(UnityWebRequest request)
        {
            return request.result == UnityWebRequest.Result.Success;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        private static string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder sb = new StringBuilder(text.Length + 16);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
