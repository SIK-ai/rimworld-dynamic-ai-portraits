using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.Sound;

namespace AIPortraits
{
    public enum BackendType
    {
        Replicate,
        HuggingFace,
        Pollinations,
        GoogleImagen,
        LocalA1111,    // AUTOMATIC1111 / Forge / SD.Next / ComfyUI (A1111-compat) on localhost
        Cloudflare,    // Cloudflare Workers AI — FLUX.1 Schnell, SDXL, etc. Free 10k req/day.
        DeepInfra,     // DeepInfra — FLUX/SDXL, OpenAI-style API, ~$0.0005/img
        Custom
    }

    public enum PortraitStyle
    {
        Realistic_Korean,   // Korean webtoon manhwa (Solo Leveling style)
        Realistic_Western,  // Rick and Morty Adult Swim cartoon style
        DotPixel            // 16-bit pixel art / retro JRPG sprite
    }

    public enum LLMModelType
    {
        GeminiFlashLite,
        Gemma26B,
        Gemma31B
    }

    public class AIPortraitsSettings : ModSettings
    {
        public BackendType backendType = BackendType.Pollinations;
        public LLMModelType llmModelType = LLMModelType.GeminiFlashLite;

        // Legacy (kept for migration but not actively used for storage)
        public string apiKey = "";
        public string apiUrl = "";
        public string modelName = "";

        // Distinct Backend Settings
        public string cfApiKey = "";
        public string cfApiUrl = "https://api.cloudflare.com";
        public string cfModelName = "@cf/black-forest-labs/flux-1-schnell";

        public string giApiKey = "";
        public string giApiUrl = "https://generativelanguage.googleapis.com";
        public string giModelName = "imagen-4.0-fast-generate-001";

        public string diApiKey = "";
        public string diApiUrl = "https://api.deepinfra.com";
        public string diModelName = "black-forest-labs/FLUX-1-schnell";

        public string hfApiKey = "";
        public string hfApiUrl = "https://api-inference.huggingface.co";
        public string hfModelName = "black-forest-labs/FLUX.1-schnell";

        public string localApiUrl = "http://127.0.0.1:7860";

        public string polApiUrl = "https://image.pollinations.ai";
        public string polModelName = "sana";

        public string CurrentApiKey
        {
            get
            {
                switch (backendType)
                {
                    case BackendType.Cloudflare: return cfApiKey;
                    case BackendType.GoogleImagen: return giApiKey;
                    case BackendType.DeepInfra: return diApiKey;
                    case BackendType.HuggingFace: return hfApiKey;
                    default: return "";
                }
            }
            set
            {
                switch (backendType)
                {
                    case BackendType.Cloudflare: cfApiKey = value; break;
                    case BackendType.GoogleImagen: giApiKey = value; break;
                    case BackendType.DeepInfra: diApiKey = value; break;
                    case BackendType.HuggingFace: hfApiKey = value; break;
                }
            }
        }

        public string CurrentApiUrl
        {
            get
            {
                switch (backendType)
                {
                    case BackendType.Cloudflare: return cfApiUrl;
                    case BackendType.GoogleImagen: return giApiUrl;
                    case BackendType.DeepInfra: return diApiUrl;
                    case BackendType.HuggingFace: return hfApiUrl;
                    case BackendType.LocalA1111: return localApiUrl;
                    case BackendType.Pollinations: return polApiUrl;
                    default: return "";
                }
            }
            set
            {
                switch (backendType)
                {
                    case BackendType.Cloudflare: cfApiUrl = value; break;
                    case BackendType.GoogleImagen: giApiUrl = value; break;
                    case BackendType.DeepInfra: diApiUrl = value; break;
                    case BackendType.HuggingFace: hfApiUrl = value; break;
                    case BackendType.LocalA1111: localApiUrl = value; break;
                    case BackendType.Pollinations: polApiUrl = value; break;
                }
            }
        }

        public string CurrentModelName
        {
            get
            {
                switch (backendType)
                {
                    case BackendType.Cloudflare: return cfModelName;
                    case BackendType.GoogleImagen: return giModelName;
                    case BackendType.DeepInfra: return diModelName;
                    case BackendType.HuggingFace: return hfModelName;
                    case BackendType.Pollinations: return polModelName;
                    default: return "";
                }
            }
            set
            {
                switch (backendType)
                {
                    case BackendType.Cloudflare: cfModelName = value; break;
                    case BackendType.GoogleImagen: giModelName = value; break;
                    case BackendType.DeepInfra: diModelName = value; break;
                    case BackendType.HuggingFace: hfModelName = value; break;
                    case BackendType.Pollinations: polModelName = value; break;
                }
            }
        }

        public PortraitStyle portraitStyle = PortraitStyle.Realistic_Korean;

        // User-appended style suffix (overrides nothing, just appends)
        public string baseStylePrompt = "";
        public string manhwaStylePrompt = "flat 2D webtoon manhwa drawing, hand-drawn digital illustration, bold clean inked outlines, flat cel-shaded color fills, hard-edged anime shadows, bright saturated CMYK print-style colors, expressive stylized anime eyes, glossy stylized hair, crisp clean line art, masterpiece webtoon key visual, strictly 2D flat art, smooth flat skin rendering, no lens blur, no depth of field";
        public string cartoonStylePrompt = "Rick and Morty Adult Swim cartoon character, Justin Roiland animation style, thick consistent black outlines, flat 2D color fills, no gradients, no painterly shading, bulging round cartoon eyes with tiny black dot pupils, exaggerated wonky proportions, oversized head, hand-drawn animation cel look, bright primary color palette";
        public string pixelStylePrompt = "high-quality 16-bit retro JRPG character sprite, Tactics Ogre and Final Fantasy Tactics style, clean pixel-art grid, zero anti-aliasing, sharp deliberate pixel edges, thin consistent dark outlines, clean flat cel-shading, limited color palette, anime-style cute facial features, detailed hair";
        public string baseNegativePrompt = "generic fantasy art, cartoon style, bright cheerful lighting, flat lighting, blurry textures, generic features, messy brushstrokes, standard clean weapon, generic clothing, missing face tattoos, missing horns, missing cybernetic eyes, missing scars, photorealistic photograph, 3d render, chibi, flat shading, low quality, watermark, extra limbs, deformed face, bad anatomy, multiple people, text, signature, anti-aliased pixels, jagged irregular lines, muddied unclear character design, illegible text, generic UI, inconsistency between sprite and portrait";

        public float cfgScale = 7f;
        public int steps = 20;

        public float portraitScale = 260f;
        public float portraitOffsetX = 0f;
        public float portraitOffsetY = 0f;

        public bool includeIdeology = true;
        public bool includeRimLighting = true;
        public bool useGearReferenceSheet = true;
        public bool excludeHelmet = false;

        // LLM-assisted prompt generation (Gemini Flash)
        public bool   useLLMPrompt = false;
        public string llmApiKey    = "";
        public string videoApiKey  = "";

        // AI Background Removal (Cloudflare Bria-RMBG-1.4)
        public bool useAIBgRemoval = false;
        public string cfBgRemovalKey = "";

        private Vector2 scrollPosition = Vector2.zero;

        public Dictionary<string, string> activePortraits = new Dictionary<string, string>();
        public Dictionary<string, string> pawnFraming = new Dictionary<string, string>();
        public Dictionary<string, bool> pawnVideoToggles = new Dictionary<string, bool>();

        private List<string> activePortraitsKeys;
        private List<string> activePortraitsValues;
        private List<string> pawnFramingKeys;
        private List<string> pawnFramingValues;
        private List<string> pawnVideoTogglesKeys;
        private List<bool> pawnVideoTogglesValues;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref backendType, "backendType", BackendType.Pollinations);
            Scribe_Values.Look(ref apiKey, "apiKey", "");
            Scribe_Values.Look(ref apiUrl, "apiUrl", "");
            Scribe_Values.Look(ref modelName, "modelName", "");

            Scribe_Values.Look(ref cfApiKey, "cfApiKey", "");
            Scribe_Values.Look(ref cfApiUrl, "cfApiUrl", "https://api.cloudflare.com");
            Scribe_Values.Look(ref cfModelName, "cfModelName", "@cf/black-forest-labs/flux-1-schnell");

            Scribe_Values.Look(ref giApiKey, "giApiKey", "");
            Scribe_Values.Look(ref giApiUrl, "giApiUrl", "https://generativelanguage.googleapis.com");
            Scribe_Values.Look(ref giModelName, "giModelName", "imagen-4.0-fast-generate-001");

            Scribe_Values.Look(ref diApiKey, "diApiKey", "");
            Scribe_Values.Look(ref diApiUrl, "diApiUrl", "https://api.deepinfra.com");
            Scribe_Values.Look(ref diModelName, "diModelName", "black-forest-labs/FLUX-1-schnell");

            Scribe_Values.Look(ref hfApiKey, "hfApiKey", "");
            Scribe_Values.Look(ref hfApiUrl, "hfApiUrl", "https://api-inference.huggingface.co");
            Scribe_Values.Look(ref hfModelName, "hfModelName", "black-forest-labs/FLUX.1-schnell");

            Scribe_Values.Look(ref localApiUrl, "localApiUrl", "http://127.0.0.1:7860");

            Scribe_Values.Look(ref polApiUrl, "polApiUrl", "https://image.pollinations.ai");
            Scribe_Values.Look(ref polModelName, "polModelName", "sana");

