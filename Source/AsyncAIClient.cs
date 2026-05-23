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
        public delegate void PortraitCallback(Texture2D texture, byte[] rawBytes, string error);

        private const int RequestTimeoutSeconds = 120;

        public static void QueueGeneration(PawnState state, AIPortraitsSettings settings, string continuityToken, PortraitCallback callback)
        {
            string positivePrompt = PromptCompiler.CompilePositivePrompt(state, settings, continuityToken);
            string negativePrompt = PromptCompiler.CompileNegativePrompt(settings);

            Log.Message("[Dynamic AI Portraits] Prompt: " + positivePrompt);

            switch (settings.backendType)
            {
                case BackendType.HuggingFace:
                    CoroutineRunner.Instance.StartCoroutine(GenerateHuggingFace(positivePrompt, negativePrompt, settings, callback));
                    break;
                case BackendType.Pollinations:
                    CoroutineRunner.Instance.StartCoroutine(GeneratePollinations(positivePrompt, settings, callback));
                    break;
                case BackendType.GoogleImagen:
                    CoroutineRunner.Instance.StartCoroutine(GenerateGoogleImagen(positivePrompt, negativePrompt, settings, callback));
                    break;
                default:
                    callback(null, null, "Backend type " + settings.backendType + " is not implemented.");
                    break;
            }
        }

        // ── HuggingFace ──────────────────────────────────────────────────────────────
        private static IEnumerator GenerateHuggingFace(string prompt, string negativePrompt, AIPortraitsSettings settings, PortraitCallback callback)
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
                    callback(null, null, "HuggingFace API Error: " + request.error + " - " + Truncate(request.downloadHandler.text, 400));
                    yield break;
                }

                byte[] imgBytes = request.downloadHandler.data;
                if (imgBytes == null || imgBytes.Length == 0)
                {
                    callback(null, null, "Empty response body from HuggingFace.");
                    yield break;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, imgBytes))
                    callback(texture, imgBytes, null);
                else
                    callback(null, null, "Failed to load image bytes from HuggingFace.");
            }
        }

        // ── Pollinations ─────────────────────────────────────────────────────────────
        private static IEnumerator GeneratePollinations(string prompt, AIPortraitsSettings settings, PortraitCallback callback)
        {
            string baseUrl = string.IsNullOrEmpty(settings.apiUrl) ? "https://image.pollinations.ai" : settings.apiUrl.TrimEnd('/');
            string model = string.IsNullOrEmpty(settings.modelName) ? "flux" : settings.modelName;

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
                    callback(null, null, "Pollinations API Error: " + request.error);
                    yield break;
                }

                byte[] imgBytes = request.downloadHandler.data;
                if (imgBytes == null || imgBytes.Length == 0)
                {
                    callback(null, null, "Empty response body from Pollinations.");
                    yield break;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, imgBytes))
                    callback(texture, imgBytes, null);
                else
                    callback(null, null, "Failed to load image bytes from Pollinations.");
            }
        }

        // ── Google Imagen ────────────────────────────────────────────────────────────
        private static IEnumerator GenerateGoogleImagen(string prompt, string negativePrompt, AIPortraitsSettings settings, PortraitCallback callback)
        {
            string baseUrl = string.IsNullOrEmpty(settings.apiUrl) ? "https://generativelanguage.googleapis.com" : settings.apiUrl.TrimEnd('/');
            string model = string.IsNullOrEmpty(settings.modelName) ? "imagen-3.0-fast-generate-001" : settings.modelName;
            string url = baseUrl + "/v1beta/models/" + model + ":predict";

            string fullPrompt = PromptCompiler.CompileImagenSystemPrompt(settings.portraitStyle) + "\n\n" + prompt;
            string imagenNeg  = PromptCompiler.CompileImagenNegativePrompt();

            StringBuilder json = new StringBuilder();
            json.Append("{");
            json.Append("\"instances\":[{");
            json.Append("\"prompt\":\"").Append(EscapeJson(fullPrompt)).Append("\"");
            json.Append("}],");
            json.Append("\"parameters\":{");
            json.Append("\"sampleCount\":1,");
            json.Append("\"aspectRatio\":\"1:1\",");
            json.Append("\"outputMimeType\":\"image/png\"");
            json.Append(",\"negativePrompt\":\"").Append(EscapeJson(imagenNeg)).Append("\"");
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
                    callback(null, null, "Google Imagen API Error: " + request.error + " | " + Truncate(request.downloadHandler.text, 400));
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
                        callback(null, null, "Could not find 'bytesBase64Encoded' in response. Full response: " + Truncate(text, 400));
                        yield break;
                    }

                    int colonIdx = text.IndexOf(':', keyIdx);
                    int openQuote = text.IndexOf('"', colonIdx + 1);
                    int closeQuote = text.IndexOf('"', openQuote + 1);
                    if (openQuote == -1 || closeQuote == -1)
                    {
                        callback(null, null, "Malformed bytesBase64Encoded value.");
                        yield break;
                    }

                    string base64 = text.Substring(openQuote + 1, closeQuote - openQuote - 1);
                    byte[] imgBytes = Convert.FromBase64String(base64);

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(texture, imgBytes))
                        callback(texture, imgBytes, null);
                    else
                        callback(null, null, "Failed to decode image from Google Imagen bytes.");
                }
                catch (Exception ex)
                {
                    callback(null, null, "Google Imagen parse exception: " + ex.Message);
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

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
