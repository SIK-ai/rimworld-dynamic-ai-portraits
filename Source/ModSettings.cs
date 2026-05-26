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

    public class AIPortraitsSettings : ModSettings
    {
        public BackendType backendType = BackendType.Pollinations;

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
        public string manhwaStylePrompt = "highly detailed digital illustration, professional webtoon manhwa key visual, sharp dynamic clean inked outlines, deep volumetric shading, dramatic cinematic lighting with rich chiaroscuro contrast, subtle rim lighting to define the silhouette, vibrant saturated colors, cinematic composition, exquisite detailed expressive eyes with realistic reflections, beautifully styled glossy hair flows, pristine smooth skin rendering, masterpiece anime art, aesthetic design";
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

        // LLM-assisted prompt generation (Gemini Flash)
        public bool   useLLMPrompt = false;
        public string llmApiKey    = "";

        // AI Background Removal (Cloudflare Bria-RMBG-1.4)
        public bool useAIBgRemoval = false;
        public string cfBgRemovalKey = "";

        private Vector2 scrollPosition = Vector2.zero;

        public Dictionary<string, string> activePortraits = new Dictionary<string, string>();
        public Dictionary<string, string> pawnFraming = new Dictionary<string, string>();
        public Dictionary<string, bool> pawnVideoToggles = new Dictionary<string, bool>();

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
            }
            Scribe_Values.Look(ref portraitStyle, "portraitStyle", PortraitStyle.Realistic_Korean);
            Scribe_Values.Look(ref baseStylePrompt, "baseStylePrompt", "");
            Scribe_Values.Look(ref manhwaStylePrompt, "manhwaStylePrompt", "highly detailed digital illustration, professional webtoon manhwa key visual, sharp dynamic clean inked outlines, deep volumetric shading, dramatic cinematic lighting with rich chiaroscuro contrast, subtle rim lighting to define the silhouette, vibrant saturated colors, cinematic composition, exquisite detailed expressive eyes with realistic reflections, beautifully styled glossy hair flows, pristine smooth skin rendering, masterpiece anime art, aesthetic design");
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
            Scribe_Values.Look(ref useLLMPrompt,       "useLLMPrompt",     false);
            Scribe_Values.Look(ref llmApiKey,          "llmApiKey",        "");
            Scribe_Values.Look(ref useAIBgRemoval,     "useAIBgRemoval",   false);
            Scribe_Values.Look(ref cfBgRemovalKey,     "cfBgRemovalKey",   "");
            Scribe_Collections.Look(ref activePortraits, "activePortraits", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref pawnFraming, "pawnFraming", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref pawnVideoToggles, "pawnVideoToggles", LookMode.Value, LookMode.Value);

            if (activePortraits == null)
                activePortraits = new Dictionary<string, string>();
            if (pawnFraming == null)
                pawnFraming = new Dictionary<string, string>();
            if (pawnVideoToggles == null)
                pawnVideoToggles = new Dictionary<string, bool>();
        }

        // UI states (not serialized)
        private int activeTab = 0; // 0 = API Settings, 1 = Pawn Gallery, 2 = Prompt Preview
        private bool showAdvanced = false;
        private Vector2 leftScrollPosition = Vector2.zero;
        private Vector2 rightScrollPosition = Vector2.zero;
        private Vector2 vibeScrollPosition = Vector2.zero;
        private Vector2 promptScrollPosition = Vector2.zero;
        private Pawn selectedPawn = null;
        private Pawn promptTabSelectedPawn = null;
        private SavedPortrait selectedSavedPortrait = null;
        private string customPromptBuffer = "";

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
                            if (parts.Length >= 5) // New format: Name_Style_Framing_Date_Time
                            {
                                sp.styleName = parts[1];
                                sp.framingName = parts[2];
                                sp.timestamp = parts[3].Replace('-', ':') + " " + parts[4].Replace('-', ':');
                            }
                            else if (parts.Length == 4) // Legacy format: Name_Style_Date_Time
                            {
                                sp.styleName = parts[1];
                                sp.framingName = "portrait"; // Default to portrait
                                sp.timestamp = parts[2].Replace('-', ':') + " " + parts[3].Replace('-', ':');
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
            tabs.Add(new TabRecord("Prompt Preview", () => { activeTab = 2; }, activeTab == 2));

            TabDrawer.DrawTabs(tabRect, tabs);

            // Main content sits below the tab strip
            float mainTop    = inRect.y + TitleBarPadding + 40f;
            float mainHeight = inRect.height - TitleBarPadding - 40f;
            Rect mainRect = new Rect(inRect.x, mainTop, inRect.width, mainHeight);

            if      (activeTab == 0) DrawApiSettings(mainRect);
            else if (activeTab == 1) DrawPawnGallery(mainRect);
            else if (activeTab == 2) DrawPromptPreview(mainRect);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // PROVIDER REGISTRY — single source of truth for label, models, defaults,
        // API key requirements, and info text. Adding a new provider means adding
        // a new switch case in each of these helpers + a coroutine in AsyncAIClient.
        // ──────────────────────────────────────────────────────────────────────────

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
            float viewHeight = 830f;
            if (useLLMPrompt) viewHeight += 60f;
            if (useAIBgRemoval) viewHeight += 60f;
            if (showAdvanced) viewHeight += 320f;

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
            if (Widgets.ButtonText(btnKorean,  "🎨 Manhwa"))  portraitStyle = PortraitStyle.Realistic_Korean;
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

            // ── BACKEND TOGGLE ────────────────────────────────────────────────────
            listing.Label("AI Backend");
            listing.Gap(6f);

            // ─── PROVIDER DROPDOWN ───────────────────────────────────────────────
            Rect providerRow = listing.GetRect(34f);
            Widgets.Label(new Rect(providerRow.x, providerRow.y + 8f, 90f, 24f), "Provider:");
            Rect providerBtn = new Rect(providerRow.x + 90f, providerRow.y, providerRow.width - 90f, 34f);
            if (Widgets.ButtonText(providerBtn, ProviderLabel(backendType)))
            {
                var opts = new List<FloatMenuOption>();
                BackendType[] order = new[] {
                    BackendType.Pollinations,
                    BackendType.Cloudflare,
                    BackendType.GoogleImagen,
                    BackendType.DeepInfra,
                    BackendType.HuggingFace,
                    BackendType.LocalA1111
                };
                foreach (BackendType bt in order)
                {
                    BackendType captured = bt;
                    opts.Add(new FloatMenuOption(ProviderLabel(bt), delegate () { ApplyProviderDefaults(captured); }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            listing.Gap(6f);

            // ─── MODEL DROPDOWN (provider-filtered) ──────────────────────────────
            string[] availableModels = ModelsForProvider(backendType);
            if (availableModels != null && availableModels.Length > 0)
            {
                Rect modelRow = listing.GetRect(34f);
                Widgets.Label(new Rect(modelRow.x, modelRow.y + 8f, 90f, 24f), "Model:");
                Rect modelBtn = new Rect(modelRow.x + 90f, modelRow.y, modelRow.width - 90f, 34f);
                string currentModel = string.IsNullOrEmpty(CurrentModelName) ? availableModels[0] : CurrentModelName;
                if (Widgets.ButtonText(modelBtn, currentModel))
                {
                    var opts = new List<FloatMenuOption>();
                    foreach (string m in availableModels)
                    {
                        string captured = m;
                        opts.Add(new FloatMenuOption(m, delegate () { CurrentModelName = captured; }));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
                listing.Gap(6f);
            }

            // ─── PROVIDER INFO BOX ───────────────────────────────────────────────
            string infoText = ProviderInfo(backendType);
            float infoHeight = Text.CalcHeight(infoText, listing.ColumnWidth - 12f) + 12f;
            Rect infoBoxRect = listing.GetRect(infoHeight);
            Widgets.DrawBoxSolid(infoBoxRect, ProviderInfoColor(backendType));
            Text.Font = GameFont.Tiny;
            Widgets.Label(infoBoxRect.ContractedBy(6f), infoText);
            Text.Font = GameFont.Small;
            listing.Gap(6f);

            // ─── API KEY FIELD (single field; format varies per provider) ─────────
            // Masked with password-field stars so the key isn't visible during screen sharing
            // (originally proposed by Jules-bot sentinel/mask-api-key).
            if (ProviderNeedsApiKey(backendType))
            {
                listing.Label(ApiKeyLabel(backendType));
                Rect apiKeyRect = listing.GetRect(24f);
                CurrentApiKey = UnityEngine.GUI.PasswordField(apiKeyRect, CurrentApiKey, '*');
                listing.Gap(listing.verticalSpacing);
                listing.Gap(2f);
                Text.Font = GameFont.Tiny;

                // Inline validation: red warning when key is missing for a provider that needs it
                // (originally proposed by Jules-bot palette-ux-improvements).
                if (string.IsNullOrEmpty(CurrentApiKey))
                {
                    GUI.color = new Color(0.9f, 0.3f, 0.3f);
                    Widgets.Label(listing.GetRect(20f),
                        "  ⚠ API key required for " + ProviderLabel(backendType).Trim());
                }
                else
                {
                    GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    Widgets.Label(listing.GetRect(20f), "  " + ApiKeyHint(backendType));
                }
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            // ─── LOCAL SERVER URL (only when LocalA1111 selected) ─────────────────
            if (backendType == BackendType.LocalA1111)
            {
                listing.Gap(4f);
                listing.Label("Server URL");
                CurrentApiUrl = listing.TextEntry(CurrentApiUrl);
                listing.Gap(2f);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(listing.GetRect(20f), "  Default: http://127.0.0.1:7860 (A1111 / Forge default)");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(6f);

            // ── PORTRAIT DETAILS SETTINGS ─────────────────────────────────────────
            listing.Label("Portrait Details Settings");
            listing.Gap(4f);
            listing.CheckboxLabeled("Include Ideology details", ref includeIdeology, "Include pawn's ideology role (e.g. Moral Guide) and follower description/iconography.");
            listing.CheckboxLabeled("Include Ideology Rim Lighting", ref includeRimLighting, "Separate the character silhouette from the background using a rim light styled with their favorite/ideoligion color.");
            listing.CheckboxLabeled("Use Gear Reference Sheet (Gemini)", ref useGearReferenceSheet, "Stitch matched weapon/apparel sprites into a single reference image for Gemini models. Helps retain equipment designs across generations.");
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

            // ── PROMPT GENERATION ─────────────────────────────────────────────────
            listing.Label("Prompt Generation");
            listing.Gap(6f);

            // Two-button toggle: No (compiled template) vs Gemini Flash Lite
            Rect promptModeRow  = listing.GetRect(36f);
            float promptHalfW   = promptModeRow.width / 2f;
            Rect btnNoLLM       = new Rect(promptModeRow.x,                promptModeRow.y, promptHalfW - 4f, 36f);
            Rect btnGeminiFlash = new Rect(promptModeRow.x + promptHalfW,  promptModeRow.y, promptHalfW - 4f, 36f);

            if (!useLLMPrompt) GUI.color = new Color(0.5f, 0.85f, 0.5f);
            if (Widgets.ButtonText(btnNoLLM, "No — Compiled Template"))
                useLLMPrompt = false;
            GUI.color = Color.white;

            if (useLLMPrompt) GUI.color = new Color(0.5f, 0.75f, 1f);
            if (Widgets.ButtonText(btnGeminiFlash, "Gemini Flash Lite"))
                useLLMPrompt = true;
            GUI.color = Color.white;

            listing.Gap(8f);

            Rect promptInfoRect = listing.GetRect(48f);
            Widgets.DrawBoxSolid(promptInfoRect,
                useLLMPrompt
                    ? new Color(0.08f, 0.14f, 0.22f, 0.7f)
                    : new Color(0.10f, 0.16f, 0.10f, 0.7f));
            Text.Font = GameFont.Tiny;
            Widgets.Label(promptInfoRect.ContractedBy(6f),
                useLLMPrompt
                    ? "Gemini Flash Lite rewrites the structured pawn data into an optimized,\n" +
                      "creative image prompt. Requires a Google AI Studio API key (free).\n" +
                      "Falls back to the compiled template if the call fails."
                    : "Built-in template compiler builds the prompt deterministically from\n" +
                      "pawn state. Fast, free, no extra API key. Less creative than Gemini\n" +
                      "but reliable and predictable.");
            Text.Font = GameFont.Small;
            listing.Gap(6f);

            if (useLLMPrompt)
            {
                // If using Imagen the same key works for Gemini Flash — show a green note.
                bool canReuseImagenKey = (!string.IsNullOrEmpty(giApiKey));
                if (canReuseImagenKey)
                {
                    Rect reuseBox = listing.GetRect(30f);
                    Widgets.DrawBoxSolid(reuseBox, new Color(0.05f, 0.18f, 0.05f, 0.8f));
                    GUI.color = new Color(1f, 1f, 1f, 0.15f);
                    Widgets.DrawBox(reuseBox, 1);
                    GUI.color = Color.white;
                    Text.Font   = GameFont.Tiny;
                    GUI.color   = new Color(0.5f, 0.95f, 0.5f);
                    Widgets.Label(reuseBox.ContractedBy(5f), "\u2713  Using your Imagen API key for Gemini Flash \u2014 no extra key needed.");
                    GUI.color   = Color.white;
                    Text.Font   = GameFont.Small;
                }
                else
                {
                    listing.Label("Gemini Flash API Key  (Google AI Studio):");
                    Rect llmKeyRect = listing.GetRect(24f);
                    llmApiKey = UnityEngine.GUI.PasswordField(llmKeyRect, llmApiKey, '*');
                    listing.Gap(listing.verticalSpacing);
                    listing.Gap(2f);
                    Rect hintRect = listing.GetRect(20f);
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.55f, 0.55f, 0.55f);
                    Widgets.Label(hintRect, "  Free key at aistudio.google.com/app/apikey  \u2022  Your Imagen key also works here.");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }
                listing.Gap(4f);
            }

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
                listing.Label("Steps: " + steps + "  (HuggingFace only)");
                steps = (int)listing.Slider(steps, 5, 50);

                listing.Label("CFG Scale: " + cfgScale.ToString("F1") + "  (HuggingFace only)");
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
        private void DrawPromptPreview(Rect inRect)
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                Widgets.Label(inRect, "Please load a save game to preview prompts.");
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

            if (promptTabSelectedPawn == null || !colonists.Contains(promptTabSelectedPawn))
                promptTabSelectedPawn = colonists[0];

            // ── LEFT: colonist list ──────────────────────────────────────────────
            const float SidebarW = 180f;
            Rect sidebarRect = new Rect(inRect.x, inRect.y, SidebarW, inRect.height);
            Widgets.DrawMenuSection(sidebarRect);
            Rect sidebarInner = sidebarRect.ContractedBy(4f);
            Rect leftView     = new Rect(0f, 0f, sidebarInner.width - 16f, colonists.Count * 32f);
            Widgets.BeginScrollView(sidebarInner, ref leftScrollPosition, leftView);
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                Rect row = new Rect(0f, i * 32f, leftView.width, 28f);
                if (p == promptTabSelectedPawn) GUI.color = new Color(0.5f, 0.9f, 1f);
                if (Widgets.ButtonText(row, p.LabelShortCap))
                    promptTabSelectedPawn = p;
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();

            // ── RIGHT: prompt content ────────────────────────────────────────────
            Rect rightArea = new Rect(sidebarRect.xMax + 10f, inRect.y,
                                       inRect.width - SidebarW - 10f, inRect.height);
            Widgets.DrawMenuSection(rightArea);
            Rect content = rightArea.ContractedBy(10f);

            PawnState state = PawnStateExtractor.ExtractState(promptTabSelectedPawn);
            if (state == null)
            {
                Widgets.Label(content, "Could not extract state for " + promptTabSelectedPawn.LabelShortCap);
                return;
            }

            string framing = "portrait";
            if (promptTabSelectedPawn != null && pawnFraming != null)
                pawnFraming.TryGetValue(promptTabSelectedPawn.ThingID, out framing);

            state.framing = framing;

            string structuredDesc = PromptCompiler.CompilePawnStateDescription(state, this);
            string compiledPrompt = PromptCompiler.CompilePositivePrompt(state, this, null);
            string llmSystem      = useLLMPrompt ? PromptCompiler.GetLLMSystemPrompt(portraitStyle, this, framing) : null;

            // Read the most recent .txt sibling next to this pawn's saved PNGs — that's
            // the canonical "what was actually sent to the image API last time" record.
            string lastActualPrompt = null;
            string lastActualFile   = null;
            try
            {
                string dir = CacheManager.GetPortraitSaveDirectory(promptTabSelectedPawn);
                if (Directory.Exists(dir))
                {
                    string[] txts = Directory.GetFiles(dir, "*.txt");
                    DateTime latestT = DateTime.MinValue;
                    for (int i = 0; i < txts.Length; i++)
                    {
                        DateTime t = File.GetLastWriteTime(txts[i]);
                        if (t > latestT) { latestT = t; lastActualFile = txts[i]; }
                    }
                    if (lastActualFile != null)
                        lastActualPrompt = File.ReadAllText(lastActualFile);
                }
            }
            catch (Exception) { /* ignore — just show "none recorded" */ }

            float viewW = content.width - 18f;
            float descH    = Text.CalcHeight(structuredDesc, viewW - 12f);
            float promptH  = Text.CalcHeight(compiledPrompt, viewW - 12f);
            float llmH     = llmSystem != null ? Text.CalcHeight(llmSystem, viewW - 12f) : 0f;
            float lastH    = !string.IsNullOrEmpty(lastActualPrompt) ? Text.CalcHeight(lastActualPrompt, viewW - 12f) : 0f;
            float totalH   = descH + promptH + llmH + lastH + 360f;

            Rect view = new Rect(0f, 0f, viewW, totalH);
            Widgets.BeginScrollView(content, ref promptScrollPosition, view);

            float y = 0f;

            // Heading
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, viewW, 28f),
                          "Prompt preview for " + promptTabSelectedPawn.LabelShortCap);
            y += 32f;
            Text.Font = GameFont.Small;

            // Mode indicator
            Text.Font = GameFont.Tiny;
            GUI.color = useLLMPrompt ? new Color(0.4f, 0.85f, 1f) : new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(0f, y, viewW, 18f),
                          useLLMPrompt
                            ? "Mode: Gemini Flash will rewrite the structured data below into a custom image prompt."
                            : "Mode: Compiled template (LLM mode off — toggle in API Settings to enable).");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;

            // ── 0. Last actual prompt sent (read from disk, no API call) ─────────
            // This is the canonical record of what was sent to the image API on the
            // most recent generation for this pawn. If you used Gemini Flash mode,
            // THIS is the Gemini-rewritten prompt. If template mode, this is the
            // compiled string. Sourced from the .txt sibling next to the saved PNG.
            if (!string.IsNullOrEmpty(lastActualPrompt))
            {
                string fileBaseName = Path.GetFileName(lastActualFile);
                Widgets.Label(new Rect(0f, y, viewW, 22f),
                              "<b>Last actual prompt sent</b>  (from " + fileBaseName + "):");
                y += 24f;
                Rect copyLastBtn = new Rect(0f, y, 150f, 22f);
                if (Widgets.ButtonText(copyLastBtn, "Copy last sent prompt"))
                {
                    GUIUtility.systemCopyBuffer = lastActualPrompt;
                    Messages.Message("Last sent prompt copied to clipboard.", MessageTypeDefOf.TaskCompletion, false);
                }
                y += 26f;
                Rect lastBox = new Rect(0f, y, viewW, lastH + 10f);
                Widgets.DrawBoxSolid(lastBox, new Color(0.08f, 0.05f, 0.12f, 1f));
                GUI.color = new Color(0.6f, 0.45f, 0.85f, 0.7f);
                Widgets.DrawBox(lastBox, 1);
                GUI.color = Color.white;
                Widgets.Label(lastBox.ContractedBy(6f), lastActualPrompt);
                y += lastBox.height + 18f;
            }
            else
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.55f, 0.55f, 0.55f);
                Widgets.Label(new Rect(0f, y, viewW, 22f),
                              "No prompts yet recorded for this pawn — generate a portrait first.");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 26f;
            }

            // ── 1. Structured pawn description (what the LLM receives, also human-readable)
            Widgets.Label(new Rect(0f, y, viewW, 22f), "<b>Pawn data sheet (what's extracted):</b>");
            y += 24f;
            Rect copyDescBtn = new Rect(0f, y, 130f, 22f);
            if (Widgets.ButtonText(copyDescBtn, "Copy data sheet"))
            {
                GUIUtility.systemCopyBuffer = structuredDesc;
                Messages.Message("Data sheet copied to clipboard.", MessageTypeDefOf.TaskCompletion, false);
            }
            y += 26f;
            Rect descBox = new Rect(0f, y, viewW, descH + 10f);
            Widgets.DrawBoxSolid(descBox, new Color(0.05f, 0.08f, 0.12f, 1f));
            Widgets.DrawBox(descBox, 1);
            Widgets.Label(descBox.ContractedBy(6f), structuredDesc);
            y += descBox.height + 16f;

            // ── 2. Compiled prompt (always shown — this is what the image API receives
            //       directly when LLM mode is OFF, or as a fallback when LLM fails)
            Widgets.Label(new Rect(0f, y, viewW, 22f),
                useLLMPrompt
                    ? "<b>Compiled prompt (fallback if Gemini fails):</b>"
                    : "<b>Compiled prompt (sent to image API):</b>");
            y += 24f;
            Rect copyPromptBtn = new Rect(0f, y, 130f, 22f);
            if (Widgets.ButtonText(copyPromptBtn, "Copy prompt"))
            {
                GUIUtility.systemCopyBuffer = compiledPrompt;
                Messages.Message("Compiled prompt copied to clipboard.", MessageTypeDefOf.TaskCompletion, false);
            }
            y += 26f;
            Rect promptBox = new Rect(0f, y, viewW, promptH + 10f);
            Widgets.DrawBoxSolid(promptBox, new Color(0.05f, 0.08f, 0.12f, 1f));
            Widgets.DrawBox(promptBox, 1);
            Widgets.Label(promptBox.ContractedBy(6f), compiledPrompt);
            y += promptBox.height + 16f;

            // ── 3. LLM system prompt (only when LLM mode is on)
            if (llmSystem != null)
            {
                Widgets.Label(new Rect(0f, y, viewW, 22f),
                              "<b>LLM system instruction (sent to Gemini Flash):</b>");
                y += 24f;
                Rect llmBox = new Rect(0f, y, viewW, llmH + 10f);
                Widgets.DrawBoxSolid(llmBox, new Color(0.05f, 0.10f, 0.08f, 1f));
                Widgets.DrawBox(llmBox, 1);
                Widgets.Label(llmBox.ContractedBy(6f), llmSystem);
                y += llmBox.height + 16f;
            }

            Widgets.EndScrollView();
        }
    }
}