            Scribe_Values.Look(ref portraitStyle, "portraitStyle", PortraitStyle.Realistic_Korean);
            Scribe_Values.Look(ref baseStylePrompt, "baseStylePrompt", "");
            Scribe_Values.Look(ref manhwaStylePrompt, "manhwaStylePrompt", "flat 2D webtoon manhwa drawing, hand-drawn digital illustration, bold clean inked outlines, flat cel-shaded color fills, hard-edged anime shadows, bright saturated CMYK print-style colors, expressive stylized anime eyes, glossy stylized hair, crisp clean line art, masterpiece webtoon key visual, strictly 2D flat art, smooth flat skin rendering, no lens blur, no depth of field");
            Scribe_Values.Look(ref cartoonStylePrompt, "cartoonStylePrompt", "Rick and Morty Adult Swim cartoon character, Justin Roiland animation style, thick consistent black outlines, flat 2D color fills, no gradients, no painterly shading, bulging round cartoon eyes with tiny black dot pupils, exaggerated wonky proportions, oversized head, hand-drawn animation cel look, bright primary color palette");
            Scribe_Values.Look(ref pixelStylePrompt, "pixelStylePrompt", "high-quality 16-bit retro JRPG character sprite, Tactics Ogre and Final Fantasy Tactics style, clean pixel-art grid, zero anti-aliasing, sharp deliberate pixel edges, thin consistent dark outlines, clean flat cel-shading, limited color palette, anime-style cute facial features, detailed hair");
            Scribe_Values.Look(ref baseNegativePrompt, "baseNegativePrompt", "generic fantasy art, cartoon style, bright cheerful lighting, flat lighting, blurry textures, generic features, messy brushstrokes, standard clean weapon, generic clothing, missing face tattoos, missing horns, missing cybernetic eyes, missing scars, photorealistic photograph, 3d render, chibi, flat shading, low quality, watermark, extra limbs, deformed face, bad anatomy, multiple people, text, signature, anti-aliased pixels, jagged irregular lines, muddied unclear character design, illegible text, generic UI, inconsistency between sprite and portrait");
            Scribe_Values.Look(ref cfgScale, "cfgScale", 7f);
            Scribe_Values.Look(ref steps, "steps", 20);
            Scribe_Values.Look(ref portraitScale, "portraitScale", 260f);
            Scribe_Values.Look(ref portraitOffsetX, "portraitOffsetX", 0f);
            Scribe_Values.Look(ref portraitOffsetY, "portraitOffsetY", 0f);
            Scribe_Values.Look(ref includeIdeology,    "includeIdeology",  true);
            Scribe_Values.Look(ref includeRimLighting, "includeRimLighting", true);
            Scribe_Values.Look(ref useGearReferenceSheet, "useGearReferenceSheet", true);
            Scribe_Values.Look(ref excludeHelmet,      "excludeHelmet",    false);
            Scribe_Values.Look(ref useLLMPrompt,       "useLLMPrompt",     false);
            Scribe_Values.Look(ref llmModelType,       "llmModelType",     LLMModelType.GeminiFlashLite);
            Scribe_Values.Look(ref llmApiKey,          "llmApiKey",        "");
            Scribe_Values.Look(ref videoApiKey,        "videoApiKey",      "");
            Scribe_Values.Look(ref useAIBgRemoval,     "useAIBgRemoval",   false);
            Scribe_Values.Look(ref cfBgRemovalKey,     "cfBgRemovalKey",   "");
            Scribe_Collections.Look(ref activePortraits, "activePortraits", LookMode.Value, LookMode.Value, ref activePortraitsKeys, ref activePortraitsValues);
            Scribe_Collections.Look(ref pawnFraming, "pawnFraming", LookMode.Value, LookMode.Value, ref pawnFramingKeys, ref pawnFramingValues);
            Scribe_Collections.Look(ref pawnVideoToggles, "pawnVideoToggles", LookMode.Value, LookMode.Value, ref pawnVideoTogglesKeys, ref pawnVideoTogglesValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!string.IsNullOrEmpty(apiKey))
                {
                    CurrentApiKey = apiKey;
                    apiKey = "";
                }
                if (!string.IsNullOrEmpty(apiUrl))
                {
                    CurrentApiUrl = apiUrl;
                    apiUrl = "";
                }
                if (!string.IsNullOrEmpty(modelName))
                {
                    CurrentModelName = modelName;
                    modelName = "";
                }

                if (activePortraits == null)
                    activePortraits = new Dictionary<string, string>();
                if (pawnFraming == null)
                    pawnFraming = new Dictionary<string, string>();
                if (pawnVideoToggles == null)
                    pawnVideoToggles = new Dictionary<string, bool>();

