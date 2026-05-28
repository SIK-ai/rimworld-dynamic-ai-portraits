using System;
using System.Collections;
using System.Collections.Generic;
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

        public static bool isGeneratingRefPortrait = false;

        private const int RequestTimeoutSeconds = 120;

        public static void QueueGeneration(PawnState state, AIPortraitsSettings settings, string continuityToken, byte[] portraitBytes, PortraitCallback callback)
        {
            // If a prompt-generation key is set, rewrite via the LLM; otherwise fall back to
            // the in-house compiled template (no separate on/off toggle — the key is the switch).
            if (!string.IsNullOrEmpty(GetLLMApiKey(settings)))
            {
                CoroutineRunner.Instance.StartCoroutine(GenerateLLMThenDispatch(state, settings, continuityToken, portraitBytes, callback));
                return;
            }

            // Standard compiled-template path
            string positivePrompt = PromptCompiler.CompilePositivePrompt(state, settings, continuityToken);
            string negativePrompt = PromptCompiler.CompileNegativePrompt(settings, state != null ? state.framing : "portrait");
            Log.Message("[Dynamic AI Portraits] Prompt: " + positivePrompt);
            DispatchImageBackend(positivePrompt, negativePrompt, settings, state, portraitBytes, callback);
        }

        public static void QueueCustomGeneration(string customPrompt, AIPortraitsSettings settings, PawnState state, byte[] portraitBytes, PortraitCallback callback)
        {
            string negativePrompt = PromptCompiler.CompileNegativePrompt(settings, state != null ? state.framing : "portrait");
            Log.Message("[Dynamic AI Portraits] Custom Prompt: " + customPrompt);
            DispatchImageBackend(customPrompt, negativePrompt, settings, state, portraitBytes, callback);
        }

        /// <summary>
        /// Routes a compiled prompt to the configured image backend, with automatic
        /// fallback to free Pollinations: if the chosen paid source has no API key, or
        /// if it fails at runtime (error or no image), we silently retry on Pollinations
        /// so generation never hard-fails for lack of a key/quota.
        /// </summary>
        private static void DispatchImageBackend(string positivePrompt, string negativePrompt,
                                                 AIPortraitsSettings settings, PawnState state, byte[] portraitBytes, PortraitCallback callback)
        {
            BackendType bt = settings.backendType;
            DebugLog.Log("API", "image dispatch: backend=" + bt + " framing=" + (state != null ? state.framing : "?"));

            // Paid/keyed source with a blank key → go straight to free Pollinations.
            bool needsKey = (bt == BackendType.Cloudflare || bt == BackendType.GoogleImagen ||
                             bt == BackendType.DeepInfra || bt == BackendType.HuggingFace);
            if (needsKey && string.IsNullOrEmpty((settings.CurrentApiKey ?? "").Trim()))
            {
                Log.Message("[Dynamic AI Portraits] No API key for " + bt + " — using free Pollinations.");
                DebugLog.Log("API", "image: no key for " + bt + " -> fallback Pollinations");
                CoroutineRunner.Instance.StartCoroutine(GeneratePollinations(positivePrompt, settings, state, callback));
                return;
            }

            // For any non-Pollinations source, wrap the callback so a runtime failure
            // (error string, or a null texture) auto-retries once on free Pollinations.
            PortraitCallback cb = callback;
            if (bt != BackendType.Pollinations)
            {
                BackendType failed = bt;
                PortraitCallback original = callback;
                cb = delegate(Texture2D tex, byte[] bytes, string promptUsed, string error)
                {
                    if (tex == null || !string.IsNullOrEmpty(error))
                    {
                        Log.Warning("[Dynamic AI Portraits] " + failed + " failed (" +
                                    (string.IsNullOrEmpty(error) ? "no image returned" : error) +
                                    ") — falling back to free Pollinations.");
                        DebugLog.Log("API", "image: " + failed + " FAILED (" + (string.IsNullOrEmpty(error) ? "no image" : error) + ") -> fallback Pollinations");
                        CoroutineRunner.Instance.StartCoroutine(GeneratePollinations(positivePrompt, settings, state, original));
                    }
                    else
                    {
                        original(tex, bytes, promptUsed, error);
                    }
                };
            }

            switch (bt)
            {
                case BackendType.HuggingFace:
                    CoroutineRunner.Instance.StartCoroutine(GenerateHuggingFace(positivePrompt, negativePrompt, settings, state, cb));
                    break;
                case BackendType.Pollinations:
                    CoroutineRunner.Instance.StartCoroutine(GeneratePollinations(positivePrompt, settings, state, cb));
                    break;
                case BackendType.GoogleImagen:
                    CoroutineRunner.Instance.StartCoroutine(GenerateGoogleImagen(positivePrompt, negativePrompt, settings, state, portraitBytes, cb));
                    break;
                case BackendType.LocalA1111:
                    CoroutineRunner.Instance.StartCoroutine(GenerateLocalA1111(positivePrompt, negativePrompt, settings, state, cb));
                    break;
                case BackendType.Cloudflare:
                    CoroutineRunner.Instance.StartCoroutine(GenerateCloudflare(positivePrompt, negativePrompt, settings, state, cb));
                    break;
                case BackendType.DeepInfra:
                    CoroutineRunner.Instance.StartCoroutine(GenerateDeepInfra(positivePrompt, negativePrompt, settings, state, cb));
                    break;
                default:
                    callback(null, null, null, "Backend type " + bt + " is not implemented.");
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
            if (!string.IsNullOrEmpty(settings.giApiKey))
                return settings.giApiKey;
            return null;
        }

        /// <summary>
        /// Scrubs API keys / tokens from a string before it gets logged or returned in
        /// an error callback. RimWorld's Player.log is plaintext on disk and routinely
        /// shared by users when reporting bugs — without this scrub, anyone who posts a
        /// failed-generation log would also be publishing their API keys.
        ///
        /// Originally proposed by Jules-bot (sentinel-fix-api-key-leak); adapted here to
        /// cover the per-provider credential fields the user added in WIP.
        /// </summary>
        private static string SanitizeLog(string message, AIPortraitsSettings settings)
        {
            if (string.IsNullOrEmpty(message) || settings == null) return message;
            string s = message;

            // Per-provider keys
            if (!string.IsNullOrEmpty(settings.cfApiKey)) s = s.Replace(settings.cfApiKey, "[REDACTED]");
            if (!string.IsNullOrEmpty(settings.giApiKey)) s = s.Replace(settings.giApiKey, "[REDACTED]");
            if (!string.IsNullOrEmpty(settings.diApiKey)) s = s.Replace(settings.diApiKey, "[REDACTED]");
            if (!string.IsNullOrEmpty(settings.hfApiKey)) s = s.Replace(settings.hfApiKey, "[REDACTED]");

            // Legacy single apiKey (back-compat)
            if (!string.IsNullOrEmpty(settings.apiKey)) s = s.Replace(settings.apiKey, "[REDACTED]");

            // LLM key — separate field, may differ from any provider's key
            if (!string.IsNullOrEmpty(settings.llmApiKey)) s = s.Replace(settings.llmApiKey, "[REDACTED]");
            // Newer dedicated keys (Veo video + Cloudflare background removal) — scrub these too,
            // otherwise they can leak into logs (sentinel-fix-api-key-leak).
            if (!string.IsNullOrEmpty(settings.videoApiKey)) s = s.Replace(settings.videoApiKey, "[REDACTED]");
            if (!string.IsNullOrEmpty(settings.cfBgRemovalKey)) s = s.Replace(settings.cfBgRemovalKey, "[REDACTED]");

            // Cloudflare uses account_id:token format — if cfApiKey is set we already redacted
            // it above, but the token portion alone might still appear in URL paths. Defensive
            // extra pass on each key after splitting on a colon (catches token-only leakage).
            string[] possibleCombined = { settings.cfApiKey, settings.cfBgRemovalKey };
            foreach (string combo in possibleCombined)
            {
                if (string.IsNullOrEmpty(combo)) continue;
                int colon = combo.IndexOf(':');
                if (colon > 0 && colon < combo.Length - 1)
                {
                    string accountId = combo.Substring(0, colon).Trim();
                    string token     = combo.Substring(colon + 1).Trim();
                    if (!string.IsNullOrEmpty(token))     s = s.Replace(token, "[REDACTED]");
                    if (!string.IsNullOrEmpty(accountId)) s = s.Replace(accountId, "[REDACTED_ACCT]");
                }
            }
            return s;
        }

        /// <summary>
        /// Calls Gemini Flash to generate the image prompt from pawn metadata, then
        /// dispatches to the configured image backend with the result.
        /// Falls back to the compiled template on any failure so portrait generation
        /// always succeeds even without a valid LLM key.
        /// </summary>
        private static IEnumerator GenerateLLMThenDispatch(PawnState state, AIPortraitsSettings settings,
                                                            string continuityToken, byte[] portraitBytes, PortraitCallback callback)
        {
            string llmKey    = GetLLMApiKey(settings);
            string pawnDesc  = PromptCompiler.CompilePawnStateDescription(state, settings);
            // Defensive null check on state.framing — every other call site in this file
            // already does the (state != null ? state.framing : "portrait") dance, this one
            // didn't and would NPE if a caller passed a half-built state.
            string framingForLLM = (state != null && !string.IsNullOrEmpty(state.framing)) ? state.framing : "portrait";
            string sysPrompt = PromptCompiler.GetLLMSystemPrompt(settings.portraitStyle, settings, framingForLLM, state != null && state.excludeHelmet);

            string modelName = "gemini-3.1-flash-lite";
            if (settings.llmModelType == LLMModelType.Gemma26B)
            {
                modelName = "gemma-4-26b-a4b-it";
            }
            else if (settings.llmModelType == LLMModelType.Gemma31B)
            {
                modelName = "gemma-4-31b-it";
            }
            string llmUrl = "https://generativelanguage.googleapis.com/v1beta/models/" + modelName + ":generateContent?key=" + llmKey;

            StringBuilder json = new StringBuilder();
            json.Append("{");
            if (settings.llmModelType == LLMModelType.GeminiFlashLite)
            {
                json.Append("\"system_instruction\":{\"parts\":[{\"text\":\"")
                    .Append(EscapeJson(sysPrompt)).Append("\"}]},");
                json.Append("\"contents\":[{\"parts\":[{\"text\":\"")
                    .Append(EscapeJson(pawnDesc)).Append("\"}]}],");
            }
            else
            {
                // Gemma doesn't support system_instruction, prepend system prompt to the user contents
                string combinedPrompt = sysPrompt + "\n\n" + pawnDesc;
                json.Append("\"contents\":[{\"parts\":[{\"text\":\"")
                    .Append(EscapeJson(combinedPrompt)).Append("\"}]}],");
            }
            json.Append("\"generationConfig\":{\"maxOutputTokens\":200,\"temperature\":0.75}");
            json.Append("}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());
            string generatedPrompt = null;

            string modelLogName = AIPortraitsSettings.LLMModelLabel(settings.llmModelType);

            using (UnityWebRequest request = new UnityWebRequest(llmUrl, "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 30;

                Log.Message("[Dynamic AI Portraits] Calling " + modelLogName + " for " + (state.name ?? "pawn") + "...");
                yield return request.SendWebRequest();

                if (IsSuccess(request))
                {
                    generatedPrompt = ExtractGeminiText(request.downloadHandler.text);
                    if (!string.IsNullOrEmpty(generatedPrompt))
                    {
                        string[] labelsToRemove = new string[] {
                            "Line 1:", "Line 2:", "Line 3:", "Line 4:", "Line 5:", "Line 6:", "Line 7:",
                            "Line 1", "Line 2", "Line 3", "Line 4", "Line 5", "Line 6", "Line 7",
                            "Core Subject & Pose:", "Clothing & Gear:", "Camera & Lens Settings:",
                            "Lighting & Shadows:", "Aesthetics & Color Scheme:", "Style Medium & Quality Keywords:",
                            "Background Setting:", "Core Subject:", "Clothing:", "Camera:", "Lighting:", "Aesthetics:", "Style:", "Background:"
                        };
                        foreach (string label in labelsToRemove)
                        {
                            generatedPrompt = generatedPrompt.Replace(label, "");
                        }
                        while (generatedPrompt.Contains(" ,")) generatedPrompt = generatedPrompt.Replace(" ,", ",");
                        while (generatedPrompt.Contains(",,")) generatedPrompt = generatedPrompt.Replace(",,", ",");
                        while (generatedPrompt.Contains("  ")) generatedPrompt = generatedPrompt.Replace("  ", " ");
                        generatedPrompt = generatedPrompt.Trim();

                        Log.Message("[Dynamic AI Portraits] LLM prompt: " + generatedPrompt);
                    }
                    else
                    {
                        Log.Warning(SanitizeLog("[Dynamic AI Portraits] " + modelLogName + " returned empty text. Raw: " +
                                    Truncate(request.downloadHandler.text, 400), settings));
                    }
                }
                else
                {
                    Log.Warning(SanitizeLog("[Dynamic AI Portraits] " + modelLogName + " error: " + request.error +
                                " | " + Truncate(request.downloadHandler.text, 200), settings));
                }
            }

            // Fall back to compiled template if LLM failed
            if (string.IsNullOrEmpty(generatedPrompt))
            {
                Log.Warning("[Dynamic AI Portraits] Falling back to compiled template.");
                generatedPrompt = PromptCompiler.CompilePositivePrompt(state, settings, continuityToken);
            }

            string negativePrompt = PromptCompiler.CompileNegativePrompt(settings, state != null ? state.framing : "portrait");
            DispatchImageBackend(generatedPrompt, negativePrompt, settings, state, portraitBytes, callback);
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
                            case 'n':  sb.Append(", ");  break; // newlines → comma separated
                            case 'r':                   break;
                            case 't':  sb.Append(' ');  break;
                            case 'u':
                                if (idx + 4 <= json.Length)
                                {
                                    string hex = json.Substring(idx, 4);
                                    try
                                    {
                                        int code = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                                        sb.Append((char)code);
                                        idx += 4;
                                    }
                                    catch
                                    {
                                        sb.Append('u');
                                    }
                                }
                                else
                                {
                                    sb.Append('u');
                                }
                                break;
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
            string modelId = string.IsNullOrEmpty(settings.CurrentModelName) ? "stabilityai/stable-diffusion-xl-base-1.0" : settings.CurrentModelName;
            string baseUrl = string.IsNullOrEmpty(settings.CurrentApiUrl) ? "https://api-inference.huggingface.co" : settings.CurrentApiUrl.TrimEnd('/');
            string url = baseUrl + "/models/" + modelId;

            int width = 512;
            int height = 512;
            if (state != null)
            {
                if (state.framing == "bodyshot") { width = 512; height = 768; }
                else if (state.framing == "special") { width = 768; height = 512; }
            }

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"inputs\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"parameters\":{");
            json.Append("\"negative_prompt\":\"").Append(EscapeJson(negativePrompt)).Append("\",");
            json.Append("\"num_inference_steps\":").Append(settings.steps).Append(",");
            json.Append("\"width\":").Append(width).Append(",");
            json.Append("\"height\":").Append(height).Append(",");
            json.Append("\"guidance_scale\":").Append(settings.cfgScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            json.Append("}}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = RequestTimeoutSeconds;
                if (!string.IsNullOrEmpty(settings.CurrentApiKey))
                    request.SetRequestHeader("Authorization", "Bearer " + settings.CurrentApiKey);

                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    callback(null, null, null, SanitizeLog("HuggingFace API Error: " + request.error + " - " + Truncate(request.downloadHandler.text, 400), settings));
                    yield break;
                }

                byte[] imgBytes = request.downloadHandler.data;
                if (imgBytes == null || imgBytes.Length == 0)
                {
                    callback(null, null, null, "Empty response body from HuggingFace.");
                    yield break;
                }

                DeliverImage(imgBytes, prompt, "HuggingFace", state, settings, callback);
            }
        }

        // ── Pollinations ─────────────────────────────────────────────────────────────
        private static IEnumerator GeneratePollinations(string prompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string baseUrl = string.IsNullOrEmpty(settings.CurrentApiUrl) ? "https://image.pollinations.ai" : settings.CurrentApiUrl.TrimEnd('/');
            string model = string.IsNullOrEmpty(settings.CurrentModelName) ? "sana" : settings.CurrentModelName;

            // Pollinations puts the prompt in the URL path. URLs much over ~4KB get rejected, so
            // we hard-cap the encoded prompt length.
            string encodedPrompt = Uri.EscapeDataString(prompt);
            if (encodedPrompt.Length > 3500) encodedPrompt = encodedPrompt.Substring(0, 3500);

            int width = 512;
            int height = 512;
            if (state != null)
            {
                if (state.framing == "bodyshot") { width = 512; height = 768; }
                else if (state.framing == "special") { width = 768; height = 512; }
            }

            string url = baseUrl + "/prompt/" + encodedPrompt
                       + "?width=" + width + "&height=" + height
                       + "&model=" + Uri.EscapeDataString(model)
                       + "&nologo=true&private=true";

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

                DeliverImage(imgBytes, prompt, "Pollinations", state, settings, callback);
            }
        }

        // ── Google Imagen ────────────────────────────────────────────────────────────
        private static IEnumerator GenerateGoogleImagen(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, byte[] portraitBytes, PortraitCallback callback)
        {
            string baseUrl = string.IsNullOrEmpty(settings.CurrentApiUrl) ? "https://generativelanguage.googleapis.com" : settings.CurrentApiUrl.TrimEnd('/');
            string model = string.IsNullOrEmpty(settings.CurrentModelName) ? "imagen-4.0-fast-generate-001" : settings.CurrentModelName;
            // Map UI names to official Google Model IDs
            string apiModel = model;
            if (apiModel == "nanobanana-2") apiModel = "gemini-3.1-flash-image-preview";
            else if (apiModel == "nanobanana") apiModel = "gemini-2.5-flash-image";
            else if (apiModel == "nanobanana-pro") apiModel = "gemini-3-pro-image-preview";

            bool isGeminiImageModel = apiModel.Contains("gemini-") && apiModel.Contains("-image");

            string url;
            StringBuilder json = new StringBuilder();

            // ONE reference image per generation, and its CONTENTS reflect the per-image toggles:
            //   "Reference portrait image" (refPortrait) => a portrait reference is included
            //   "Use gear reference sheet"  (useGearRef)  => the equipment sprites are included
            // The continuity portrait (portraitBytes, also gated by refPortrait upstream) and the
            // gear/native-portrait sheet are merged into a SINGLE combined image. With both toggles
            // off, no image is sent at all.
            bool wantPortrait = state != null && state.refPortrait;
            bool wantGear     = state != null && state.useGearRef;
            byte[] refSheetBytes = (wantPortrait || wantGear) ? BuildReferenceSheet(state) : null;
            byte[] refImage = CombineReferenceImages(portraitBytes, refSheetBytes);
            bool hasRef = refImage != null && refImage.Length > 0;

            string framing = state != null ? state.framing : "portrait";
            string fullPrompt = PromptCompiler.CompileImagenSystemPrompt(settings.portraitStyle, settings, framing) + "\n\n" + prompt;
            if (hasRef)
            {
                if (wantPortrait && wantGear)
                {
                    fullPrompt += "\n\nattached is a single combined reference image for this character: it contains a reference portrait (match this exact face, hair color, hair style, skin tone, and features) together with the character's equipment and clothing items (render the character wearing/holding these exact items).";
                }
                else if (wantPortrait)
                {
                    fullPrompt += "\n\nattached is a reference portrait of this character. match this exact face, hair color, hair style, skin tone, and features.";
                }
                else if (wantGear)
                {
                    fullPrompt += "\n\nattached is a reference sheet of the character's equipment and clothing items. render the character wearing/holding these exact items.";
                }
                if (framing == "special")
                {
                    fullPrompt += " DISREGARD any solid white background of this reference image entirely; you MUST generate the detailed thematic environment background described above.";
                }
            }

            string aspectRatio = "1:1";
            if (state != null)
            {
                if (state.framing == "bodyshot") aspectRatio = "3:4";
                else if (state.framing == "special") aspectRatio = "4:3";
            }

            if (isGeminiImageModel)
            {
                url = baseUrl + "/v1beta/models/" + apiModel + ":generateContent?key=" + (settings.CurrentApiKey ?? "");

                json.Append("{");
                json.Append("\"contents\":[{");
                json.Append("\"parts\":[");
                json.Append("{\"text\":\"").Append(EscapeJson(fullPrompt)).Append("\"}");

                if (hasRef)
                {
                    string base64Image = Convert.ToBase64String(refImage);
                    json.Append(",{");
                    json.Append("\"inlineData\":{");
                    json.Append("\"mimeType\":\"image/png\",");
                    json.Append("\"data\":\"").Append(base64Image).Append("\"");
                    json.Append("}");
                    json.Append("}");
                }

                json.Append("]");
                json.Append("}],");
                json.Append("\"generationConfig\":{");
                json.Append("\"responseModalities\":[\"IMAGE\"],");
                json.Append("\"imageConfig\":{");
                json.Append("\"aspectRatio\":\"").Append(aspectRatio).Append("\"");
                json.Append("}");
                json.Append("}");
                json.Append("}");
            }
            else
            {
                url = baseUrl + "/v1beta/models/" + apiModel + ":predict";

                bool isCapabilityModel = apiModel.Contains("nanobanana") || apiModel.Contains("capability") || apiModel.Contains("editing");

                json.Append("{");
                json.Append("\"instances\":[{");
                json.Append("\"prompt\":\"").Append(EscapeJson(fullPrompt)).Append("\"");

                if (isCapabilityModel && hasRef)
                {
                    string base64Image = Convert.ToBase64String(refImage);
                    json.Append(",\"referenceImage\":{");
                    json.Append("\"bytesBase64Encoded\":\"").Append(base64Image).Append("\",");
                    json.Append("\"mimeType\":\"image/png\"");
                    json.Append("}");
                }

                json.Append("}],");
                json.Append("\"parameters\":{");
                json.Append("\"sampleCount\":1,");
                json.Append("\"aspectRatio\":\"").Append(aspectRatio).Append("\",");
                json.Append("\"outputMimeType\":\"image/png\"");
                json.Append("}}");
            }

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (!isGeminiImageModel)
                {
                    request.SetRequestHeader("x-goog-api-key", settings.CurrentApiKey ?? "");
                }
                request.timeout = RequestTimeoutSeconds;

                Log.Message(SanitizeLog("[Dynamic AI Portraits] Google AI URL: " + url, settings));

                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    callback(null, null, null, SanitizeLog("Google AI API Error: " + request.error + " | " + Truncate(request.downloadHandler.text, 400), settings));
                    yield break;
                }

                string text = request.downloadHandler.text;
                Log.Message(SanitizeLog("[Dynamic AI Portraits] Google AI raw response (first 300): " + Truncate(text, 300), settings));

                try
                {
                    if (isGeminiImageModel)
                    {
                        // Response has: "inlineData":{"mimeType":"image/png","data":"..."}
                        int inlineDataIdx = text.IndexOf("\"inlineData\"");
                        if (inlineDataIdx == -1)
                        {
                            callback(null, null, null, "Could not find 'inlineData' in Gemini response. Full response: " + Truncate(text, 400));
                            yield break;
                        }

                        int dataKeyIdx = text.IndexOf("\"data\"", inlineDataIdx);
                        if (dataKeyIdx == -1)
                        {
                            callback(null, null, null, "Could not find 'data' field inside inlineData. Full response: " + Truncate(text, 400));
                            yield break;
                        }

                        int colonIdx = text.IndexOf(':', dataKeyIdx);
                        int openQuote = text.IndexOf('"', colonIdx + 1);
                        int closeQuote = text.IndexOf('"', openQuote + 1);
                        if (openQuote == -1 || closeQuote == -1)
                        {
                            callback(null, null, null, "Malformed data value inside inlineData.");
                            yield break;
                        }

                        string base64 = text.Substring(openQuote + 1, closeQuote - openQuote - 1);
                        byte[] imgBytes = Convert.FromBase64String(base64);

                        DeliverImage(imgBytes, prompt, model, state, settings, callback);
                    }
                    else
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

                        DeliverImage(imgBytes, prompt, model, state, settings, callback);
                    }
                }
                catch (Exception ex)
                {
                    callback(null, null, null, "Google AI parse exception: " + ex.Message);
                }
            }
        }

        // ── Cloudflare Workers AI (FLUX.1 Schnell + others) ─────────────────────────
        private static IEnumerator GenerateCloudflare(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string combined = (settings.CurrentApiKey ?? "").Trim();
            int colonIdx = combined.IndexOf(':');
            if (colonIdx <= 0 || colonIdx >= combined.Length - 1)
            {
                callback(null, null, null, "Cloudflare needs API Key in format 'account_id:token'. Get both at dash.cloudflare.com → AI → Workers AI.");
                yield break;
            }
            string accountId = combined.Substring(0, colonIdx).Trim();
            string apiToken  = combined.Substring(colonIdx + 1).Trim();

            string model = string.IsNullOrEmpty(settings.CurrentModelName)
                ? "@cf/black-forest-labs/flux-1-schnell"
                : settings.CurrentModelName;
            string url = "https://api.cloudflare.com/client/v4/accounts/" + accountId + "/ai/run/" + model;

            int width = 512;
            int height = 512;
            if (state != null)
            {
                if (state.framing == "bodyshot") { width = 512; height = 768; }
                else if (state.framing == "special") { width = 768; height = 512; }
            }

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"prompt\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"steps\":4,");
            json.Append("\"width\":").Append(width).Append(",");
            json.Append("\"height\":").Append(height);
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
                    callback(null, null, null, SanitizeLog("Cloudflare API Error: " + request.error + " - " + Truncate(request.downloadHandler.text, 400), settings));
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
                DeliverImage(imgBytes, prompt, "Cloudflare", state, settings, callback);
            }
        }

        // ── DeepInfra (OpenAI-compatible image inference) ───────────────────────────
        // Ultra-cheap (~$0.0005/image at 512×512), GitHub OAuth signup, single token.
        // POST /v1/inference/<model> with prompt + dimensions, response has base64 PNGs.
        private static IEnumerator GenerateDeepInfra(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string apiToken = (settings.CurrentApiKey ?? "").Trim();
            if (string.IsNullOrEmpty(apiToken))
            {
                callback(null, null, null, "DeepInfra requires an API token. Sign up free at deepinfra.com.");
                yield break;
            }

            string model = string.IsNullOrEmpty(settings.CurrentModelName)
                ? "black-forest-labs/FLUX-1-schnell"
                : settings.CurrentModelName;
            string baseUrl = string.IsNullOrEmpty(settings.CurrentApiUrl)
                ? "https://api.deepinfra.com"
                : settings.CurrentApiUrl.TrimEnd('/');
            string url = baseUrl + "/v1/inference/" + model;

            // FLUX Schnell wants num_inference_steps=4; SDXL wants ~20-30
            int steps = model.ToLower().Contains("schnell")
                ? 4
                : System.Math.Max(20, System.Math.Min(30, settings.steps));

            int width = 512;
            int height = 512;
            if (state != null)
            {
                if (state.framing == "bodyshot") { width = 512; height = 768; }
                else if (state.framing == "special") { width = 768; height = 512; }
            }

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"prompt\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"width\":").Append(width).Append(",");
            json.Append("\"height\":").Append(height).Append(",");
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
                    callback(null, null, null, SanitizeLog("DeepInfra API Error: " + request.error +
                                               " | " + Truncate(request.downloadHandler.text, 400), settings));
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

                DeliverImage(imgBytes, prompt, "DeepInfra", state, settings, callback);
            }
        }

        // ── Local A1111 (AUTOMATIC1111 / Forge / SD.Next / ComfyUI A1111-compat) ───
        private static IEnumerator GenerateLocalA1111(string prompt, string negativePrompt, AIPortraitsSettings settings, PawnState state, PortraitCallback callback)
        {
            string baseUrl = string.IsNullOrEmpty(settings.CurrentApiUrl) ? "http://127.0.0.1:7860" : settings.CurrentApiUrl.TrimEnd('/');
            string url = baseUrl + "/sdapi/v1/txt2img";

            int width = 512;
            int height = 512;
            if (state != null)
            {
                if (state.framing == "bodyshot") { width = 512; height = 768; }
                else if (state.framing == "special") { width = 768; height = 512; }
            }

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"prompt\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"negative_prompt\":\"").Append(EscapeJson(negativePrompt)).Append("\",");
            json.Append("\"steps\":").Append(settings.steps).Append(",");
            json.Append("\"width\":").Append(width).Append(",");
            json.Append("\"height\":").Append(height).Append(",");
            json.Append("\"cfg_scale\":").Append(settings.cfgScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(",");
            json.Append("\"sampler_name\":\"DPM++ 2M Karras\",");
            json.Append("\"seed\":-1");
            if (!string.IsNullOrEmpty(settings.CurrentModelName))
            {
                json.Append(",\"override_settings\": {\"sd_model_checkpoint\": \"")
                    .Append(EscapeJson(settings.CurrentModelName)).Append("\"}");
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
                    callback(null, null, null, SanitizeLog("Local A1111 API Error: " + request.error + hint +
                                         " | " + Truncate(request.downloadHandler.text, 200), settings));
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

                DeliverImage(imgBytes, prompt, "Local A1111", state, settings, callback);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        // Decodes the raw image bytes, runs background removal, and hands the result
        // back to the caller. If the BackgroundRemover produces a new texture (i.e.
        // the image had an opaque background), the original decode is destroyed and
        // the new texture + re-encoded PNG bytes are returned so the cache + disk
        // save reflect the cleaned image.
        private static string GetCFBgRemovalKey(AIPortraitsSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.cfBgRemovalKey)) return settings.cfBgRemovalKey;
            if (!string.IsNullOrEmpty(settings.cfApiKey) && settings.cfApiKey.Contains(":")) return settings.cfApiKey;
            return null;
        }

        private static IEnumerator GenerateCloudflareBackgroundRemoval(byte[] imgBytes, string promptUsed, string backendName, PawnState state, AIPortraitsSettings settings, bool shouldRemoveBg, PortraitCallback callback)
        {
            string key = GetCFBgRemovalKey(settings);
            if (string.IsNullOrEmpty(key) || !key.Contains(":"))
            {
                Log.Warning("[Dynamic AI Portraits] AI Background Removal enabled but no valid Cloudflare key found (format accountId:token). Falling back to local flood-fill.");
                DeliverImageLocal(imgBytes, promptUsed, backendName, state, shouldRemoveBg, callback);
                yield break;
            }

            int colonIdx = key.IndexOf(':');
            string accountId = key.Substring(0, colonIdx).Trim();
            string apiToken  = key.Substring(colonIdx + 1).Trim();

            string url = "https://api.cloudflare.com/client/v4/accounts/" + accountId + "/ai/run/@cf/bria-ai/bria-rmbg-1.4";

            string base64 = Convert.ToBase64String(imgBytes);
            string jsonBody = "{\"image\":\"" + base64 + "\"}";
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonBody);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiToken);
                request.timeout = RequestTimeoutSeconds;

                Log.Message("[Dynamic AI Portraits] Calling Cloudflare bria-rmbg-1.4 for background removal...");
                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    Log.Warning(SanitizeLog("[Dynamic AI Portraits] Cloudflare BG Removal failed: " + request.error + " | " + Truncate(request.downloadHandler.text, 200) + ". Falling back to local flood-fill.", settings));
                    DeliverImageLocal(imgBytes, promptUsed, backendName, state, shouldRemoveBg, callback);
                    yield break;
                }

                byte[] transparentBytes = request.downloadHandler.data;
                if (transparentBytes == null || transparentBytes.Length == 0)
                {
                    Log.Warning("[Dynamic AI Portraits] Cloudflare BG Removal returned empty bytes. Falling back to local.");
                    DeliverImageLocal(imgBytes, promptUsed, backendName, state, shouldRemoveBg, callback);
                    yield break;
                }

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, transparentBytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    Log.Warning("[Dynamic AI Portraits] Cloudflare BG Removal returned invalid image data. Falling back to local.");
                    DeliverImageLocal(imgBytes, promptUsed, backendName, state, shouldRemoveBg, callback);
                    yield break;
                }

                callback(tex, transparentBytes, promptUsed, null);
            }
        }

        private static void DeliverImage(byte[] imgBytes, string promptUsed, string backendName, PawnState state, AIPortraitsSettings settings, PortraitCallback callback)
        {
            if (imgBytes == null || imgBytes.Length == 0)
            {
                callback(null, null, null, backendName + ": empty image bytes.");
                return;
            }

            bool shouldRemoveBg = false;
            if (state != null)
            {
                if (state.framing == "portrait" || state.framing == "bodyshot")
                {
                    shouldRemoveBg = true;
                }
                else if (state.framing == "special")
                {
                    // Check if the generated special scene has a mono-colored/flat background
                    Texture2D temp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(temp, imgBytes))
                    {
                        if (BackgroundRemover.IsMonoBackground(temp))
                        {
                            shouldRemoveBg = true;
                            Log.Message("[Dynamic AI Portraits] Detected mono-colored background in special photoshoot. Enabling background removal.");
                        }
                    }
                    UnityEngine.Object.Destroy(temp);
                }
            }

            if (settings.useAIBgRemoval && shouldRemoveBg)
            {
                CoroutineRunner.Instance.StartCoroutine(GenerateCloudflareBackgroundRemoval(imgBytes, promptUsed, backendName, state, settings, shouldRemoveBg, callback));
            }
            else
            {
                DeliverImageLocal(imgBytes, promptUsed, backendName, state, shouldRemoveBg, callback);
            }
        }

        private static void DeliverImageLocal(byte[] imgBytes, string promptUsed, string backendName, PawnState state, bool shouldRemoveBg, PortraitCallback callback)
        {
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
                if (shouldRemoveBg)
                {
                    // u2netp ONNX remover (local, offline). Internally falls back to the legacy
                    // YCbCr remover if the native runtime/model can't load.
                    processed = U2NetRemover.Process(raw);
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
                else
                {
                    processed = raw;
                    finalBytes = imgBytes;
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
#pragma warning disable 618
            return !(request.isNetworkError || request.isHttpError);
#pragma warning restore 618
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

        private static Pawn FindPawnById(string thingId)
        {
            if (string.IsNullOrEmpty(thingId)) return null;

            Pawn selected = Find.Selector.SingleSelectedThing as Pawn;
            if (selected != null && selected.ThingID == thingId) return selected;

            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map.mapPawns != null)
                    {
                        foreach (Pawn p in map.mapPawns.AllPawns)
                        {
                            if (p != null && p.ThingID == thingId)
                                return p;
                        }
                    }
                }
            }

            if (Find.World != null && Find.World.worldPawns != null)
            {
                foreach (Pawn p in Find.World.worldPawns.AllPawnsAliveOrDead)
                {
                    if (p != null && p.ThingID == thingId)
                        return p;
                }
            }

            return null;
        }

        private static Texture2D GetReadableNativePortraitTexture(Pawn pawn, bool excludeHelmet)
        {
            if (pawn == null) return null;
            bool wasGenerating = isGeneratingRefPortrait;
            try
            {
                if (excludeHelmet)
                {
                    isGeneratingRefPortrait = true;
                    RimWorld.PortraitsCache.SetDirty(pawn);
                }

                RenderTexture originalTex = RimWorld.PortraitsCache.Get(pawn, new UnityEngine.Vector2(256f, 256f), Rot4.South);
                if (originalTex == null) return null;

                RenderTexture temp = RenderTexture.GetTemporary(originalTex.width, originalTex.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                Graphics.Blit(originalTex, temp);

                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = temp;

                Texture2D readableTex = new Texture2D(originalTex.width, originalTex.height, TextureFormat.RGBA32, false);
                readableTex.ReadPixels(new Rect(0f, 0f, temp.width, temp.height), 0, 0);
                readableTex.Apply();

                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(temp);

                return readableTex;
            }
            catch (Exception ex)
            {
                Log.Warning("[Dynamic AI Portraits] Failed to render readable native portrait: " + ex.Message);
                return null;
            }
            finally
            {
                if (excludeHelmet)
                {
                    isGeneratingRefPortrait = wasGenerating;
                    RimWorld.PortraitsCache.SetDirty(pawn);
                }
            }
        }

        private static string NormalizeGearName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in input.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static byte[] BuildReferenceSheet(PawnState state)
        {
            if (state == null) return null;
            try
            {
                if (AIPortraitsMod.Instance == null || AIPortraitsMod.Instance.Content == null)
                {
                    return null;
                }
                string spritesDir = System.IO.Path.Combine(AIPortraitsMod.Instance.Content.RootDir, "Sprites");
                Dictionary<string, string> spriteMap = new Dictionary<string, string>();
                if (System.IO.Directory.Exists(spritesDir))
                {
                    string[] files = System.IO.Directory.GetFiles(spritesDir, "*.png");
                    if (files != null)
                    {
                        foreach (string file in files)
                        {
                            string filename = System.IO.Path.GetFileNameWithoutExtension(file);
                            string norm = NormalizeGearName(filename);
                            if (!string.IsNullOrEmpty(norm) && !spriteMap.ContainsKey(norm))
                            {
                                spriteMap[norm] = file;
                            }
                        }
                    }
                }

                List<Texture2D> texturesToCombine = new List<Texture2D>();

                // The native in-game portrait is the "portrait" half of the reference sheet, so it is
                // included ONLY when the per-image "Reference portrait image" toggle is on. This is what
                // makes the reference image reflect that toggle (off => no portrait reference at all).
                if (state.refPortrait)
                {
                    Pawn pawn = FindPawnById(state.pawnId);
                    if (pawn != null)
                    {
                        Texture2D nativeTex = GetReadableNativePortraitTexture(pawn, state.excludeHelmet);
                        if (nativeTex != null)
                        {
                            texturesToCombine.Add(nativeTex);
                        }
                    }
                }

                // The gear sprites are the "gear" half of the reference sheet, gated by the per-image
                // "Use gear reference sheet" toggle.
                List<string> gearItems = new List<string>();
                if (state.useGearRef)
                {
                    if (!string.IsNullOrEmpty(state.primaryWeapon))
                    {
                        gearItems.Add(state.primaryWeapon);
                    }
                    if (state.apparel != null)
                    {
                        foreach (string app in state.apparel)
                        {
                            if (!string.IsNullOrEmpty(app))
                            {
                                gearItems.Add(app);
                            }
                        }
                    }
                }

                foreach (string gear in gearItems)
                {
                    string normGear = NormalizeGearName(gear);
                    if (string.IsNullOrEmpty(normGear)) continue;

                    string bestMatchPath = null;
                    int bestMatchLength = 0;

                    foreach (KeyValuePair<string, string> kvp in spriteMap)
                    {
                        if (normGear.Contains(kvp.Key))
                        {
                            if (kvp.Key.Length > bestMatchLength)
                            {
                                bestMatchLength = kvp.Key.Length;
                                bestMatchPath = kvp.Value;
                            }
                        }
                    }

                    if (bestMatchPath != null)
                    {
                        try
                        {
                            byte[] data = System.IO.File.ReadAllBytes(bestMatchPath);
                            if (data != null && data.Length > 0)
                            {
                                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                                if (ImageConversion.LoadImage(tex, data))
                                {
                                    texturesToCombine.Add(tex);
                                }
                                else
                                {
                                    UnityEngine.Object.Destroy(tex);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[Dynamic AI Portraits] Failed to load sprite texture from " + bestMatchPath + ": " + ex.Message);
                        }
                    }
                }

                if (texturesToCombine.Count == 0) return null;

                int padding = 16;
                int totalWidth = padding;
                int maxHeight = 0;

                foreach (Texture2D tex in texturesToCombine)
                {
                    totalWidth += tex.width + padding;
                    if (tex.height > maxHeight) maxHeight = tex.height;
                }
                maxHeight += padding * 2;

                // White background — cache the color once
                Color white = new Color(1f, 1f, 1f, 1f);
                Color[] destPixels = new Color[totalWidth * maxHeight];
                for (int i = 0; i < destPixels.Length; i++)
                {
                    destPixels[i] = white;
                }

                int currentX = padding;
                foreach (Texture2D tex in texturesToCombine)
                {
                    // Bulk-fetch the entire source pixel array once (much faster than per-pixel
                    // GetPixel, which marshals every call across the managed/native boundary).
                    int sw = tex.width;
                    int sh = tex.height;
                    Color[] src = tex.GetPixels();

                    int startY = padding + (maxHeight - padding * 2 - sh) / 2;
                    for (int y = 0; y < sh; y++)
                    {
                        int destRow = (startY + y) * totalWidth + currentX;
                        int srcRow  = y * sw;
                        for (int x = 0; x < sw; x++)
                        {
                            Color pixel = src[srcRow + x];
                            if (pixel.a > 0.01f)
                            {
                                Color blended = Color.Lerp(white, pixel, pixel.a);
                                blended.a = 1f;
                                destPixels[destRow + x] = blended;
                            }
                        }
                    }
                    currentX += sw + padding;
                }

                Texture2D combined = new Texture2D(totalWidth, maxHeight, TextureFormat.RGBA32, false);
                combined.SetPixels(destPixels);
                combined.Apply();
                byte[] pngBytes = ImageConversion.EncodeToPNG(combined);

                UnityEngine.Object.Destroy(combined);
                foreach (Texture2D tex in texturesToCombine)
                {
                    UnityEngine.Object.Destroy(tex);
                }

                return pngBytes;
            }
            catch (Exception ex)
            {
                Log.Warning("[Dynamic AI Portraits] Error building combined reference sheet: " + ex.Message);
                return null;
            }
        }

        // Merges the two possible reference inputs (the identity-continuity portrait and the
        // gear/native-portrait reference sheet) into ONE image so each generation sends a single
        // image input. Returns null if both are empty, the lone input if only one is present, and
        // a side-by-side stitch (portrait LEFT, gear sheet RIGHT) on a white canvas when both exist.
        private static byte[] CombineReferenceImages(byte[] a, byte[] b)
        {
            bool hasA = a != null && a.Length > 0;
            bool hasB = b != null && b.Length > 0;
            if (!hasA && !hasB) return null;
            if (hasA && !hasB) return a;
            if (!hasA && hasB) return b;

            Texture2D ta = null;
            Texture2D tb = null;
            Texture2D combined = null;
            try
            {
                ta = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tb = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(ta, a) || !ImageConversion.LoadImage(tb, b))
                {
                    return a; // fall back to the identity portrait
                }

                int padding = 16;
                int totalWidth = ta.width + tb.width + padding * 3;
                int maxHeight = (ta.height > tb.height ? ta.height : tb.height) + padding * 2;

                Color white = new Color(1f, 1f, 1f, 1f);
                Color[] destPixels = new Color[totalWidth * maxHeight];
                for (int i = 0; i < destPixels.Length; i++)
                {
                    destPixels[i] = white;
                }

                Texture2D[] parts = new Texture2D[] { ta, tb };
                int currentX = padding;
                for (int p = 0; p < parts.Length; p++)
                {
                    Texture2D tex = parts[p];
                    int sw = tex.width;
                    int sh = tex.height;
                    Color[] src = tex.GetPixels();

                    int startY = padding + (maxHeight - padding * 2 - sh) / 2;
                    for (int y = 0; y < sh; y++)
                    {
                        int destRow = (startY + y) * totalWidth + currentX;
                        int srcRow = y * sw;
                        for (int x = 0; x < sw; x++)
                        {
                            Color pixel = src[srcRow + x];
                            if (pixel.a > 0.01f)
                            {
                                Color blended = Color.Lerp(white, pixel, pixel.a);
                                blended.a = 1f;
                                destPixels[destRow + x] = blended;
                            }
                        }
                    }
                    currentX += sw + padding;
                }

                combined = new Texture2D(totalWidth, maxHeight, TextureFormat.RGBA32, false);
                combined.SetPixels(destPixels);
                combined.Apply();
                return ImageConversion.EncodeToPNG(combined);
            }
            catch (Exception ex)
            {
                Log.Warning("[Dynamic AI Portraits] Failed to combine reference images: " + ex.Message);
                return a;
            }
            finally
            {
                if (ta != null) UnityEngine.Object.Destroy(ta);
                if (tb != null) UnityEngine.Object.Destroy(tb);
                if (combined != null) UnityEngine.Object.Destroy(combined);
            }
        }

        // ── Google Veo 3.1 Lite Video generation ─────────────────────────────────────
        public static void QueueVideoGeneration(Pawn pawn, PawnState state, byte[] initImageBytes, string apiKey, Action<string, string> callback)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                callback(null, "Google API Key is not configured. Please set the Google Imagen API Key in Mod Settings.");
                return;
            }

            if (initImageBytes == null || initImageBytes.Length == 0)
            {
                callback(null, "No static portrait image is available to animate.");
                return;
            }

            CoroutineRunner.Instance.StartCoroutine(GenerateVeoVideoCoroutine(pawn, state, initImageBytes, apiKey, callback));
        }

        private static IEnumerator GenerateVeoVideoCoroutine(Pawn pawn, PawnState state, byte[] initImageBytes, string apiKey, Action<string, string> callback)
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/models/veo-3.1-lite-generate-preview:predictLongRunning?key=" + apiKey;

            // Framing first — it controls both the background directive and the aspect ratio.
            string framing = (state != null && !string.IsNullOrEmpty(state.framing)) ? state.framing : "portrait";

            string basePrompt = PromptCompiler.CompilePositivePrompt(state, AIPortraitsMod.settings, null);
            string motion = ", subtle natural idle motion only: hair and loose clothing drifting gently as if in a soft breeze, slow calm breathing, an occasional natural blink, a faint relaxed shift of weight, eyes calmly on the viewer; no large gestures, no walking, hands and any held item stay perfectly still (no waving), seamless loop with identical first and last frames, steady locked-off camera, high quality";
            string bgDirective;
            if (framing == "special")
            {
                // 'special' keeps its scenic background (it is NOT matted) — let it stay cinematic.
                bgDirective = ", cinematic, masterpiece";
            }
            else
            {
                // portrait/bodyshot get background-removed. Veo otherwise ANIMATES the backdrop
                // (drifting light / gradients), and that moving, non-uniform background is what
                // makes the matte flicker and leave black/white patches. Force a perfectly STATIC,
                // UNIFORM, MONO backdrop so background removal is clean and temporally stable.
                bgDirective = ", solid flat uniform mono background, completely static unchanging background, the background stays perfectly still and identical in every single frame, no background motion, no background lighting changes, no gradients, no shadows cast on the background, plain seamless backdrop, only the character moves";
            }
            string prompt = basePrompt + bgDirective + motion + ". Audio: silent or only soft ambient wind with faint distant birdsong, no music, no speech, no dialogue, quiet and unobtrusive.";

            // Match aspect ratio to framing:
            // portrait / bodyshot → 9:16 (vertical, matches tall portrait images)
            // special             → 16:9 (landscape)
            // Veo 3.1 Lite only supports 9:16 and 16:9; 1:1 is not supported.
            string aspectRatio = (framing == "special") ? "16:9" : "9:16";

            string diskKey = AIPortraitsManager.GetActiveKeyForFraming(pawn, framing);

            string base64Image = Convert.ToBase64String(initImageBytes);

            // Correct image-to-video payload: 'image' key lives directly inside instance,
            // NOT inside a 'reference_images' array. mimeType is camelCase.
            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"instances\":[{");
            json.Append("\"prompt\":\"").Append(EscapeJson(prompt)).Append("\",");
            json.Append("\"image\":{");
            json.Append("\"bytesBase64Encoded\":\"").Append(base64Image).Append("\",");
            json.Append("\"mimeType\":\"image/png\"");
            json.Append("}");
            json.Append("}],");
            json.Append("\"parameters\":{");
            json.Append("\"sampleCount\":1,");
            // NOTE: veo-3.1-lite rejects "generateAudio" (HTTP 400 "isn't supported by this model").
            // Audio is steered to be quiet via the prompt instead; do not re-add this parameter.
            json.Append("\"aspectRatio\":\"").Append(aspectRatio).Append("\",");
            json.Append("\"durationSeconds\":4,");
            json.Append("\"resolution\":\"720p\"");
            json.Append("}");
            json.Append("}");

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToString());

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(jsonBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 60;

                Log.Message("[Dynamic AI Portraits] Initiating Veo Video for " + pawn.LabelShortCap + " (" + framing + ")...");
                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    // Inline scrub (apiKey is a coroutine param, not on settings)
                    string errMsg = "Veo long-running start error: " + request.error + " | " + Truncate(request.downloadHandler.text, 400);
                    if (!string.IsNullOrEmpty(apiKey))
                        errMsg = errMsg.Replace(apiKey, "[REDACTED]");
                    callback(null, errMsg);
                    yield break;
                }

                string text = request.downloadHandler.text;
                int nameIdx = text.IndexOf("\"name\"");
                if (nameIdx == -1)
                {
                    callback(null, "Could not find operation 'name' in Veo response. Response: " + text);
                    yield break;
                }

                int colonIdx = text.IndexOf(':', nameIdx);
                int openQuote = text.IndexOf('"', colonIdx + 1);
                int closeQuote = text.IndexOf('"', openQuote + 1);
                if (openQuote == -1 || closeQuote == -1)
                {
                    callback(null, "Malformed operation name in Veo response. Response: " + text);
                    yield break;
                }

                string operationName = text.Substring(openQuote + 1, closeQuote - openQuote - 1);
                Log.Message("[Dynamic AI Portraits] Veo operation started: " + operationName + ". Polling status...");

                yield return CoroutineRunner.Instance.StartCoroutine(PollVeoOperation(operationName, apiKey, delegate(string videoUri, string pollErr)
                {
                    if (pollErr != null)
                    {
                        callback(null, pollErr);
                    }
                    else if (videoUri != null)
                    {
                        string downloadUrl = videoUri.Contains("?") ? (videoUri + "&key=" + apiKey) : (videoUri + "?key=" + apiKey);
                        CoroutineRunner.Instance.StartCoroutine(DownloadVideoBytes(downloadUrl, delegate(byte[] videoBytes, string dlErr)
                        {
                            if (dlErr != null)
                            {
                                callback(null, dlErr);
                            }
                            else if (videoBytes != null)
                            {
                                string videoPath = System.IO.Path.Combine(CacheManager.GetCacheDirectory(), diskKey + ".mp4");
                                try
                                {
                                    System.IO.File.WriteAllBytes(videoPath, videoBytes);
                                    Log.Message("[Dynamic AI Portraits] Saved generated Veo video to: " + videoPath);

                                    string dir = CacheManager.GetPortraitSaveDirectory(pawn);
                                    string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                                    string styleName = AIPortraitsMod.settings.portraitStyle.ToString();
                                    string file = pawn.LabelShortCap + "_" + styleName + "_" + framing + "_" + ts + ".mp4";
                                    string userPath = System.IO.Path.Combine(dir, file);
                                    System.IO.File.WriteAllBytes(userPath, videoBytes);

                                    callback(videoPath, null);
                                }
                                catch (Exception ex)
                                {
                                    callback(null, "Failed to save downloaded video to file: " + ex.Message);
                                }
                            }
                        }));
                    }
                }));
            }
        }

        private static IEnumerator PollVeoOperation(string operationName, string apiKey, Action<string, string> callback)
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/" + operationName + "?key=" + apiKey;
            int attempts = 0;
            const int maxAttempts = 30; // 10 seconds * 30 = 300 seconds (5 minutes) max polling time

            while (attempts < maxAttempts)
            {
                yield return new WaitForSeconds(10f);
                attempts++;
                Log.Message("[Dynamic AI Portraits] Veo poll attempt " + attempts + "/" + maxAttempts + "...");

                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = 30;
                    yield return request.SendWebRequest();

                    if (!IsSuccess(request))
                    {
                        // request.error may contain the URL (with ?key=) on certain HTTP failures.
                        // Inline scrub since this coroutine takes apiKey as a parameter, not settings.
                        string errMsg = "[Dynamic AI Portraits] Veo polling error: " + request.error;
                        if (!string.IsNullOrEmpty(apiKey))
                            errMsg = errMsg.Replace(apiKey, "[REDACTED]");
                        Log.Warning(errMsg);
                        continue;
                    }

                    string text = request.downloadHandler.text;
                    if (text.Contains("\"error\""))
                    {
                        string err = ParseVeoError(text);
                        callback(null, "Veo generation failed: " + (err ?? "unknown error"));
                        yield break;
                    }

                    if (ParseVeoDone(text))
                    {
                        string videoUri = ParseVeoVideoUri(text);
                        if (!string.IsNullOrEmpty(videoUri))
                        {
                            DebugLog.Log("API", "veo poll: DONE, video ready after " + attempts + " polls");
                            callback(videoUri, null);
                            yield break;
                        }
                        else
                        {
                            // Check for RAI (safety) filter rejection
                            string raiReason = ParseVeoRaiReason(text);
                            if (!string.IsNullOrEmpty(raiReason))
                            {
                                DebugLog.Log("API", "veo poll: SAFETY FILTER -> " + raiReason);
                                callback(null, "Veo safety filter: " + raiReason);
                            }
                            else
                            {
                                callback(null, "Veo: operation finished but no video was produced. Response: " + Truncate(text, 300));
                            }
                            yield break;
                        }
                    }
                }
            }

            // Wait interval is 10s (WaitForSeconds(10f)), not 5s — bug fixed during audit
            DebugLog.Log("API", "veo poll: TIMEOUT after " + (maxAttempts * 10) + "s (" + maxAttempts + " polls)");
            callback(null, "Veo video generation timed out after " + (maxAttempts * 10) + " seconds.");
        }

        private static IEnumerator DownloadVideoBytes(string downloadUrl, Action<byte[], string> callback)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(downloadUrl))
            {
                request.timeout = 60;
                yield return request.SendWebRequest();

                if (!IsSuccess(request))
                {
                    callback(null, "Failed to download video file: " + request.error);
                    yield break;
                }

                byte[] data = request.downloadHandler.data;
                if (data == null || data.Length == 0)
                {
                    callback(null, "Downloaded video file was empty.");
                    yield break;
                }

                callback(data, null);
            }
        }

        private static bool ParseVeoDone(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            int idx = json.IndexOf("\"done\"");
            if (idx < 0) return false;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return false;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i < json.Length && json.Substring(i).StartsWith("true"))
            {
                return true;
            }
            return false;
        }

        private static string ParseVeoVideoUri(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int generatedSamplesIdx = json.IndexOf("\"generatedSamples\"");
            if (generatedSamplesIdx < 0) generatedSamplesIdx = 0;

            int videoIdx = json.IndexOf("\"video\"", generatedSamplesIdx);
            if (videoIdx < 0) videoIdx = json.IndexOf("\"uri\"");
            if (videoIdx < 0) return null;

            int uriIdx = json.IndexOf("\"uri\"", videoIdx);
            if (uriIdx < 0) return null;

            int colonIdx = json.IndexOf(':', uriIdx);
            if (colonIdx < 0) return null;

            int openQuote = json.IndexOf('"', colonIdx + 1);
            if (openQuote == -1) return null;

            int closeQuote = json.IndexOf('"', openQuote + 1);
            if (closeQuote == -1) return null;

            return json.Substring(openQuote + 1, closeQuote - openQuote - 1);
        }

        private static string ParseVeoError(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int errorIdx = json.IndexOf("\"error\"");
            if (errorIdx < 0) return null;
            int messageIdx = json.IndexOf("\"message\"", errorIdx);
            if (messageIdx < 0) return "Unknown operation error";
            int colonIdx = json.IndexOf(':', messageIdx);
            if (colonIdx < 0) return "Unknown operation error";
            int openQuote = json.IndexOf('"', colonIdx + 1);
            if (openQuote == -1) return "Unknown operation error";
            int closeQuote = json.IndexOf('"', openQuote + 1);
            if (closeQuote == -1) return "Unknown operation error";
            return json.Substring(openQuote + 1, closeQuote - openQuote - 1);
        }

        /// <summary>Extracts the first raiMediaFilteredReasons message, if present.</summary>
        private static string ParseVeoRaiReason(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int idx = json.IndexOf("\"raiMediaFilteredReasons\"");
            if (idx < 0) return null;
            // Advance past the array open bracket
            int bracket = json.IndexOf('[', idx);
            if (bracket < 0) return null;
            int openQuote = json.IndexOf('"', bracket + 1);
            if (openQuote < 0) return null;
            int closeQuote = json.IndexOf('"', openQuote + 1);
            if (closeQuote < 0) return null;
            return json.Substring(openQuote + 1, closeQuote - openQuote - 1);
        }
    }
}
