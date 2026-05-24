using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;
using RimWorld;

namespace AIPortraits
{
    public enum BackendType
    {
        Replicate,
        HuggingFace,
        Pollinations,
        GoogleImagen,
        LocalA1111,   // AUTOMATIC1111 / Forge / SD.Next / ComfyUI (A1111-compat) on localhost
        Custom
    }

    public enum PortraitStyle
    {
        Realistic_Korean,   // Semi-realistic, Korean RPG fan-art style (dramatic, warm/cool contrast)
        Realistic_Western,  // Western fantasy (Path of Exile / Pillars of Eternity)
        DotPixel            // Pixel art / dot-style retro RPG
    }

    public class AIPortraitsSettings : ModSettings
    {
        public BackendType backendType = BackendType.Pollinations;
        public string apiKey = "";
        public string apiUrl = "https://image.pollinations.ai";
        public string modelName = "sana";

        public PortraitStyle portraitStyle = PortraitStyle.Realistic_Korean;

        // User-appended style suffix (overrides nothing, just appends)
        public string baseStylePrompt = "";
        public string baseNegativePrompt = "generic fantasy art, cartoon style, bright cheerful lighting, flat lighting, blurry textures, generic features, messy brushstrokes, standard clean weapon, generic clothing, missing face tattoos, missing horns, missing cybernetic eyes, missing scars, photorealistic photograph, 3d render, chibi, flat shading, low quality, watermark, extra limbs, deformed face, bad anatomy, multiple people, text, signature, anti-aliased pixels, jagged irregular lines, muddied unclear character design, illegible text, generic UI, inconsistency between sprite and portrait";

        public float cfgScale = 7f;
        public int steps = 20;

        public bool forceOpenEyes = true;
        public bool allowCuteProps = true;
        public bool includeIdeology = true;
        public bool includeRimLighting = true;

        // LLM-assisted prompt generation (Gemini Flash)
        public bool   useLLMPrompt = false;
        public string llmApiKey    = "";

        private Vector2 scrollPosition = Vector2.zero;

        public Dictionary<string, string> activePortraits = new Dictionary<string, string>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref backendType, "backendType", BackendType.Pollinations);
            Scribe_Values.Look(ref apiKey, "apiKey", "");
            Scribe_Values.Look(ref apiUrl, "apiUrl", "https://image.pollinations.ai");
            Scribe_Values.Look(ref modelName, "modelName", "sana");
            Scribe_Values.Look(ref portraitStyle, "portraitStyle", PortraitStyle.Realistic_Korean);
            Scribe_Values.Look(ref baseStylePrompt, "baseStylePrompt", "");
            Scribe_Values.Look(ref baseNegativePrompt, "baseNegativePrompt", "");
            Scribe_Values.Look(ref cfgScale, "cfgScale", 7f);
            Scribe_Values.Look(ref steps, "steps", 20);
            Scribe_Values.Look(ref forceOpenEyes,     "forceOpenEyes",    true);
            Scribe_Values.Look(ref allowCuteProps,     "allowCuteProps",   true);
            Scribe_Values.Look(ref includeIdeology,    "includeIdeology",  true);
            Scribe_Values.Look(ref includeRimLighting, "includeRimLighting", true);
            Scribe_Values.Look(ref useLLMPrompt,       "useLLMPrompt",     false);
            Scribe_Values.Look(ref llmApiKey,          "llmApiKey",        "");
            Scribe_Collections.Look(ref activePortraits, "activePortraits", LookMode.Value, LookMode.Value);

            if (activePortraits == null)
                activePortraits = new Dictionary<string, string>();
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

        public class SavedPortrait
        {
            public string pngPath;
            public string txtPath;
            public Texture2D texture;
            public string prompt;
            public string styleName;
            public string timestamp;
        }

        private Pawn lastCachedPawn = null;
        private List<SavedPortrait> cachedSavedPortraits = new List<SavedPortrait>();