                if (manhwaStylePrompt != null && (manhwaStylePrompt.Contains("no realistic skin texture") || manhwaStylePrompt.Contains("non-photorealistic")))
                {
                    manhwaStylePrompt = "flat 2D webtoon manhwa drawing, hand-drawn digital illustration, bold clean inked outlines, flat cel-shaded color fills, hard-edged anime shadows, bright saturated CMYK print-style colors, expressive stylized anime eyes, glossy stylized hair, crisp clean line art, masterpiece webtoon key visual, strictly 2D flat art, smooth flat skin rendering, no lens blur, no depth of field";
                }
            }
        }

        // UI states (not serialized)
        private int activeTab = 0; // 0 = API Settings, 1 = Pawn Gallery, 2 = Prompt Engineering
        private bool showAdvanced = false;
        private Vector2 leftScrollPosition = Vector2.zero;
        private Vector2 rightScrollPosition = Vector2.zero;
        private Vector2 vibeScrollPosition = Vector2.zero;
        private Vector2 promptScrollPosition = Vector2.zero;
        private Pawn selectedPawn = null;
        private SavedPortrait selectedSavedPortrait = null;
        private string customPromptBuffer = "";

        // Prompt Engineering tab — its own buffer + selection so edits never clobber the Gallery's.
        private Vector2 promptEngLeftScroll = Vector2.zero;
        private string promptEngBuffer = "";
        private SavedPortrait promptEngSelectedPortrait = null;
        private bool promptEngShowLastSent = true;
        private bool promptEngShowLlmSystem = false;

        public class SavedPortrait
        {
            public string pngPath;
            public string txtPath;
            public Texture2D texture;
            public string prompt;
            public string styleName;
            public string framingName;
            public string timestamp;

            // Reference textures
            public Texture2D refGearTexture;
            public Texture2D refPortraitTexture;
        }

        private Pawn lastCachedPawn = null;
        private List<SavedPortrait> cachedSavedPortraits = new List<SavedPortrait>();

        private void RefreshPawnPortraitsCache(Pawn pawn)
        {
            selectedSavedPortrait = null;
            customPromptBuffer = "";
            promptEngSelectedPortrait = null;
            promptEngBuffer = "";

            // Destroy textures to avoid memory leaks
            foreach (var sp in cachedSavedPortraits)
            {
                if (sp.texture != null)
                {
                    UnityEngine.Object.Destroy(sp.texture);
                }
                if (sp.refGearTexture != null)
                {
                    UnityEngine.Object.Destroy(sp.refGearTexture);
                }
                if (sp.refPortraitTexture != null)
                {
                    UnityEngine.Object.Destroy(sp.refPortraitTexture);
                }
            }
            cachedSavedPortraits.Clear();

            if (pawn == null)
            {
                lastCachedPawn = null;
                return;
            }

            lastCachedPawn = pawn;
            string dir = CacheManager.GetPortraitSaveDirectory(pawn);
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.png");
                foreach (string file in files)
                {
                    // Skip reference files from main grid loading
                    if (file.EndsWith("_ref_gear.png") || file.EndsWith("_ref_portrait.png"))
                    {
                        continue;
                    }

                    try
                    {
                        byte[] bytes = File.ReadAllBytes(file);
                        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (ImageConversion.LoadImage(tex, bytes))
                        {
                            SavedPortrait sp = new SavedPortrait
                            {
                                pngPath = file,
                                texture = tex
                            };

                            string txtFile = Path.ChangeExtension(file, ".txt");
                            if (File.Exists(txtFile))
                            {
                                sp.txtPath = txtFile;
                                sp.prompt = File.ReadAllText(txtFile);
                            }
                            else
                            {
                                sp.prompt = "No prompt recorded.";
                            }

                            // Load gear reference sheet if it exists
                            string gearFile = Path.ChangeExtension(file, null) + "_ref_gear.png";
                            if (File.Exists(gearFile))
                            {
                                byte[] gBytes = File.ReadAllBytes(gearFile);
                                Texture2D gTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                                if (ImageConversion.LoadImage(gTex, gBytes))
                                {
                                    sp.refGearTexture = gTex;
                                }
                                else
                                {
                                    UnityEngine.Object.Destroy(gTex);
                                }
                            }

                            // Load active portrait reference if it exists
                            string portFile = Path.ChangeExtension(file, null) + "_ref_portrait.png";
                            if (File.Exists(portFile))
                            {
                                byte[] pBytes = File.ReadAllBytes(portFile);
                                Texture2D pTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                                if (ImageConversion.LoadImage(pTex, pBytes))
                                {
                                    sp.refPortraitTexture = pTex;
                                }
                                else
                                {
                                    UnityEngine.Object.Destroy(pTex);
                                }
                            }

                            string filename = Path.GetFileNameWithoutExtension(file);
                            string[] parts = filename.Split('_');
                            int n = parts.Length;
                            if (n >= 5 && (parts[n - 3] == "portrait" || parts[n - 3] == "bodyshot" || parts[n - 3] == "special"))
                            {
                                if (filename.Contains("Realistic_Korean")) sp.styleName = "Realistic_Korean";
                                else if (filename.Contains("Realistic_Western")) sp.styleName = "Realistic_Western";
                                else if (filename.Contains("DotPixel")) sp.styleName = "DotPixel";
                                else sp.styleName = parts[1];

                                sp.framingName = parts[n - 3];
                                sp.timestamp = parts[n - 2].Replace('-', ':') + " " + parts[n - 1].Replace('-', ':');
                            }
                            else if (n >= 4) // Name_Style_Date_Time (Legacy)
                            {
                                if (filename.Contains("Realistic_Korean")) sp.styleName = "Realistic_Korean";
                                else if (filename.Contains("Realistic_Western")) sp.styleName = "Realistic_Western";
                                else if (filename.Contains("DotPixel")) sp.styleName = "DotPixel";
                                else sp.styleName = parts[1];

                                sp.framingName = "portrait";
                                sp.timestamp = parts[n - 2].Replace('-', ':') + " " + parts[n - 1].Replace('-', ':');
                            }
                            else
                            {
                                sp.styleName = "Unknown";
                                sp.framingName = "portrait";
                                sp.timestamp = "Unknown";
                            }

                            cachedSavedPortraits.Add(sp);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[Dynamic AI Portraits] Error loading gallery image " + file + ": " + ex.Message);
                    }
                }
            }
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            // Leave room at the top for the window's "Dynamic AI Portraits" title bar —
            // without this padding the tab strip renders directly under the title and overlaps it.
            const float TitleBarPadding = 32f;

            Rect tabRect = new Rect(inRect.x, inRect.y + TitleBarPadding, inRect.width, 35f);
            List<TabRecord> tabs = new List<TabRecord>();
            tabs.Add(new TabRecord("API Settings",   () => { activeTab = 0; }, activeTab == 0));
            tabs.Add(new TabRecord("Pawn Gallery",   () => { activeTab = 1; }, activeTab == 1));
            tabs.Add(new TabRecord("Prompt Engineering", () => { activeTab = 2; }, activeTab == 2));

            TabDrawer.DrawTabs(tabRect, tabs);

            // Main content sits below the tab strip
            float mainTop    = inRect.y + TitleBarPadding + 40f;
            float mainHeight = inRect.height - TitleBarPadding - 40f;
            Rect mainRect = new Rect(inRect.x, mainTop, inRect.width, mainHeight);

            if      (activeTab == 0) DrawApiSettings(mainRect);
            else if (activeTab == 1) DrawPawnGallery(mainRect);
            else if (activeTab == 2) DrawPromptEngineering(mainRect);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // PROVIDER REGISTRY — single source of truth for label, models, defaults,
        // API key requirements, and info text. Adding a new provider means adding
        // a new switch case in each of these helpers + a coroutine in AsyncAIClient.
        // ──────────────────────────────────────────────────────────────────────────

        public static string LLMModelLabel(LLMModelType t)
        {
            switch (t)
            {
                case LLMModelType.GeminiFlashLite: return "Gemini Flash Lite";
                case LLMModelType.Gemma26B:        return "Gemma 4 26B";
                case LLMModelType.Gemma31B:        return "Gemma 4 31B";
                default:                           return t.ToString();
            }
        }

        private static string ProviderLabel(BackendType bt)
        {
            switch (bt)
            {
                case BackendType.Pollinations: return "🆓 Pollinations (free, no key)";
                case BackendType.Cloudflare:   return "☁ Cloudflare Workers AI (free 10k/day, then ~$0.0005)";
                case BackendType.GoogleImagen: return "💎 Google Imagen 4 ($0.02 each)";
                case BackendType.DeepInfra:    return "⚡ DeepInfra (~$0.0005 each)";
                case BackendType.HuggingFace:  return "🤗 HuggingFace Inference (limited free tier)";
                case BackendType.LocalA1111:   return "🖥 Local GPU (your machine, free)";
                default:                       return bt.ToString();
            }
        }

        // Default model/URL/key wipe when switching providers
        private void ApplyProviderDefaults(BackendType bt)
        {
            backendType = bt;
            // distinct fields natively preserve configurations, no longer need to wipe/overwrite
        }

        private static string[] ModelsForProvider(BackendType bt)
        {
            switch (bt)
            {
                case BackendType.Pollinations:
                    return new[] { "sana" };
                case BackendType.Cloudflare:
                    return new[] {
                        "@cf/black-forest-labs/flux-1-schnell",
                        "@cf/stabilityai/stable-diffusion-xl-base-1.0",
                        "@cf/lykon/dreamshaper-8-lcm",
                        "@cf/bytedance/stable-diffusion-xl-lightning"
                    };
                case BackendType.GoogleImagen:
                    return new[] {
                        "nanobanana-2",
                        "nanobanana-pro",
                        "nanobanana",
                        "imagen-4.0-fast-generate-001",
                        "imagen-4.0-generate-001",
                        "imagen-4.0-ultra-generate-001",
                        "imagen-3.0-fast-generate-001"
                    };
                case BackendType.DeepInfra:
                    return new[] {
                        "black-forest-labs/FLUX-1-schnell",
                        "black-forest-labs/FLUX-1-dev",
                        "stabilityai/sdxl-turbo"
                    };
                case BackendType.HuggingFace:
                    return new[] {
                        "black-forest-labs/FLUX.1-schnell",
                        "black-forest-labs/FLUX.1-dev",
                        "stabilityai/stable-diffusion-xl-base-1.0"
                    };
                case BackendType.LocalA1111:
                    return null; // uses whatever the server has loaded
                default:
                    return null;
            }
        }

        private static bool ProviderNeedsApiKey(BackendType bt)
        {
            return bt == BackendType.Cloudflare
                || bt == BackendType.GoogleImagen
                || bt == BackendType.DeepInfra
                || bt == BackendType.HuggingFace;
        }

        private static string ApiKeyLabel(BackendType bt)
        {
            switch (bt)
            {
                case BackendType.Cloudflare:   return "API Key  (format: account_id:token)";
                case BackendType.GoogleImagen: return "Google AI Studio API Key";
                case BackendType.DeepInfra:    return "DeepInfra API Token";
                case BackendType.HuggingFace:  return "HuggingFace API Token";
                default:                       return "API Key";
            }
        }

        private static string ApiKeyHint(BackendType bt)
        {
            switch (bt)
            {
                case BackendType.Cloudflare:
                    return "Get account ID + token at dash.cloudflare.com → AI → Workers AI → API tokens";
                case BackendType.GoogleImagen:
                    return "Free key at aistudio.google.com/app/apikey";
                case BackendType.DeepInfra:
                    return "Free signup at deepinfra.com (GitHub OAuth, ~2 min)";
                case BackendType.HuggingFace:
                    return "Token at huggingface.co/settings/tokens (free, read-access enough)";
                default:
                    return "";
            }
        }

        private static string ProviderInfo(BackendType bt)
        {
            switch (bt)
            {
                case BackendType.Pollinations:
                    return "Truly free, no signup. Model: Sana via Pollinations. ~50s per generation. " +
                           "Outputs JPEG (opaque background — background remover post-processes).";
                case BackendType.Cloudflare:
                    return "Cheapest option overall: 10,000 free requests/day, then ~$0.0005 each. " +
                           "FLUX.1 Schnell quality (better than Sana). ~2-4s. Needs free Cloudflare " +
                           "account + API token. Auth field uses format account_id:token.";
                case BackendType.GoogleImagen:
                    return "$0.02 / image (Fast tier). ~3s generation. True transparent PNG output. " +
                           "Best overall quality + style adherence. Free Google AI Studio key.";
                case BackendType.DeepInfra:
                    return "~$0.0005 per image, no free credits but ultra-cheap. Single Bearer token. " +
                           "FLUX.1 Schnell quality. ~2-5s. Quick GitHub OAuth signup.";
                case BackendType.HuggingFace:
                    return "Free tier exists (rate-limited, models go cold). 30s-2min on first call. " +
                           "Best for very occasional generation. Requires a HF token.";
                case BackendType.LocalA1111:
                    return "Free forever, runs on your own GPU. Requires AUTOMATIC1111 / Forge / SD.Next " +
                           "or ComfyUI A1111-compat server on port 7860 with --api flag enabled. " +
                           "Start the server BEFORE clicking refresh.";
                default:
                    return "";
            }
        }

        private static Color ProviderInfoColor(BackendType bt)
        {
            switch (bt)
            {
                case BackendType.Pollinations: return new Color(0.10f, 0.20f, 0.10f, 0.6f);
                case BackendType.Cloudflare:   return new Color(0.20f, 0.15f, 0.05f, 0.6f);
                case BackendType.GoogleImagen: return new Color(0.20f, 0.15f, 0.05f, 0.6f);
                case BackendType.DeepInfra:    return new Color(0.10f, 0.20f, 0.20f, 0.6f);
                case BackendType.HuggingFace:  return new Color(0.18f, 0.12f, 0.05f, 0.6f);
                case BackendType.LocalA1111:   return new Color(0.10f, 0.15f, 0.25f, 0.6f);
                default:                       return new Color(0.12f, 0.12f, 0.14f, 0.6f);
            }
        }

        private void DrawApiSettings(Rect inRect)
        {
            float viewHeight = 1000f;
            if (useAIBgRemoval) viewHeight += 100f;
            if (showAdvanced) viewHeight += 540f;

            Rect viewRect = new Rect(0f, 0f, inRect.width - 18f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ── PORTRAIT STYLE ────────────────────────────────────────────────────
            listing.Label("Portrait Art Style");
            listing.Gap(4f);

            Rect styleRow = listing.GetRect(28f);
            float styleW = styleRow.width / 3f;

            Rect btnKorean  = new Rect(styleRow.x,             styleRow.y, styleW - 4f, 28f);
            Rect btnWestern = new Rect(styleRow.x + styleW,    styleRow.y, styleW - 4f, 28f);
            Rect btnPixel   = new Rect(styleRow.x + styleW*2f, styleRow.y, styleW - 4f, 28f);

            if (portraitStyle == PortraitStyle.Realistic_Korean)  GUI.color = new Color(0.5f, 0.9f, 1f);
            if (Widgets.ButtonText(btnKorean,  "🎨 Webtoon / Manhwa"))  portraitStyle = PortraitStyle.Realistic_Korean;
            GUI.color = Color.white;

            if (portraitStyle == PortraitStyle.Realistic_Western) GUI.color = new Color(0.5f, 0.9f, 1f);
            if (Widgets.ButtonText(btnWestern, "📺 Rick & Morty Cartoon")) portraitStyle = PortraitStyle.Realistic_Western;
            GUI.color = Color.white;

            if (portraitStyle == PortraitStyle.DotPixel)          GUI.color = new Color(0.5f, 0.9f, 1f);
            if (Widgets.ButtonText(btnPixel,   "🟦 Pixel / Dot"))          portraitStyle = PortraitStyle.DotPixel;
            GUI.color = Color.white;

            listing.Gap(6f);
            string styleDesc =
                portraitStyle == PortraitStyle.Realistic_Korean  ? "Korean webtoon manhwa style — sharp inked line art, dramatic chiaroscuro, saturated focal colors, custom-tailored aesthetics." :
                portraitStyle == PortraitStyle.Realistic_Western ? "Adult Swim cartoon (Rick and Morty) — thick black outlines, flat 2D fills, bulging eyes, wonky proportions." :
                portraitStyle == PortraitStyle.DotPixel          ? "16-bit pixel art — classic JRPG sprite style, strict pixel grid, cel-shading bands." : "";
            Text.Font = GameFont.Tiny;
            Widgets.Label(listing.GetRect(28f), styleDesc);
            Text.Font = GameFont.Small;

            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(8f);

            // ── PROMPT GENERATION ─────────────────────────────────────────────────
            // One line: a narrow model dropdown with the API key field beside it. With a
            // key the LLM rewrites the prompt; blank uses the in-house compiled template
            // automatically (see QueueGeneration).
            listing.Label("Prompt Generation");
            listing.Gap(6f);

            Rect pgRow = listing.GetRect(30f);
            float pgModelW = 170f;
            Rect pgModelBtn = new Rect(pgRow.x, pgRow.y, pgModelW, 30f);
            if (Widgets.ButtonText(pgModelBtn, LLMModelLabel(llmModelType)))
            {
                var pgOpts = new List<FloatMenuOption>();
                LLMModelType[] pgOrder = new[] { LLMModelType.GeminiFlashLite, LLMModelType.Gemma26B, LLMModelType.Gemma31B };
                foreach (LLMModelType t in pgOrder)
                {
                    LLMModelType captured = t;
                    pgOpts.Add(new FloatMenuOption(LLMModelLabel(t), delegate () { llmModelType = captured; }));
                }
                Find.WindowStack.Add(new FloatMenu(pgOpts));
            }
            Rect pgKeyRect = new Rect(pgRow.x + pgModelW + 8f, pgRow.y + 3f, pgRow.width - pgModelW - 8f, 24f);
            llmApiKey = UnityEngine.GUI.PasswordField(pgKeyRect, llmApiKey, '*');
            listing.Gap(2f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.55f, 0.55f, 0.55f);
            Widgets.Label(listing.GetRect(20f), "  Free key at aistudio.google.com/app/apikey");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(8f);

            // ── IMAGE GENERATION ──────────────────────────────────────────────────
            // Pick a source. Free = Pollinations (no key). Paid sources auto-fall back to
            // Pollinations on failure (AsyncAIClient.DispatchImageBackend).
            listing.Label("Image Generation");
            listing.Gap(6f);

            Rect imgRow = listing.GetRect(32f);
            float imgBtnW = imgRow.width / 4f;
            bool freeActive = backendType == BackendType.Pollinations;
            bool cfActive = backendType == BackendType.Cloudflare;
            bool imagenActive = backendType == BackendType.GoogleImagen && giModelName != null && giModelName.StartsWith("imagen");
            bool nb2Active = backendType == BackendType.GoogleImagen && giModelName == "nanobanana-2";

            Rect imgFreeBtn = new Rect(imgRow.x, imgRow.y, imgBtnW, imgRow.height);
            if (Widgets.ButtonText(imgFreeBtn, "Free")) ApplyProviderDefaults(BackendType.Pollinations);
            if (freeActive) { Widgets.DrawBoxSolid(imgFreeBtn, new Color(0.2f, 0.35f, 0.45f, 0.35f)); Widgets.DrawBox(imgFreeBtn, 2); }

            Rect imgCfBtn = new Rect(imgRow.x + imgBtnW, imgRow.y, imgBtnW, imgRow.height);
            if (Widgets.ButtonText(imgCfBtn, "Cloudflare")) ApplyProviderDefaults(BackendType.Cloudflare);
            if (cfActive) { Widgets.DrawBoxSolid(imgCfBtn, new Color(0.2f, 0.35f, 0.45f, 0.35f)); Widgets.DrawBox(imgCfBtn, 2); }

            Rect imgImagenBtn = new Rect(imgRow.x + imgBtnW * 2f, imgRow.y, imgBtnW, imgRow.height);
            if (Widgets.ButtonText(imgImagenBtn, "Imagen4 Fast")) { ApplyProviderDefaults(BackendType.GoogleImagen); giModelName = "imagen-4.0-fast-generate-001"; }
            if (imagenActive) { Widgets.DrawBoxSolid(imgImagenBtn, new Color(0.2f, 0.35f, 0.45f, 0.35f)); Widgets.DrawBox(imgImagenBtn, 2); }

            Rect imgNb2Btn = new Rect(imgRow.x + imgBtnW * 3f, imgRow.y, imgBtnW, imgRow.height);
            if (Widgets.ButtonText(imgNb2Btn, "Nano Banana 2")) { ApplyProviderDefaults(BackendType.GoogleImagen); giModelName = "nanobanana-2"; }
            if (nb2Active) { Widgets.DrawBoxSolid(imgNb2Btn, new Color(0.2f, 0.35f, 0.45f, 0.35f)); Widgets.DrawBox(imgNb2Btn, 2); }

            listing.Gap(10f);

            listing.Label("Cloudflare API Key:");
            Rect cfKeyRect = listing.GetRect(24f);
            cfApiKey = UnityEngine.GUI.PasswordField(cfKeyRect, cfApiKey, '*');
            listing.Gap(6f);

            listing.Label("Google (paid) API Key:");
            Rect giKeyRect = listing.GetRect(24f);
            giApiKey = UnityEngine.GUI.PasswordField(giKeyRect, giApiKey, '*');

            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(8f);

            // ── VIDEO ─────────────────────────────────────────────────────────────
            // Google Veo video key. Blank simply disables video generation.
            listing.Label("Video");
            listing.Gap(6f);

            listing.Label("Video API Key:");
            Rect vidKeyRect = listing.GetRect(24f);
            videoApiKey = UnityEngine.GUI.PasswordField(vidKeyRect, videoApiKey, '*');
            listing.Gap(listing.verticalSpacing);
            listing.Gap(2f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.55f, 0.55f, 0.55f);
            Widgets.Label(listing.GetRect(20f), "  Google AI Studio key for Veo video (aistudio.google.com/app/apikey).");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(6f);

            // ── PORTRAIT DETAILS SETTINGS ─────────────────────────────────────────
            listing.Label("Portrait Details Settings");
            listing.Gap(4f);
            listing.CheckboxLabeled("Include Ideology details", ref includeIdeology, "Include pawn's ideology role (e.g. Moral Guide) and follower description/iconography.");
            listing.CheckboxLabeled("Include Ideology Rim Lighting", ref includeRimLighting, "Separate the character silhouette from the background using a rim light styled with their favorite/ideoligion color.");
            listing.CheckboxLabeled("Use Gear Reference Sheet (Gemini)", ref useGearReferenceSheet, "Stitch matched weapon/apparel sprites into a single reference image for Gemini models. Helps retain equipment designs across generations.");
            listing.CheckboxLabeled("Exclude Helmets/Headgear", ref excludeHelmet, "Excludes helmets, hats, hoods, caps, and masks from the generated portraits and reference sheets.");
            listing.Gap(8f);

            // ── PORTRAIT POSITION & SCALE ──────────────────────────────────────────
            listing.Label("Portrait Display Size & Location (Inspect Pane)");
            listing.Gap(4f);
            
            // Portrait Scale
            listing.Label("Display Size (Scale): " + portraitScale.ToString("F0") + " px  (Default: 260)");
            portraitScale = listing.Slider(portraitScale, 100f, 500f);
            
            // Horizontal Offset
            listing.Label("Horizontal Offset: " + portraitOffsetX.ToString("F0") + " px  (Default: 0)");
            portraitOffsetX = listing.Slider(portraitOffsetX, -300f, 300f);
            
            // Vertical Offset
            listing.Label("Vertical Offset: " + portraitOffsetY.ToString("F0") + " px  (Default: 0)");
            portraitOffsetY = listing.Slider(portraitOffsetY, -300f, 300f);
            listing.Gap(8f);

            listing.GapLine();
            listing.Gap(6f);

            // ── AI BACKGROUND REMOVAL ─────────────────────────────────────────────
            listing.Label("AI Background Removal (Cloudflare)");
            listing.Gap(4f);
            listing.CheckboxLabeled("Use AI Background Removal", ref useAIBgRemoval, "Use Cloudflare's bria-rmbg-1.4 AI model for flawless background and halo removal. Fixes hair/clothing colors being erased by the local flood-fill. Requires a free Cloudflare API key.");
            if (useAIBgRemoval)
            {
                bool canReuseCfKey = (!string.IsNullOrEmpty(cfApiKey) && cfApiKey.Contains(":"));
                if (canReuseCfKey)
                {
                    Rect reuseBox = listing.GetRect(30f);
                    Widgets.DrawBoxSolid(reuseBox, new Color(0.05f, 0.18f, 0.05f, 0.8f));
                    GUI.color = new Color(1f, 1f, 1f, 0.15f);
                    Widgets.DrawBox(reuseBox, 1);
                    GUI.color = Color.white;
                    Text.Font   = GameFont.Tiny;
                    GUI.color   = new Color(0.5f, 0.95f, 0.5f);
                    Widgets.Label(reuseBox.ContractedBy(5f), "\u2713  Using your Cloudflare image generation key \u2014 no extra key needed.");
                    GUI.color   = Color.white;
                    Text.Font   = GameFont.Small;
                }
                else
                {
                    listing.Label("Cloudflare API Key (account_id:token):");
                    Rect cfBgRect = listing.GetRect(24f);
                    cfBgRemovalKey = UnityEngine.GUI.PasswordField(cfBgRect, cfBgRemovalKey, '*');
                    listing.Gap(listing.verticalSpacing);
                    listing.Gap(2f);
                    Rect hintRect = listing.GetRect(20f);
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.55f, 0.55f, 0.55f);
                    Widgets.Label(hintRect, "  Format: <account_id>:<api_token>  \u2022  Free 10,000 uses/day via Workers AI.");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }
                listing.Gap(4f);
            }

            listing.Gap(8f);
            listing.GapLine();
            listing.Gap(6f);

            // ── ADVANCED ──────────────────────────────────────────────────────────
            Rect advHeaderRect = listing.GetRect(26f);
            string advLabel = (showAdvanced ? "▼" : "▶") + "  Advanced Settings";
            if (Widgets.ButtonText(advHeaderRect, advLabel))
                showAdvanced = !showAdvanced;

            if (showAdvanced)
            {
                listing.Gap(6f);
                if (listing.ButtonText("Reset to Default Prompts & Settings"))
                {
                    baseStylePrompt = "";
                    manhwaStylePrompt = "flat 2D webtoon manhwa drawing, hand-drawn digital illustration, bold clean inked outlines, flat cel-shaded color fills, hard-edged anime shadows, bright saturated CMYK print-style colors, expressive stylized anime eyes, glossy stylized hair, crisp clean line art, masterpiece webtoon key visual, strictly 2D flat art, smooth flat skin rendering, no lens blur, no depth of field";
                    cartoonStylePrompt = "Rick and Morty Adult Swim cartoon character, Justin Roiland animation style, thick consistent black outlines, flat 2D color fills, no gradients, no painterly shading, bulging round cartoon eyes with tiny black dot pupils, exaggerated wonky proportions, oversized head, hand-drawn animation cel look, bright primary color palette";
                    pixelStylePrompt = "high-quality 16-bit retro JRPG character sprite, Tactics Ogre and Final Fantasy Tactics style, clean pixel-art grid, zero anti-aliasing, sharp deliberate pixel edges, thin consistent dark outlines, clean flat cel-shading, limited color palette, anime-style cute facial features, detailed hair";
                    baseNegativePrompt = "generic fantasy art, cartoon style, bright cheerful lighting, flat lighting, blurry textures, generic features, messy brushstrokes, standard clean weapon, generic clothing, missing face tattoos, missing horns, missing cybernetic eyes, missing scars, photorealistic photograph, 3d render, chibi, flat shading, low quality, watermark, extra limbs, deformed face, bad anatomy, multiple people, text, signature, anti-aliased pixels, jagged irregular lines, muddied unclear character design, illegible text, generic UI, inconsistency between sprite and portrait";
                    steps = 20;
                    cfgScale = 7f;
                    portraitScale = 260f;
                    portraitOffsetX = 0f;
                    portraitOffsetY = 0f;
                    excludeHelmet = false;
                    llmModelType = LLMModelType.GeminiFlashLite;
                    useLLMPrompt = false;
                    useAIBgRemoval = false;
                    includeIdeology = true;
                    includeRimLighting = true;
                    useGearReferenceSheet = true;
                    backendType = BackendType.Pollinations;
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                    Messages.Message("Settings and prompts reset to default values.", MessageTypeDefOf.PositiveEvent, false);
                }
                listing.Gap(6f);

                // Full backend picker — includes the extra backends kept off the landing tab.
                listing.Label("Backend Provider (incl. DeepInfra / HuggingFace / Local GPU)");
                Rect advProvRow = listing.GetRect(32f);
                if (Widgets.ButtonText(advProvRow, ProviderLabel(backendType)))
                {
                    var provOpts = new List<FloatMenuOption>();
                    BackendType[] provOrder = new[] {
                        BackendType.Pollinations,
                        BackendType.Cloudflare,
                        BackendType.GoogleImagen,
                        BackendType.DeepInfra,
                        BackendType.HuggingFace,
                        BackendType.LocalA1111
                    };
                    foreach (BackendType bt in provOrder)
                    {
                        BackendType captured = bt;
                        provOpts.Add(new FloatMenuOption(ProviderLabel(bt), delegate () { ApplyProviderDefaults(captured); }));
                    }
                    Find.WindowStack.Add(new FloatMenu(provOpts));
                }
                listing.Gap(6f);

                if (ProviderNeedsApiKey(backendType))
                {
                    listing.Label(ApiKeyLabel(backendType));
                    Rect advKeyRect = listing.GetRect(24f);
                    CurrentApiKey = UnityEngine.GUI.PasswordField(advKeyRect, CurrentApiKey, '*');
                    listing.Gap(6f);
                }

                listing.Label("API URL");
                CurrentApiUrl = listing.TextEntry(CurrentApiUrl);

                listing.Gap(6f);
                listing.Label("Model Name");
                CurrentModelName = listing.TextEntry(CurrentModelName);

                listing.Gap(6f);
                listing.Label("Extra Style Prompt (appended to every generation)");
                baseStylePrompt = listing.TextEntry(baseStylePrompt, 2);

                listing.Gap(6f);
                listing.Label("Manhwa Style Prompt");
                manhwaStylePrompt = listing.TextEntry(manhwaStylePrompt, 2);

                listing.Gap(6f);
                listing.Label("Cartoon Style Prompt");
                cartoonStylePrompt = listing.TextEntry(cartoonStylePrompt, 2);

                listing.Gap(6f);
                listing.Label("Pixel Style Prompt");
                pixelStylePrompt = listing.TextEntry(pixelStylePrompt, 2);

                listing.Gap(6f);
                listing.Label("Negative Prompt");
                baseNegativePrompt = listing.TextEntry(baseNegativePrompt, 2);

                listing.Gap(6f);
                listing.Label("Steps: " + steps + "  (HuggingFace, DeepInfra, Local)");
                steps = (int)listing.Slider(steps, 5, 50);

                listing.Label("CFG Scale: " + cfgScale.ToString("F1") + "  (HuggingFace, Local)");
                cfgScale = listing.Slider(cfgScale, 1f, 15f);
            }

            listing.End();
            Widgets.EndScrollView();
        }

        private bool IsInjuredOrSick(Pawn p)
        {
            if (p.health == null) return false;
            if (p.health.hediffSet.PainTotal > 0.05f) return true;
            if (p.health.hediffSet.BleedRateTotal > 0.01f) return true;
            for (int i = 0; i < p.health.hediffSet.hediffs.Count; i++)
            {
                Hediff h = p.health.hediffSet.hediffs[i];
                if (h.def != null && h.def.isBad && h.def.makesSickThought)
                {
                    return true;
                }
            }
            return false;
        }

        private void DrawPawnGallery(Rect inRect)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Widgets.Label(inRect, "Please load a save game to view the Pawn Gallery.");
                return;
            }

            List<Pawn> colonists = null;
            if (Find.ColonistBar != null)
            {
                colonists = Find.ColonistBar.GetColonistsInOrder();
            }
            if (colonists == null || colonists.Count == 0)
            {
                Widgets.Label(inRect, "No colonists found on the colonist bar.");
                return;
            }

            if (selectedPawn == null || !colonists.Contains(selectedPawn))
            {
                selectedPawn = colonists[0];
                selectedSavedPortrait = null;
                customPromptBuffer = "";
            }

            // Left Sidebar for Colonists
            float sidebarWidth = 180f;
            Rect sidebarRect = new Rect(inRect.x, inRect.y, sidebarWidth, inRect.height);
            Widgets.DrawMenuSection(sidebarRect);

            Rect sidebarScrollRect = sidebarRect.ContractedBy(4f);
            Rect viewRectLeft = new Rect(0f, 0f, sidebarScrollRect.width - 16f, colonists.Count * 32f);

            Widgets.BeginScrollView(sidebarScrollRect, ref leftScrollPosition, viewRectLeft);
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                Rect rowRect = new Rect(0f, i * 32f, viewRectLeft.width, 28f);

                if (p == selectedPawn)
                {
                    GUI.color = new Color(0.5f, 0.9f, 1f);
                }

                // Add status badges: ⚔ (Drafted), 💤 (Sleeping), 💔 (Injured / Sick)
                string label = p.LabelShortCap;
                string badges = "";
                if (p.Drafted)
                {
                    badges += " ⚔";
                }
                if (!p.Awake())
                {
                    badges += " 💤";
                }
                if (IsInjuredOrSick(p))
                {
                    badges += " 💔";
                }
                if (!string.IsNullOrEmpty(badges))
                {
                    label += badges;
                }

                if (Widgets.ButtonText(rowRect, label))
                {
                    selectedPawn = p;
                    selectedSavedPortrait = null;
                    customPromptBuffer = "";
                }
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();

            // Split the remaining space: 60% for saved image grid, 40% for Pawn Vibe & Prompt panel
            float remainingWidth = inRect.width - sidebarWidth - 10f;
            float gridWidth = remainingWidth * 0.60f;
            float vibeWidth = remainingWidth - gridWidth - 10f;

            Rect gridAreaRect = new Rect(inRect.x + sidebarWidth + 10f, inRect.y, gridWidth, inRect.height);
            Rect vibeAreaRect = new Rect(gridAreaRect.xMax + 10f, inRect.y, vibeWidth, inRect.height);

            // ── MIDDLE COLUMN: SAVED IMAGE GRID ──
            // Fetch directory contents and cache if necessary
            if (selectedPawn != lastCachedPawn)
            {
                RefreshPawnPortraitsCache(selectedPawn);
            }
            else
            {
                // Check if file count changed on disk to auto-refresh (excluding reference images)
                string dir = CacheManager.GetPortraitSaveDirectory(selectedPawn);
                int diskCount = 0;
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir, "*.png"))
                    {
                        if (!file.EndsWith("_ref_gear.png") && !file.EndsWith("_ref_portrait.png"))
                        {
                            diskCount++;
                        }
                    }
                }
                if (diskCount != cachedSavedPortraits.Count)
                {
                    RefreshPawnPortraitsCache(selectedPawn);
                }
            }

            // Top Buttons in Middle Panel: Open Folder and Refresh
            Rect topButtonsRect = new Rect(gridAreaRect.x, gridAreaRect.y, gridAreaRect.width, 30f);
            float btnW = (gridAreaRect.width - 10f) / 2f;
            Rect openDirBtn = new Rect(gridAreaRect.x, gridAreaRect.y, btnW, 30f);
            Rect refreshBtn = new Rect(openDirBtn.xMax + 10f, gridAreaRect.y, btnW, 30f);

            if (Widgets.ButtonText(openDirBtn, "Open Portraits Folder"))
            {
                try
                {
                    string docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                    string path = Path.Combine(docs, "RimWorld Portraits");
                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                    CacheManager.OpenInFileExplorer(path);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Dynamic AI Portraits] Failed to open folder: " + ex.Message);
                }
            }

            if (Widgets.ButtonText(refreshBtn, "Rescan / Refresh"))
            {
                RefreshPawnPortraitsCache(selectedPawn);
            }

            // Framing buttons row
            Rect framingRow = new Rect(gridAreaRect.x, gridAreaRect.y + 36f, gridAreaRect.width, 30f);
            float fBtnW = (framingRow.width - 10f) / 3f;
            Rect btnPort = new Rect(framingRow.x, framingRow.y, fBtnW, 30f);
            Rect btnBody = new Rect(btnPort.xMax + 5f, framingRow.y, fBtnW, 30f);
            Rect btnSpec = new Rect(btnBody.xMax + 5f, framingRow.y, fBtnW, 30f);

            string currentFraming = AIPortraitsManager.GetActiveFraming(selectedPawn);

            if (currentFraming == "portrait") GUI.color = new Color(0.5f, 0.9f, 1f);
            if (Widgets.ButtonText(btnPort, "Portrait (P)"))
            {
                if (pawnFraming == null) pawnFraming = new Dictionary<string, string>();
                pawnFraming[selectedPawn.ThingID] = "portrait";
                selectedSavedPortrait = null;
                customPromptBuffer = "";
                AIPortraitsMod.Instance.WriteSettings();
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }
            GUI.color = Color.white;

            if (currentFraming == "bodyshot") GUI.color = new Color(0.5f, 0.9f, 1f);
            if (Widgets.ButtonText(btnBody, "Full Shot (B)"))
            {
                if (pawnFraming == null) pawnFraming = new Dictionary<string, string>();
                pawnFraming[selectedPawn.ThingID] = "bodyshot";
                selectedSavedPortrait = null;
                customPromptBuffer = "";
                AIPortraitsMod.Instance.WriteSettings();
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }
            GUI.color = Color.white;

            if (currentFraming == "special") GUI.color = new Color(0.5f, 0.9f, 1f);
            if (Widgets.ButtonText(btnSpec, "Special (S)"))
            {
                if (pawnFraming == null) pawnFraming = new Dictionary<string, string>();
                pawnFraming[selectedPawn.ThingID] = "special";
                selectedSavedPortrait = null;
                customPromptBuffer = "";
                AIPortraitsMod.Instance.WriteSettings();
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }
            GUI.color = Color.white;

            // Sub Header Action Buttons (Create New, Use Dynamic)
            Rect subHeaderRect = new Rect(gridAreaRect.x, gridAreaRect.y + 72f, gridAreaRect.width, 35f);
            float createBtnWidth = subHeaderRect.width * 0.65f;
            float dynamicBtnWidth = subHeaderRect.width - createBtnWidth - 10f;

            Rect createBtnRect = new Rect(subHeaderRect.x, subHeaderRect.y, createBtnWidth, 35f);
            Rect dynamicBtnRect = new Rect(createBtnRect.xMax + 10f, subHeaderRect.y, dynamicBtnWidth, 35f);

            GenerationStatus status = AIPortraitsManager.GetStatus(selectedPawn);
            string btnLabel = "Create New Portrait for " + selectedPawn.LabelShortCap;
            if (status == GenerationStatus.Generating)
            {
                btnLabel = "Painting (Generating)...";
                GUI.enabled = false;
                // Loading-state tooltip so the user understands the button is intentionally
                // disabled (originally proposed by Jules-bot palette-ux-improvements).
                TooltipHandler.TipRegion(createBtnRect,
                    "A portrait is currently being generated for this pawn. Please wait for it to finish.");
            }
            if (Widgets.ButtonText(createBtnRect, btnLabel))
            {
                AIPortraitsManager.TriggerNewPortraitWithContinuity(selectedPawn);
            }
            GUI.enabled = true;

            // Highlight dynamic button if NO active portrait is locked
            string activeKey = AIPortraitsManager.GetActiveKey(selectedPawn);
            string activePathForCheck = AIPortraitsManager.GetActivePortraitPath(selectedPawn, currentFraming);
            bool hasLocked = !string.IsNullOrEmpty(activePathForCheck) && File.Exists(activePathForCheck);
            if (!hasLocked)
            {
                GUI.color = new Color(0.5f, 0.9f, 1f);
            }
            if (Widgets.ButtonText(dynamicBtnRect, "Use Dynamic Portrait"))
            {
                if (activePortraits.ContainsKey(activeKey))
                {
                    activePortraits.Remove(activeKey);
                }
                if (currentFraming == "portrait")
                {
                    string worldId = "global";
                    if (Find.World != null && Find.World.info != null)
                        worldId = Find.World.info.persistentRandomValue.ToString();
                    string legacyKey = worldId + "_" + selectedPawn.ThingID;
                    if (activePortraits.ContainsKey(legacyKey))
                    {
                        activePortraits.Remove(legacyKey);
                    }
                }
                AIPortraitsManager.ClearPawnActiveTextureCache(selectedPawn);
                AIPortraitsMod.Instance.WriteSettings();
            }
            GUI.color = Color.white;

            // Grid of Images Scrollview
            Rect gridScrollRect = new Rect(gridAreaRect.x, gridAreaRect.y + 115f, gridAreaRect.width, gridAreaRect.height - 115f);

            // Group portraits by framing Name
            var groupedPortraits = new Dictionary<string, List<SavedPortrait>>();
            groupedPortraits["portrait"] = new List<SavedPortrait>();
            groupedPortraits["bodyshot"] = new List<SavedPortrait>();
            groupedPortraits["special"] = new List<SavedPortrait>();

            foreach (var sp in cachedSavedPortraits)
            {
                if (!groupedPortraits.ContainsKey(sp.framingName))
                    groupedPortraits[sp.framingName] = new List<SavedPortrait>();
                groupedPortraits[sp.framingName].Add(sp);
            }

            int activeGroupCount = groupedPortraits.ContainsKey(currentFraming) ? groupedPortraits[currentFraming].Count : 0;
            if (activeGroupCount == 0)
            {
                string framingFriendly = currentFraming == "portrait" ? "portrait" : (currentFraming == "bodyshot" ? "full body shot" : "special");
                Widgets.Label(gridScrollRect, "No saved " + framingFriendly + " images found. Click 'Create New' to generate one for " + selectedPawn.LabelShortCap + ".");
            }
            else
            {
                float cardW = 120f;
                float cardH = 180f;
                float margin = 8f;
                int cols = Mathf.Max(1, Mathf.FloorToInt((gridScrollRect.width - 24f) / (cardW + margin)));

                // Calculate total height needed for the selected framing
                float totalHeight = 0f;
                foreach (var kvp in groupedPortraits)
                {
                    if (kvp.Key != currentFraming) continue;
                    if (kvp.Value.Count > 0)
                    {
                        totalHeight += 30f; // Header height
                        int rows = Mathf.CeilToInt((float)kvp.Value.Count / cols);
                        totalHeight += rows * (cardH + margin);
                    }
                }

                Rect viewRectRight = new Rect(0f, 0f, gridScrollRect.width - 24f, totalHeight + 10f);

                Widgets.BeginScrollView(gridScrollRect, ref rightScrollPosition, viewRectRight);
                float curY = 0f;

                foreach (var kvp in groupedPortraits)
                {
                    if (kvp.Key != currentFraming) continue;
                    if (kvp.Value.Count == 0) continue;

                    string headerLabel = "";
                    if (kvp.Key == "portrait") headerLabel = "Portraits (1:1)";
                    else if (kvp.Key == "bodyshot") headerLabel = "Full Body Shots (3:4)";
                    else if (kvp.Key == "special") headerLabel = "Special Shots (4:3)";
                    else headerLabel = kvp.Key;

                    Widgets.Label(new Rect(0f, curY, viewRectRight.width, 25f), "<b>" + headerLabel + "</b>");
                    curY += 30f;

                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        SavedPortrait sp = kvp.Value[i];
                        int r = i / cols;
                        int c = i % cols;

                        float x = c * (cardW + margin);
                        float y = curY + r * (cardH + margin);

                        Rect cardRect = new Rect(x, y, cardW, cardH);

                        // Check if this image is the active/locked one for its specific framing
                        string activePath = AIPortraitsManager.GetActivePortraitPath(selectedPawn, sp.framingName);
                        bool isActive = !string.IsNullOrEmpty(activePath) && activePath == sp.pngPath;
                        bool isSelected = (selectedSavedPortrait == sp);

                        // Draw background and highlight border if selected/active
                        if (isSelected)
                        {
                            Widgets.DrawBoxSolid(cardRect, new Color(0.08f, 0.18f, 0.22f, 1f));
                            GUI.color = new Color(0.2f, 0.7f, 1f);
                            Widgets.DrawBox(cardRect, 2);
                            GUI.color = Color.white;
                        }
                        else if (isActive)
                        {
                            Widgets.DrawBoxSolid(cardRect, new Color(0.2f, 0.18f, 0.08f, 1f));
                            GUI.color = new Color(0.9f, 0.7f, 0f);
                            Widgets.DrawBox(cardRect, 2);
                            GUI.color = Color.white;
                        }
                        else
                        {
                            Widgets.DrawBoxSolid(cardRect, new Color(0.1f, 0.10f, 0.10f, 1f));
                            GUI.color = new Color(1f, 1f, 1f, 0.15f);
                            Widgets.DrawBox(cardRect, 1);
                            GUI.color = Color.white;
                        }

                        // Draw the image
                        Rect imgRect = new Rect(cardRect.x + 4f, cardRect.y + 4f, cardW - 8f, cardW - 8f);
                        if (sp.texture != null)
                        {
                            GUI.DrawTexture(imgRect, sp.texture, ScaleMode.ScaleToFit);
                        }
                        else
                        {
                            Widgets.Label(imgRect, "Image Error");
                        }

                        // Tooltip and Click Action for Selection
                        TooltipHandler.TipRegion(imgRect, "Click to select this image and view/edit its prompt.\n\nPrompt:\n" + sp.prompt);

                        if (Mouse.IsOver(imgRect))
                        {
                            Widgets.DrawHighlight(imgRect);
                        }

                        if (Widgets.ButtonInvisible(imgRect))
                        {
                            selectedSavedPortrait = sp;
                            customPromptBuffer = sp.prompt;
                            SoundDefOf.Click.PlayOneShotOnCamera(null);
                        }

                        // Button 1: Set Active
                        Rect btnActiveRect = new Rect(cardRect.x + 4f, imgRect.yMax + 4f, cardW - 8f, 20f);
                        if (isActive)
                        {
                            GUI.color = new Color(0.9f, 0.7f, 0f);
                            Text.Anchor = TextAnchor.MiddleCenter;
                            Text.Font = GameFont.Tiny;
                            Widgets.Label(btnActiveRect, "★ Active");
                            Text.Font = GameFont.Small;
                            Text.Anchor = TextAnchor.UpperLeft;
                            GUI.color = Color.white;
                        }
                        else
                        {
                            if (Widgets.ButtonText(btnActiveRect, "Set Active"))
                            {
                                string key = selectedPawn.ThingID + "_" + sp.framingName;
                                activePortraits[key] = sp.pngPath;
                                AIPortraitsManager.ClearPawnActiveTextureCache(selectedPawn);
                                AIPortraitsMod.Instance.WriteSettings();
                            }
                        }

                        // Button 2: Delete
                        Rect btnDelRect = new Rect(cardRect.x + 4f, btnActiveRect.yMax + 4f, cardW - 8f, 20f);
                        if (Widgets.ButtonText(btnDelRect, "Delete"))
                        {
                            Find.WindowStack.Add(new Dialog_MessageBox(
                                "Are you sure you want to delete this portrait?",
                                "Yes",
                                delegate()
                                {
                                    try
                                    {
                                        if (isActive)
                                        {
                                            string key = selectedPawn.ThingID + "_" + sp.framingName;
                                            activePortraits.Remove(key);
                                            if (sp.framingName == "portrait")
                                            {
                                                string worldId = "global";
                                                if (Find.World != null && Find.World.info != null)
                                                    worldId = Find.World.info.persistentRandomValue.ToString();
                                                string legacyKey = worldId + "_" + selectedPawn.ThingID;
                                                activePortraits.Remove(legacyKey);
                                            }
                                            AIPortraitsManager.ClearPawnActiveTextureCache(selectedPawn);
                                            AIPortraitsMod.Instance.WriteSettings();
                                        }

                                        if (selectedSavedPortrait == sp)
                                        {
                                            selectedSavedPortrait = null;
                                            customPromptBuffer = "";
                                        }

                                        if (File.Exists(sp.pngPath)) File.Delete(sp.pngPath);
                                        if (!string.IsNullOrEmpty(sp.txtPath) && File.Exists(sp.txtPath)) File.Delete(sp.txtPath);

                                        // Also delete reference files if they exist
                                        string gearFile = Path.ChangeExtension(sp.pngPath, null) + "_ref_gear.png";
                                        if (File.Exists(gearFile)) File.Delete(gearFile);
                                        string portFile = Path.ChangeExtension(sp.pngPath, null) + "_ref_portrait.png";
                                        if (File.Exists(portFile)) File.Delete(portFile);

                                        RefreshPawnPortraitsCache(selectedPawn);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error("[Dynamic AI Portraits] Failed to delete file: " + ex.Message);
                                    }
                                },
                                "No"
                            ));
                        }
                    }

                    int rowsForSection = Mathf.CeilToInt((float)kvp.Value.Count / cols);
                    curY += rowsForSection * (cardH + margin);
                }

                Widgets.EndScrollView();
            }

            // ── RIGHT COLUMN: PAWN VIBE & PROMPT PREVIEW ──
            Widgets.DrawMenuSection(vibeAreaRect);
            Rect vibeContentRect = vibeAreaRect.ContractedBy(8f);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(vibeContentRect.x, vibeContentRect.y, vibeContentRect.width, 30f), "Custom Prompt & Preview");
            Text.Font = GameFont.Small;

            Rect vibeScrollArea = new Rect(vibeContentRect.x, vibeContentRect.y + 35f, vibeContentRect.width, vibeContentRect.height - 35f);

            // Pre-load default prompt if buffer is empty
            if (string.IsNullOrEmpty(customPromptBuffer))
            {
                if (selectedSavedPortrait != null)
                {
                    customPromptBuffer = selectedSavedPortrait.prompt;
                }
                else
                {
                    PawnState pState = AIPortraitsManager.GetCachedPawnState(selectedPawn);
                    if (pState != null)
                    {
                        customPromptBuffer = PromptCompiler.CompilePositivePrompt(pState, this);
                    }
                }
            }

            float viewWidth = vibeScrollArea.width - 16f;
            float contentHeight = 500f; // estimated content height
            Rect viewRectVibe = new Rect(0f, 0f, viewWidth, contentHeight);

            Widgets.BeginScrollView(vibeScrollArea, ref vibeScrollPosition, viewRectVibe);
            float vibeCurY = 0f;

            // 1. Status / Title
            string statusLabelText = selectedSavedPortrait != null
                ? "Editing prompt for saved portrait (" + selectedSavedPortrait.styleName + ")"
                : "New custom prompt for " + selectedPawn.LabelShortCap;
            Widgets.Label(new Rect(0f, vibeCurY, viewWidth, 22f), "<b>" + statusLabelText + "</b>");
            vibeCurY += 25f;

            // 2. Image Preview
            Texture2D previewTex = null;
            if (selectedSavedPortrait != null)
            {
                previewTex = selectedSavedPortrait.texture;
            }
            else
            {
                // Fallback to active portrait for the selected framing
                GenerationStatus previewStatus;
                string previewErr;
                previewTex = AIPortraitsManager.GetPortraitTexture(selectedPawn, out previewStatus, out previewErr);
            }

            Rect previewRect = new Rect((viewWidth - 128f) / 2f, vibeCurY, 128f, 128f);
            Widgets.DrawBoxSolid(previewRect, new Color(0.06f, 0.06f, 0.06f, 1f));
            if (previewTex != null)
            {
                GUI.DrawTexture(previewRect.ContractedBy(2f), previewTex, ScaleMode.ScaleToFit);
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(previewRect, "No Image");
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Widgets.DrawBox(previewRect, 1);
            GUI.color = Color.white;
            vibeCurY += 138f;

            // ── Draw references if they exist ──
            if (selectedSavedPortrait != null && (selectedSavedPortrait.refGearTexture != null || selectedSavedPortrait.refPortraitTexture != null))
            {
                vibeCurY += 8f;
                Widgets.Label(new Rect(0f, vibeCurY, viewWidth, 20f), "<b>References Used:</b>");
                vibeCurY += 22f;

                float refW = 80f;
                float refH = 80f;
                float refX = 0f;

                if (selectedSavedPortrait.refPortraitTexture != null)
                {
                    Rect refPortRect = new Rect(refX, vibeCurY, refW, refH);
                    Widgets.DrawBoxSolid(refPortRect, new Color(0.06f, 0.06f, 0.06f, 1f));
                    GUI.DrawTexture(refPortRect.ContractedBy(2f), selectedSavedPortrait.refPortraitTexture, ScaleMode.ScaleToFit);
                    TooltipHandler.TipRegion(refPortRect, "Portrait reference used for identity continuity.");
                    GUI.color = new Color(1f, 1f, 1f, 0.15f);
                    Widgets.DrawBox(refPortRect, 1);
                    GUI.color = Color.white;
                    refX += refW + 8f;
                }

                if (selectedSavedPortrait.refGearTexture != null)
                {
                    Rect refGearRect = new Rect(refX, vibeCurY, refW * 2f, refH); // gear sheet is horizontal/wider
                    Widgets.DrawBoxSolid(refGearRect, new Color(0.06f, 0.06f, 0.06f, 1f));
                    GUI.DrawTexture(refGearRect.ContractedBy(2f), selectedSavedPortrait.refGearTexture, ScaleMode.ScaleToFit);
                    TooltipHandler.TipRegion(refGearRect, "Gear sprite reference sheet used for item design consistency.");
                    GUI.color = new Color(1f, 1f, 1f, 0.15f);
                    Widgets.DrawBox(refGearRect, 1);
                    GUI.color = Color.white;
                    refX += refW * 2f + 8f;
                }

                vibeCurY += refH + 5f;
            }

            // 3. Prompt Header
            Widgets.Label(new Rect(0f, vibeCurY, viewWidth, 20f), "<b>Prompt:</b>");
            vibeCurY += 22f;

            // 4. Text Area for prompt
            Rect promptBoxRect = new Rect(0f, vibeCurY, viewWidth, 160f);
            customPromptBuffer = Widgets.TextArea(promptBoxRect, customPromptBuffer);
            vibeCurY += 170f;

            // 5. Buttons - Row 1
            float row1BtnW = (viewWidth - 5f) / 2f;
            Rect btnSavePrompt = new Rect(0f, vibeCurY, row1BtnW, 28f);
            Rect btnGenerate = new Rect(btnSavePrompt.xMax + 5f, vibeCurY, row1BtnW, 28f);

            // Save Prompt (only enabled if we have selected a saved portrait)
            if (selectedSavedPortrait == null)
            {
                GUI.enabled = false;
            }
            if (Widgets.ButtonText(btnSavePrompt, "Save Text Sibling"))
            {
                try
                {
                    if (selectedSavedPortrait != null && !string.IsNullOrEmpty(selectedSavedPortrait.txtPath))
                    {
                        File.WriteAllText(selectedSavedPortrait.txtPath, customPromptBuffer);
                        selectedSavedPortrait.prompt = customPromptBuffer;
                        Messages.Message("Prompt text updated on disk!", MessageTypeDefOf.TaskCompletion, false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Dynamic AI Portraits] Failed to update prompt text: " + ex.Message);
                }
            }
            GUI.enabled = true;

            // Generate Button
            GenerationStatus genStatus = AIPortraitsManager.GetStatus(selectedPawn);
            bool isGenRunning = (genStatus == GenerationStatus.Generating);
            if (isGenRunning)
            {
                GUI.enabled = false;
            }
            string genLabel = isGenRunning ? "Generating..." : "Generate Custom";
            if (Widgets.ButtonText(btnGenerate, genLabel))
            {
                AIPortraitsManager.TriggerCustomGeneration(selectedPawn, customPromptBuffer);
            }
            GUI.enabled = true;
            vibeCurY += 33f;

            // Row 2 Buttons
            Rect btnReset = new Rect(0f, vibeCurY, row1BtnW, 28f);
            Rect btnCopy = new Rect(btnReset.xMax + 5f, vibeCurY, row1BtnW, 28f);

            // Reset Button
            if (Widgets.ButtonText(btnReset, "Reset Prompt"))
            {
                if (selectedSavedPortrait != null)
                {
                    customPromptBuffer = selectedSavedPortrait.prompt;
                }
                else
                {
                    PawnState pState = AIPortraitsManager.GetCachedPawnState(selectedPawn);
                    if (pState != null)
                    {
                        customPromptBuffer = PromptCompiler.CompilePositivePrompt(pState, this);
                    }
                }
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }

            // Copy Button
            if (Widgets.ButtonText(btnCopy, "Copy Prompt"))
            {
                GUIUtility.systemCopyBuffer = customPromptBuffer;
                Messages.Message("Prompt copied to clipboard!", MessageTypeDefOf.TaskCompletion, false);
            }

            vibeCurY += 35f;
            contentHeight = vibeCurY; // dynamically adjust scrollable height
            Widgets.EndScrollView();
        }

        // ── PROMPT PREVIEW TAB ────────────────────────────────────────────────────
        // Shows the user EXACTLY what's being sent to the image API for any colonist,
        // so they can verify equipment, helmets, traits etc. are flowing through to
        // the prompt. Useful for debugging "why isn't the helmet showing up?" cases.
        private void DrawPromptEngineering(Rect inRect)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Widgets.Label(inRect, "Please load a save game to engineer prompts.");
                return;
            }

            List<Pawn> colonists = Find.ColonistBar != null
                ? Find.ColonistBar.GetColonistsInOrder()
                : null;
            if (colonists == null || colonists.Count == 0)
            {
                Widgets.Label(inRect, "No colonists found.");
                return;
            }

            if (selectedPawn == null || !colonists.Contains(selectedPawn))
            {
                selectedPawn = colonists[0];
                promptEngSelectedPortrait = null;
                promptEngBuffer = "";
            }

            // Keep the saved-portrait cache warm (shared with the Pawn Gallery tab so
            // flipping between tabs doesn't reload/dispose textures every frame).
            if (selectedPawn != lastCachedPawn)
            {
                RefreshPawnPortraitsCache(selectedPawn);
            }
            else
            {
                string scanDir = CacheManager.GetPortraitSaveDirectory(selectedPawn);
                int diskCount = 0;
                if (Directory.Exists(scanDir))
                {
                    foreach (string f in Directory.GetFiles(scanDir, "*.png"))
                    {
                        if (!f.EndsWith("_ref_gear.png") && !f.EndsWith("_ref_portrait.png"))
                            diskCount++;
                    }
                }
                if (diskCount != cachedSavedPortraits.Count)
                    RefreshPawnPortraitsCache(selectedPawn);
            }

            // ── LEFT sidebar: colonist list ──────────────────────────────────────
            const float SidebarW = 180f;
            Rect sidebarRect = new Rect(inRect.x, inRect.y, SidebarW, inRect.height);
            Widgets.DrawMenuSection(sidebarRect);
            Rect sidebarInner = sidebarRect.ContractedBy(4f);
            Rect colView = new Rect(0f, 0f, sidebarInner.width - 16f, colonists.Count * 32f);
            Widgets.BeginScrollView(sidebarInner, ref leftScrollPosition, colView);
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                Rect row = new Rect(0f, i * 32f, colView.width, 28f);
                if (p == selectedPawn) GUI.color = new Color(0.5f, 0.9f, 1f);
                if (Widgets.ButtonText(row, p.LabelShortCap))
                {
                    selectedPawn = p;
                    promptEngSelectedPortrait = null;
                    promptEngBuffer = "";
                }
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();

            float remaining = inRect.width - SidebarW - 20f;
            float leftColW  = remaining * 0.42f;
            float rightColW = remaining - leftColW - 10f;
            Rect leftCol  = new Rect(sidebarRect.xMax + 10f, inRect.y, leftColW,  inRect.height);
            Rect rightCol = new Rect(leftCol.xMax + 10f,     inRect.y, rightColW, inRect.height);

            // ── LEFT column: base-portrait selector + references + output + details ──
            Widgets.DrawMenuSection(leftCol);
            Rect leftInner = leftCol.ContractedBy(8f);
            float leftContentH = 720f + cachedSavedPortraits.Count * 70f;
            Rect leftViewRect = new Rect(0f, 0f, leftInner.width - 16f, leftContentH);
            Widgets.BeginScrollView(leftInner, ref promptEngLeftScroll, leftViewRect);
            float lw = leftViewRect.width;
            float ly = 0f;

            Widgets.Label(new Rect(0f, ly, lw, 22f), "<b>Base portrait</b>");
            ly += 24f;

            Rect newRow = new Rect(0f, ly, lw, 30f);
            if (promptEngSelectedPortrait == null) Widgets.DrawBoxSolid(newRow, new Color(0.2f, 0.35f, 0.45f, 0.6f));
            else if (Mouse.IsOver(newRow)) Widgets.DrawHighlight(newRow);
            if (Widgets.ButtonInvisible(newRow))
            {
                promptEngSelectedPortrait = null;
                SeedPromptEngBuffer();
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(newRow.x + 6f, newRow.y, newRow.width - 6f, 30f), "+ New prompt (no base image)");
            Text.Anchor = TextAnchor.UpperLeft;
            ly += 34f;

            for (int i = 0; i < cachedSavedPortraits.Count; i++)
            {
                SavedPortrait sp = cachedSavedPortraits[i];
                Rect spRow = new Rect(0f, ly, lw, 64f);
                if (sp == promptEngSelectedPortrait) Widgets.DrawBoxSolid(spRow, new Color(0.2f, 0.35f, 0.45f, 0.6f));
                else if (Mouse.IsOver(spRow)) Widgets.DrawHighlight(spRow);

                Rect thumbRect = new Rect(spRow.x + 2f, spRow.y + 2f, 60f, 60f);
                Widgets.DrawBoxSolid(thumbRect, new Color(0.06f, 0.06f, 0.06f, 1f));
                if (sp.texture != null) GUI.DrawTexture(thumbRect.ContractedBy(2f), sp.texture, ScaleMode.ScaleToFit);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(thumbRect.xMax + 6f, spRow.y, spRow.width - 70f, 64f),
                              sp.framingName + "\n" + sp.styleName + "\n" + sp.timestamp);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                if (Widgets.ButtonInvisible(spRow))
                {
                    promptEngSelectedPortrait = sp;
                    promptEngBuffer = sp.prompt != null ? sp.prompt : "";
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                }
                ly += 68f;
            }

            ly += 8f;
            Widgets.Label(new Rect(0f, ly, lw, 20f), "<b>Reference images</b>");
            ly += 22f;
            if (promptEngSelectedPortrait != null &&
                (promptEngSelectedPortrait.refPortraitTexture != null || promptEngSelectedPortrait.refGearTexture != null))
            {
                float refX = 0f;
                if (promptEngSelectedPortrait.refPortraitTexture != null)
                {
                    Rect rp = new Rect(refX, ly, 80f, 80f);
                    Widgets.DrawBoxSolid(rp, new Color(0.06f, 0.06f, 0.06f, 1f));
                    GUI.DrawTexture(rp.ContractedBy(2f), promptEngSelectedPortrait.refPortraitTexture, ScaleMode.ScaleToFit);
                    TooltipHandler.TipRegion(rp, "Portrait reference — identity continuity anchor.");
                    refX += 88f;
                }
                if (promptEngSelectedPortrait.refGearTexture != null)
                {
                    Rect rg = new Rect(refX, ly, 160f, 80f);
                    Widgets.DrawBoxSolid(rg, new Color(0.06f, 0.06f, 0.06f, 1f));
                    GUI.DrawTexture(rg.ContractedBy(2f), promptEngSelectedPortrait.refGearTexture, ScaleMode.ScaleToFit);
                    TooltipHandler.TipRegion(rg, "Gear sprite reference sheet — item design consistency.");
                }
                ly += 86f;
            }
            else
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.55f, 0.55f, 0.55f);
                Widgets.Label(new Rect(0f, ly, lw, 30f), promptEngSelectedPortrait == null
                    ? "Select a saved portrait above to see the reference images it used."
                    : "No reference images were saved for this portrait.");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                ly += 32f;
            }

            ly += 8f;
            Widgets.Label(new Rect(0f, ly, lw, 20f), "<b>Output</b>");
            ly += 22f;
            Texture2D outTex;
            if (promptEngSelectedPortrait != null)
            {
                outTex = promptEngSelectedPortrait.texture;
            }
            else
            {
                GenerationStatus ost; string oerr;
                outTex = AIPortraitsManager.GetPortraitTexture(selectedPawn, out ost, out oerr);
            }
            Rect outRect = new Rect(0f, ly, 120f, 120f);
            Widgets.DrawBoxSolid(outRect, new Color(0.06f, 0.06f, 0.06f, 1f));
            if (outTex != null) GUI.DrawTexture(outRect.ContractedBy(2f), outTex, ScaleMode.ScaleToFit);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Widgets.DrawBox(outRect, 1);
            GUI.color = Color.white;
            ly += 128f;

            Widgets.Label(new Rect(0f, ly, lw, 20f), "<b>Details</b>");
            ly += 22f;
            Text.Font = GameFont.Tiny;
            string styleStr   = promptEngSelectedPortrait != null ? promptEngSelectedPortrait.styleName : portraitStyle.ToString();
            string framingStr = promptEngSelectedPortrait != null ? promptEngSelectedPortrait.framingName : AIPortraitsManager.GetActiveFraming(selectedPawn);
            string tsStr      = promptEngSelectedPortrait != null ? promptEngSelectedPortrait.timestamp : "—";
            string modeStr    = useLLMPrompt ? (llmModelType == LLMModelType.GeminiFlashLite ? "Gemini Flash" : "Gemma 4 26B") : "Compiled template";
            string modelStr   = string.IsNullOrEmpty(CurrentModelName) ? "(server default)" : CurrentModelName;
            string detail =
                "Provider: " + ProviderLabel(backendType) + "\n" +
                "Model: " + modelStr + "\n" +
                "Prompt mode: " + modeStr + "\n" +
                "Art style: " + styleStr + "\n" +
                "Framing: " + framingStr + "\n" +
                "Gear reference sheet: " + (useGearReferenceSheet ? "on" : "off") + "\n" +
                "Timestamp: " + tsStr;
            if (promptEngSelectedPortrait != null && !string.IsNullOrEmpty(promptEngSelectedPortrait.pngPath))
                detail += "\nFile: " + Path.GetFileName(promptEngSelectedPortrait.pngPath);
            float detailH = Text.CalcHeight(detail, lw);
            Widgets.Label(new Rect(0f, ly, lw, detailH), detail);
            Text.Font = GameFont.Small;
            ly += detailH + 8f;

            Widgets.EndScrollView();

            // ── RIGHT column: editable prompt + actions + last sent + data sheet ──
            Widgets.DrawMenuSection(rightCol);
            Rect rightInner = rightCol.ContractedBy(8f);

            PawnState pstate = AIPortraitsManager.GetCachedPawnState(selectedPawn);
            if (pstate == null) pstate = PawnStateExtractor.ExtractState(selectedPawn);
            string dataSheet = pstate != null ? PromptCompiler.CompilePawnStateDescription(pstate, this) : "(could not extract pawn data)";
            string framing2  = AIPortraitsManager.GetActiveFraming(selectedPawn);
            string llmSystem = (useLLMPrompt && promptEngShowLlmSystem) ? PromptCompiler.GetLLMSystemPrompt(portraitStyle, this, framing2) : null;

            if (string.IsNullOrEmpty(promptEngBuffer)) SeedPromptEngBuffer();

            string lastActualPrompt = null;
            string lastActualFile   = null;
            try
            {
                string ddir = CacheManager.GetPortraitSaveDirectory(selectedPawn);
                if (Directory.Exists(ddir))
                {
                    string[] txts = Directory.GetFiles(ddir, "*.txt");
                    DateTime latest = DateTime.MinValue;
                    for (int i = 0; i < txts.Length; i++)
                    {
                        DateTime t = File.GetLastWriteTime(txts[i]);
                        if (t > latest) { latest = t; lastActualFile = txts[i]; }
                    }
                    if (lastActualFile != null) lastActualPrompt = File.ReadAllText(lastActualFile);
                }
            }
            catch (Exception) { }

            float rw = rightInner.width - 16f;
            float dataH = Text.CalcHeight(dataSheet, rw - 12f);
            float lastH = (promptEngShowLastSent && !string.IsNullOrEmpty(lastActualPrompt)) ? Text.CalcHeight(lastActualPrompt, rw - 12f) : 0f;
            float llmH  = llmSystem != null ? Text.CalcHeight(llmSystem, rw - 12f) : 0f;
            float rTotalH = dataH + lastH + llmH + 560f;
            Rect rView = new Rect(0f, 0f, rw, rTotalH);
            Widgets.BeginScrollView(rightInner, ref promptScrollPosition, rView);
            float ry = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, ry, rw, 28f), "Prompt Engineering — " + selectedPawn.LabelShortCap);
            ry += 32f;
            Text.Font = GameFont.Small;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(0f, ry, rw, 18f), useLLMPrompt
                ? "Your edited prompt is sent as-is to the image API (the LLM rewriter is bypassed for custom prompts)."
                : "Your edited prompt is sent directly to the image API.");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            ry += 22f;

            Widgets.Label(new Rect(0f, ry, rw, 20f), "<b>Prompt (editable):</b>");
            ry += 22f;
            Rect editRect = new Rect(0f, ry, rw, 160f);
            promptEngBuffer = Widgets.TextArea(editRect, promptEngBuffer);
            ry += 168f;

            float bw = (rw - 5f) / 2f;
            Rect bGen   = new Rect(0f, ry, bw, 28f);
            Rect bReset = new Rect(bGen.xMax + 5f, ry, bw, 28f);
            GenerationStatus gs = AIPortraitsManager.GetStatus(selectedPawn);
            bool genRunning = (gs == GenerationStatus.Generating);
            if (genRunning) GUI.enabled = false;
            if (Widgets.ButtonText(bGen, genRunning ? "Generating..." : "Generate"))
            {
                AIPortraitsManager.TriggerCustomGeneration(selectedPawn, promptEngBuffer);
            }
            GUI.enabled = true;
            if (Widgets.ButtonText(bReset, "Reset to compiled"))
            {
                promptEngSelectedPortrait = null;
                SeedPromptEngBuffer();
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }
            ry += 33f;

            Rect bCopy = new Rect(0f, ry, bw, 28f);
            Rect bSave = new Rect(bCopy.xMax + 5f, ry, bw, 28f);
            if (Widgets.ButtonText(bCopy, "Copy"))
            {
                GUIUtility.systemCopyBuffer = promptEngBuffer;
                Messages.Message("Prompt copied to clipboard.", MessageTypeDefOf.TaskCompletion, false);
            }
            bool canSave = promptEngSelectedPortrait != null && !string.IsNullOrEmpty(promptEngSelectedPortrait.txtPath);
            if (!canSave) GUI.enabled = false;
            if (Widgets.ButtonText(bSave, "Save to .txt"))
            {
                try
                {
                    File.WriteAllText(promptEngSelectedPortrait.txtPath, promptEngBuffer);
                    promptEngSelectedPortrait.prompt = promptEngBuffer;
                    Messages.Message("Prompt saved to the portrait's .txt file.", MessageTypeDefOf.TaskCompletion, false);
                }
                catch (Exception ex)
                {
                    Log.Error("[Dynamic AI Portraits] Failed to save prompt text: " + ex.Message);
                }
            }
            GUI.enabled = true;
            ry += 38f;

            // Last actually sent (foldout)
            if (Widgets.ButtonText(new Rect(0f, ry, 24f, 22f), promptEngShowLastSent ? "-" : "+"))
                promptEngShowLastSent = !promptEngShowLastSent;
            Widgets.Label(new Rect(28f, ry, rw - 28f, 22f), "<b>Last actually sent</b>"
                + (lastActualFile != null ? "  (" + Path.GetFileName(lastActualFile) + ")" : ""));
            ry += 26f;
            if (promptEngShowLastSent)
            {
                if (!string.IsNullOrEmpty(lastActualPrompt))
                {
                    Rect bCopyLast = new Rect(0f, ry, 150f, 22f);
                    if (Widgets.ButtonText(bCopyLast, "Copy last sent"))
                    {
                        GUIUtility.systemCopyBuffer = lastActualPrompt;
                        Messages.Message("Last sent prompt copied.", MessageTypeDefOf.TaskCompletion, false);
                    }
                    Rect bLoad = new Rect(bCopyLast.xMax + 5f, ry, 170f, 22f);
                    if (Widgets.ButtonText(bLoad, "Load into editor"))
                    {
                        promptEngBuffer = lastActualPrompt;
                        SoundDefOf.Click.PlayOneShotOnCamera(null);
                    }
                    ry += 26f;
                    Rect lastBox = new Rect(0f, ry, rw, lastH + 10f);
                    Widgets.DrawBoxSolid(lastBox, new Color(0.08f, 0.05f, 0.12f, 1f));
                    GUI.color = new Color(0.6f, 0.45f, 0.85f, 0.7f);
                    Widgets.DrawBox(lastBox, 1);
                    GUI.color = Color.white;
                    Widgets.Label(lastBox.ContractedBy(6f), lastActualPrompt);
                    ry += lastBox.height + 12f;
                }
                else
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.55f, 0.55f, 0.55f);
                    Widgets.Label(new Rect(0f, ry, rw, 22f), "No prompts recorded yet — generate a portrait first.");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    ry += 24f;
                }
            }

            // Pawn data sheet (read-only)
            Widgets.Label(new Rect(0f, ry, rw, 22f), "<b>Pawn data sheet (what's extracted):</b>");
            ry += 24f;
            if (Widgets.ButtonText(new Rect(0f, ry, 140f, 22f), "Copy data sheet"))
            {
                GUIUtility.systemCopyBuffer = dataSheet;
                Messages.Message("Data sheet copied.", MessageTypeDefOf.TaskCompletion, false);
            }
            ry += 26f;
            Rect dataBox = new Rect(0f, ry, rw, dataH + 10f);
            Widgets.DrawBoxSolid(dataBox, new Color(0.05f, 0.08f, 0.12f, 1f));
            Widgets.DrawBox(dataBox, 1);
            Widgets.Label(dataBox.ContractedBy(6f), dataSheet);
            ry += dataBox.height + 12f;

            // LLM system instruction (foldout, only when LLM mode on)
            if (useLLMPrompt)
            {
                if (Widgets.ButtonText(new Rect(0f, ry, 24f, 22f), promptEngShowLlmSystem ? "-" : "+"))
                    promptEngShowLlmSystem = !promptEngShowLlmSystem;
                Widgets.Label(new Rect(28f, ry, rw - 28f, 22f), "<b>LLM system instruction</b>");
                ry += 26f;
                if (promptEngShowLlmSystem && llmSystem != null)
                {
                    Rect llmBox = new Rect(0f, ry, rw, llmH + 10f);
                    Widgets.DrawBoxSolid(llmBox, new Color(0.05f, 0.10f, 0.08f, 1f));
                    Widgets.DrawBox(llmBox, 1);
                    Widgets.Label(llmBox.ContractedBy(6f), llmSystem);
                    ry += llmBox.height + 12f;
                }
            }

            Widgets.EndScrollView();
        }

        // Seeds the Prompt Engineering editor: from the selected saved portrait's prompt,
        // otherwise from the freshly compiled template for the selected pawn.
        private void SeedPromptEngBuffer()
        {
            if (promptEngSelectedPortrait != null)
            {
                promptEngBuffer = promptEngSelectedPortrait.prompt != null ? promptEngSelectedPortrait.prompt : "";
                return;
            }
            PawnState ps = AIPortraitsManager.GetCachedPawnState(selectedPawn);
            if (ps == null) ps = PawnStateExtractor.ExtractState(selectedPawn);
            if (ps != null) promptEngBuffer = PromptCompiler.CompilePositivePrompt(ps, this);
        }
    }
}