        private void RefreshPawnPortraitsCache(Pawn pawn)
        {
            // Destroy textures to avoid memory leaks
            foreach (var sp in cachedSavedPortraits)
            {
                if (sp.texture != null)
                {
                    UnityEngine.Object.Destroy(sp.texture);
                }
            }
            cachedSavedPortraits.Clear();

            if (pawn == null)
            {
                lastCachedPawn = null;
                return;
            }

            lastCachedPawn = pawn;
            string dir = CacheManager.GetPortraitSaveDirectory(pawn.LabelShortCap);
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.png");
                foreach (string file in files)
                {
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

                            string filename = Path.GetFileNameWithoutExtension(file);
                            string[] parts = filename.Split('_');
                            if (parts.Length >= 3)
                            {
                                sp.styleName = parts[1];
                                sp.timestamp = parts[2].Replace('-', ':');
                                if (parts.Length > 3)
                                {
                                    sp.timestamp += " " + parts[3].Replace('-', ':');
                                }
                            }
                            else
                            {
                                sp.styleName = "Unknown";
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

        private void DrawApiSettings(Rect inRect)
        {
            float viewHeight = 550f;
            if (useLLMPrompt) viewHeight += 60f;
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
            if (Widgets.ButtonText(btnKorean,  "🎨 Realistic (Korean)"))  portraitStyle = PortraitStyle.Realistic_Korean;
            GUI.color = Color.white;

            if (portraitStyle == PortraitStyle.Realistic_Western) GUI.color = new Color(0.5f, 0.9f, 1f);
            if (Widgets.ButtonText(btnWestern, "⚔ Realistic (Western)")) portraitStyle = PortraitStyle.Realistic_Western;
            GUI.color = Color.white;

            if (portraitStyle == PortraitStyle.DotPixel)          GUI.color = new Color(0.5f, 0.9f, 1f);
            if (Widgets.ButtonText(btnPixel,   "🟦 Pixel / Dot"))          portraitStyle = PortraitStyle.DotPixel;
            GUI.color = Color.white;

            listing.Gap(6f);
            string styleDesc =
                portraitStyle == PortraitStyle.Realistic_Korean  ? "Semi-realistic Korean manhwa / JRPG anime style — painterly brushwork, warm/cool contrast." :
                portraitStyle == PortraitStyle.Realistic_Western ? "Western dark fantasy oil painting — Path of Exile / Pillars of Eternity aesthetic." :
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

            bool isFree  = (backendType == BackendType.Pollinations);
            bool isPaid  = (backendType == BackendType.GoogleImagen);
            bool isLocal = (backendType == BackendType.LocalA1111);

            // Three-button row
            Rect modeRow  = listing.GetRect(36f);
            float thirdW  = modeRow.width / 3f;
            Rect btnFree  = new Rect(modeRow.x,               modeRow.y, thirdW - 4f, 36f);
            Rect btnPaid  = new Rect(modeRow.x + thirdW,      modeRow.y, thirdW - 4f, 36f);
            Rect btnLocal = new Rect(modeRow.x + thirdW * 2f, modeRow.y, thirdW - 4f, 36f);

            if (isFree) GUI.color = new Color(0.3f, 0.85f, 0.4f);
            if (Widgets.ButtonText(btnFree, "🆓  Free — Pollinations"))
            {
                backendType = BackendType.Pollinations;
                apiUrl      = "https://image.pollinations.ai";
                modelName   = "sana";
                apiKey      = "";
            }
            GUI.color = Color.white;

            if (isPaid) GUI.color = new Color(0.9f, 0.7f, 0.2f);
            if (Widgets.ButtonText(btnPaid, "💎  Paid — Imagen 4"))
            {
                backendType = BackendType.GoogleImagen;
                apiUrl      = "https://generativelanguage.googleapis.com";
                modelName   = "imagen-4.0-fast-generate-001";
            }
            GUI.color = Color.white;

            if (isLocal) GUI.color = new Color(0.5f, 0.7f, 1f);
            if (Widgets.ButtonText(btnLocal, "🖥  Local GPU"))
            {
                backendType = BackendType.LocalA1111;
                apiUrl      = "http://127.0.0.1:7860";
                modelName   = ""; // empty = use whatever model the local server has loaded
                apiKey      = "";
            }
            GUI.color = Color.white;

            listing.Gap(10f);

            if (isFree)
            {
                Rect infoRect = listing.GetRect(44f);
                Widgets.DrawBoxSolid(infoRect, new Color(0.1f, 0.2f, 0.1f, 0.6f));
                Text.Font = GameFont.Tiny;
                Widgets.Label(infoRect.ContractedBy(6f),
                    "No API key needed. Model: Sana (via Pollinations). ~50s per generation.\n" +
                    "Outputs JPEG — portraits have an opaque background, not true transparency.");
                Text.Font = GameFont.Small;
            }
            else if (isPaid)
            {
                Rect infoRect = listing.GetRect(28f);
                Widgets.DrawBoxSolid(infoRect, new Color(0.2f, 0.15f, 0.05f, 0.6f));
                Text.Font = GameFont.Tiny;
                Widgets.Label(infoRect.ContractedBy(6f),
                    "$0.02 / image (Fast). ~3s generation. True transparent PNG output. Best quality.");
                Text.Font = GameFont.Small;

                listing.Gap(8f);
                listing.Label("Google AI Studio API Key");
                apiKey = listing.TextEntry(apiKey);
                listing.Gap(2f);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(listing.GetRect(20f), "  Get a free key at: aistudio.google.com/app/apikey");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            else if (isLocal)
            {
                Rect infoRect = listing.GetRect(60f);
                Widgets.DrawBoxSolid(infoRect, new Color(0.1f, 0.15f, 0.25f, 0.6f));
                Text.Font = GameFont.Tiny;
                Widgets.Label(infoRect.ContractedBy(6f),
                    "Free, runs on your own GPU. Requires a local server (AUTOMATIC1111, Forge, SD.Next,\n" +
                    "or ComfyUI A1111-compat) on port 7860. ~2–5s per portrait on a modern card.\n" +
                    "Start your server BEFORE generating. URL configurable in Advanced.");
                Text.Font = GameFont.Small;

                listing.Gap(8f);
                listing.Label("Server URL");
                apiUrl = listing.TextEntry(apiUrl);
                listing.Gap(2f);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(listing.GetRect(20f), "  Default: http://127.0.0.1:7860 (A1111 / Forge default)");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            else
            {
                listing.Gap(4f);
                listing.Label("API Key");
                apiKey = listing.TextEntry(apiKey);
            }

            listing.Gap(12f);
            listing.GapLine();
            listing.Gap(6f);

            // ── PORTRAIT DETAILS SETTINGS ─────────────────────────────────────────
            listing.Label("Portrait Details Settings");
            listing.Gap(4f);
            listing.CheckboxLabeled("Force Open Eyes", ref forceOpenEyes, "Always command the AI to keep the pawn's eyes open (prevents sleeping/contemplative closed-eye generation by default).");
            listing.CheckboxLabeled("Allow Cute & Skill Props", ref allowCuteProps, "Incorporate trait-based cute items (lollipops, joint, beer) and skill-based items (potted plants, book, medical kits) in hands.");
            listing.CheckboxLabeled("Include Ideology details", ref includeIdeology, "Include pawn's ideology role (e.g. Moral Guide) and follower description/iconography.");
            listing.CheckboxLabeled("Include Ideology Rim Lighting", ref includeRimLighting, "Separate the character silhouette from the background using a rim light styled with their favorite/ideoligion color.");
            listing.Gap(8f);

            listing.GapLine();
            listing.Gap(6f);

            // ── AI PROMPT GENERATION ──────────────────────────────────────────────
            listing.Label("AI Prompt Generation");
            listing.Gap(4f);
            listing.CheckboxLabeled(
                "Use Gemini Flash for prompt generation",
                ref useLLMPrompt,
                "Sends pawn metadata to Google Gemini Flash, which writes a richer, " +
                "more coherent image prompt instead of the built-in template compiler. " +
                "Resolves contradictions (e.g. addiction props vs. degraded-face descriptors) automatically. " +
                "Requires a Google AI Studio API key (free). Falls back to the template if the call fails.");

            if (useLLMPrompt)
            {
                listing.Gap(6f);

                // If using Imagen the same key works for Gemini Flash — show a green note.
                bool canReuseImagenKey = (backendType == BackendType.GoogleImagen && !string.IsNullOrEmpty(apiKey));
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
                    llmApiKey = listing.TextEntry(llmApiKey);
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

            // ── ADVANCED ──────────────────────────────────────────────────────────
            Rect advHeaderRect = listing.GetRect(26f);
            string advLabel = (showAdvanced ? "▼" : "▶") + "  Advanced Settings";
            if (Widgets.ButtonText(advHeaderRect, advLabel))
                showAdvanced = !showAdvanced;

            if (showAdvanced)
            {
                listing.Gap(6f);
                listing.Label("API URL");
                apiUrl = listing.TextEntry(apiUrl);

                listing.Gap(6f);
                listing.Label("Model Name");
                modelName = listing.TextEntry(modelName);

                listing.Gap(6f);
                listing.Label("Extra Style Prompt (appended to every generation)");
                baseStylePrompt = listing.TextEntry(baseStylePrompt, 2);

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
                // Check if file count changed on disk to auto-refresh
                string dir = CacheManager.GetPortraitSaveDirectory(selectedPawn.LabelShortCap);
                int diskCount = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Length : 0;
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

            // Sub Header Action Buttons (Create New, Use Dynamic)
            Rect subHeaderRect = new Rect(gridAreaRect.x, gridAreaRect.y + 38f, gridAreaRect.width, 35f);
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
            }
            if (Widgets.ButtonText(createBtnRect, btnLabel))
            {
                AIPortraitsManager.TriggerNewPortraitWithContinuity(selectedPawn);
            }
            GUI.enabled = true;

            // Highlight dynamic button if NO active portrait is locked
            string activeKey = AIPortraitsManager.GetActiveKey(selectedPawn);
            string lockedP;
            bool hasLocked = activePortraits.TryGetValue(activeKey, out lockedP) && !string.IsNullOrEmpty(lockedP) && File.Exists(lockedP);
            if (!hasLocked)
            {
                GUI.color = new Color(0.5f, 0.9f, 1f);
            }
            if (Widgets.ButtonText(dynamicBtnRect, "Use Dynamic Portrait"))
            {
                if (activePortraits.ContainsKey(activeKey))
                {
                    activePortraits.Remove(activeKey);
                    AIPortraitsManager.ClearPawnActiveTextureCache(selectedPawn);
                    AIPortraitsMod.Instance.WriteSettings();
                }
            }
            GUI.color = Color.white;

            // Grid of Images Scrollview
            Rect gridScrollRect = new Rect(gridAreaRect.x, gridAreaRect.y + 80f, gridAreaRect.width, gridAreaRect.height - 80f);

            if (cachedSavedPortraits.Count == 0)
            {
                Widgets.Label(gridScrollRect, "No saved portraits found. Click 'Create New' to generate a portrait for " + selectedPawn.LabelShortCap + ".");
            }
            else
            {
                float cardW = 120f;
                float cardH = 180f;
                float margin = 8f;
                int cols = Mathf.Max(1, Mathf.FloorToInt((gridScrollRect.width - 16f) / (cardW + margin)));
                int rows = Mathf.CeilToInt((float)cachedSavedPortraits.Count / cols);

                Rect viewRectRight = new Rect(0f, 0f, gridScrollRect.width - 16f, rows * (cardH + margin) + 10f);

                Widgets.BeginScrollView(gridScrollRect, ref rightScrollPosition, viewRectRight);
                for (int i = 0; i < cachedSavedPortraits.Count; i++)
                {
                    SavedPortrait sp = cachedSavedPortraits[i];
                    int r = i / cols;
                    int c = i % cols;

                    float x = c * (cardW + margin);
                    float y = r * (cardH + margin);

                    Rect cardRect = new Rect(x, y, cardW, cardH);

                    // Check if this image is the active/locked one
                    string activePath;
                    bool isActive = activePortraits.TryGetValue(activeKey, out activePath) && activePath == sp.pngPath;

                    // Draw background and highlight border if active
                    if (isActive)
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

                    // Tooltip and Click Action for Prompt
                    TooltipHandler.TipRegion(imgRect, "Click image to copy prompt to clipboard.\n\nPrompt:\n" + sp.prompt);

                    if (Widgets.ButtonInvisible(imgRect))
                    {
                        GUIUtility.systemCopyBuffer = sp.prompt;
                        Messages.Message("Prompt copied to clipboard!", MessageTypeDefOf.TaskCompletion, false);
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
                            activePortraits[activeKey] = sp.pngPath;
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
                                        activePortraits.Remove(activeKey);
                                        AIPortraitsManager.ClearPawnActiveTextureCache(selectedPawn);
                                        AIPortraitsMod.Instance.WriteSettings();
                                    }

                                    if (File.Exists(sp.pngPath)) File.Delete(sp.pngPath);
                                    if (!string.IsNullOrEmpty(sp.txtPath) && File.Exists(sp.txtPath)) File.Delete(sp.txtPath);

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
                Widgets.EndScrollView();
            }

            // ── RIGHT COLUMN: PAWN VIBE & PROMPT PREVIEW ──
            Widgets.DrawMenuSection(vibeAreaRect);
            Rect vibeContentRect = vibeAreaRect.ContractedBy(8f);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(vibeContentRect.x, vibeContentRect.y, vibeContentRect.width, 30f), "Pawn Summary & Prompt");
            Text.Font = GameFont.Small;

            Rect vibeScrollArea = new Rect(vibeContentRect.x, vibeContentRect.y + 35f, vibeContentRect.width, vibeContentRect.height - 35f);

            PawnState state = PawnStateExtractor.ExtractState(selectedPawn);
            if (state != null)
            {
                float viewWidth = vibeScrollArea.width - 16f;
                string compiledPrompt;
                if (useLLMPrompt)
                {
                    compiledPrompt = "🤖 Gemini 3.1 Flash-Lite Prompt Generation is active.\n" +
                                     "At generation time, the pawn data sheet below is sent to the LLM to write a custom prompt.\n\n" +
                                     "--- SYSTEM INSTRUCTION (GEMINI PROMPT) ---\n" +
                                     PromptCompiler.GetLLMSystemPrompt(portraitStyle) +
                                     "\n\n--- PAWN DATA SHEET (LLM INPUT) ---\n" +
                                     PromptCompiler.CompilePawnStateDescription(state, this);
                }
                else
                {
                    compiledPrompt = PromptCompiler.CompilePositivePrompt(state, this);
                }
                float promptHeight = Text.CalcHeight(compiledPrompt, viewWidth - 8f);

                string identityText = "• Gender: " + state.gender + "\n• Age: " + state.bioAge + "\n• Body Type: " + state.bodyType + "\n• Xenotype: " + state.xenotype;
                if (!string.IsNullOrEmpty(state.xenotypeName)) identityText += " (" + state.xenotypeName + ")";
                if (state.isHemogenic) identityText += "\n• Bloodfeeder / Sanguophage";
                float idHeight = Text.CalcHeight(identityText, viewWidth - 5f);

                string backstoryText = "• Childhood: " + (state.childhoodTitle ?? "None") + "\n• Adulthood: " + (state.adulthoodTitle ?? "None");
                float bsHeight = Text.CalcHeight(backstoryText, viewWidth - 5f);

                float traitsHeight = 22f + (state.traits.Count > 0 ? state.traits.Count * 20f : 20f);
                float apparelHeight = 44f + (state.apparel.Count > 0 ? state.apparel.Count * 20f : 20f);

                int healthItemsCount = state.implants.Count + state.missingParts.Count + state.headInjuries.Count + state.bodyInjuries.Count;
                if (state.isSick) healthItemsCount++;
                if (state.isBloodloss) healthItemsCount++;
                if (state.isBurning) healthItemsCount++;
                float healthHeight = 22f + (healthItemsCount > 0 ? healthItemsCount * 20f : 20f);

                float contentHeight = idHeight + bsHeight + traitsHeight + apparelHeight + healthHeight + promptHeight + 180f;

                Rect viewRectVibe = new Rect(0f, 0f, viewWidth, contentHeight);
                Widgets.BeginScrollView(vibeScrollArea, ref vibeScrollPosition, viewRectVibe);

                float curY = 0f;

                // 1. Identity
                Widgets.Label(new Rect(0f, curY, viewWidth, 22f), "<b>Identity:</b>");
                curY += 22f;
                Widgets.Label(new Rect(5f, curY, viewWidth - 5f, idHeight), identityText);
                curY += idHeight + 10f;

                // 2. Backstory
                Widgets.Label(new Rect(0f, curY, viewWidth, 22f), "<b>Backstory:</b>");
                curY += 22f;
                Widgets.Label(new Rect(5f, curY, viewWidth - 5f, bsHeight), backstoryText);
                curY += bsHeight + 10f;

                // 3. Traits & Mood
                Widgets.Label(new Rect(0f, curY, viewWidth, 22f), "<b>Traits & Mood (Mood: " + (state.moodLevel * 100f).ToString("F0") + "%):</b>");
                curY += 22f;
                if (state.traits.Count > 0)
                {
                    for (int i = 0; i < state.traits.Count; i++)
                    {
                        Widgets.Label(new Rect(5f, curY, viewWidth - 5f, 20f), "• " + state.traits[i]);
                        curY += 20f;
                    }
                }
                else
                {
                    Widgets.Label(new Rect(5f, curY, viewWidth - 5f, 20f), "• No special traits");
                    curY += 20f;
                }
                curY += 10f;

                // 4. Apparel & Equipment
                string weaponStr = string.IsNullOrEmpty(state.primaryWeapon) ? "Unarmed" : state.primaryWeapon + " (" + state.weaponType + ")";
                Widgets.Label(new Rect(0f, curY, viewWidth, 22f), "<b>Equipment:</b> " + weaponStr);
                curY += 24f;
                Widgets.Label(new Rect(0f, curY, viewWidth, 22f), "<b>Apparel:</b>");
                curY += 22f;
                if (state.apparel.Count > 0)
                {
                    for (int i = 0; i < state.apparel.Count; i++)
                    {
                        Widgets.Label(new Rect(5f, curY, viewWidth - 5f, 20f), "• " + state.apparel[i]);
                        curY += 20f;
                    }
                }
                else
                {
                    Widgets.Label(new Rect(5f, curY, viewWidth - 5f, 20f), "• Nude / No apparel");
                    curY += 20f;
                }
                curY += 10f;

                // 5. Health & Body
                Widgets.Label(new Rect(0f, curY, viewWidth, 22f), "<b>Health & Body:</b>");
                curY += 22f;
                List<string> healthLines = new List<string>();
                for (int i = 0; i < state.implants.Count; i++) healthLines.Add("• Implant: " + state.implants[i]);
                for (int i = 0; i < state.missingParts.Count; i++) healthLines.Add("• Missing: " + state.missingParts[i]);
                for (int i = 0; i < state.headInjuries.Count; i++) healthLines.Add("• Head: " + state.headInjuries[i]);
                for (int i = 0; i < state.bodyInjuries.Count; i++) healthLines.Add("• Body: " + state.bodyInjuries[i]);
                if (state.isSick) healthLines.Add("• Sick/Diseased");
                if (state.isBloodloss) healthLines.Add("• Experiencing blood loss");
                if (state.isBurning) healthLines.Add("• Wounded by fire/burning");

                if (healthLines.Count > 0)
                {
                    for (int i = 0; i < healthLines.Count; i++)
                    {
                        Widgets.Label(new Rect(5f, curY, viewWidth - 5f, 20f), healthLines[i]);
                        curY += 20f;
                    }
                }
                else
                {
                    Widgets.Label(new Rect(5f, curY, viewWidth - 5f, 20f), "• Healthy (No active conditions)");
                    curY += 20f;
                }
                curY += 15f;

                // 6. Compiled Prompt
                Widgets.Label(new Rect(0f, curY, viewWidth, 22f), "<b>Compiled AI Prompt:</b>");
                curY += 24f;

                Rect copyBtnRect = new Rect(0f, curY, 120f, 24f);
                if (Widgets.ButtonText(copyBtnRect, "Copy Prompt"))
                {
                    GUIUtility.systemCopyBuffer = compiledPrompt;
                    Messages.Message("Compiled prompt copied to clipboard!", MessageTypeDefOf.TaskCompletion, false);
                }
                curY += 28f;

                Rect promptBoxRect = new Rect(0f, curY, viewWidth, promptHeight + 20f);
                Widgets.TextArea(promptBoxRect, compiledPrompt, true);

                curY += promptBoxRect.height + 20f;

                Widgets.EndScrollView();
            }
            else
            {
                Widgets.Label(vibeScrollArea, "No pawn selected or unable to retrieve state.");
            }
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

            string structuredDesc = PromptCompiler.CompilePawnStateDescription(state, this);
            string compiledPrompt = PromptCompiler.CompilePositivePrompt(state, this, null);
            string llmSystem      = useLLMPrompt ? PromptCompiler.GetLLMSystemPrompt(portraitStyle) : null;

            float viewW = content.width - 18f;
            float descH    = Text.CalcHeight(structuredDesc, viewW - 12f);
            float promptH  = Text.CalcHeight(compiledPrompt, viewW - 12f);
            float llmH     = llmSystem != null ? Text.CalcHeight(llmSystem, viewW - 12f) : 0f;
            float totalH   = descH + promptH + llmH + 280f;

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
