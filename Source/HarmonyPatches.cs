using UnityEngine;
using Verse;
using HarmonyLib;
using RimWorld;
using Verse.Sound;

namespace AIPortraits
{
    // ─────────────────────────────────────────────────────────────────────────────
    // PATCH — Draw portrait overlay above the inspect pane, ONLY when no tab is open
    // ─────────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(MainTabWindow_Inspect), "ExtraOnGUI")]
    public static class Patch_MainTabWindow_Inspect_ExtraOnGUI
    {
        // Overlay toolbar: collapsed to one ⊕ button by default; ⊕/▽ toggle this.
        private static bool overlayExpanded = false;

        private static string strMakingAlive;
        private static string strVeoError;
        private static string strClickToAnimate;
        private static string strPainting;
        private static string strError;
        private static string strNoPortraitClickToGenerate;


        [HarmonyPostfix]
        public static void Postfix(MainTabWindow_Inspect __instance)
        {
            if (strMakingAlive == null)
            {
                strMakingAlive = "AIPortraits_MakingAlive".Translate();
                strVeoError = "AIPortraits_VeoError".Translate();
                strClickToAnimate = "AIPortraits_ClickToAnimate".Translate();
                strPainting = "AIPortraits_Painting".Translate();
                strError = "AIPortraits_Error".Translate();
                strNoPortraitClickToGenerate = "AIPortraits_NoPortraitClickToGenerate".Translate();
            }
            // Centralised early-return guard: if any of these conditions fail, we both
            // stop any active video playback AND skip overlay drawing. Collapsed from
            // four near-identical early-return blocks.
            Pawn pawn = Find.Selector.SingleSelectedThing as Pawn;
            IInspectPane activePane = __instance as IInspectPane;
            bool shouldDraw =
                pawn != null &&
                !pawn.Destroyed &&
                activePane != null &&
                activePane.OpenTabType == null &&
                AIPortraitsManager.ShouldGenerateFor(pawn);

            if (!shouldDraw)
            {
                DebugLog.LogOnChange("draw_" + (pawn != null ? pawn.ThingID : "none"), "DRAW",
                    "hidden (shouldDraw=false): pawnNull=" + (pawn == null) +
                    " tabOpen=" + (activePane != null && activePane.OpenTabType != null));
                VideoPlaybackManager.StopPlayback();
                return;
            }

            try
            {

            GenerationStatus status; string error;
            Texture2D portrait = AIPortraitsManager.GetPortraitTexture(pawn, out status, out error);

            Rect paneRect = __instance.windowRect;

            float side = 260f;
            float offsetX = 0f;
            float offsetY = 0f;
            if (AIPortraitsMod.settings != null)
            {
                side = AIPortraitsMod.settings.portraitScale;
                offsetX = AIPortraitsMod.settings.portraitOffsetX;
                offsetY = AIPortraitsMod.settings.portraitOffsetY;
            }

            // Size the overlay to the framing's real aspect instead of forcing a square:
            // bodyshot is a tall 2:3 full-body, special is a wide 3:2 scene. 'side' (the
            // Display Size slider) is the reference dimension; the wide/tall shots are
            // enlarged so the body isn't squeezed and the special scene isn't a sliver.
            string dispFraming = AIPortraitsManager.GetActiveFraming(pawn);
            float pw, ph;
            if (dispFraming == "special")
            {
                pw = side * 1.7f;          // wide 3:2, enlarged (was too small)
                ph = pw * 2f / 3f;
            }
            else if (dispFraming == "bodyshot")
            {
                ph = side * 1.5f;          // tall 2:3 full-body
                pw = ph * 2f / 3f;
            }
            else
            {
                pw = side;                 // portrait 1:1
                ph = side;
            }

            // Leave clearance for the inspect-pane tab strip (Log, Health, etc.) which
            // sits in the space just above paneRect.y.
            const float TabStripClearance = 36f;

            float x = paneRect.x + (paneRect.width - pw) * 0.5f + offsetX;
            float y = paneRect.y - ph - TabStripClearance + offsetY;
            Rect portraitRect = new Rect(x, y, pw, ph);

            // Per-framing live toggle: this framing renders video only if [V] was enabled
            // for THIS framing (portrait/bodyshot/special each track their own still-vs-live
            // state), so the live state never carries across when switching shots or pawns.
            bool videoEnabled = AIPortraitsManager.IsVideoMode(pawn);
            DebugLog.LogOnChange("draw_" + pawn.ThingID, "DRAW",
                "pawn=" + pawn.LabelShortCap + " framing=" + dispFraming + " live=" + videoEnabled +
                " portraitNull=" + (portrait == null) + " status=" + status);

            if (videoEnabled)
            {
                string diskKey = AIPortraitsManager.GetActiveKey(pawn);
                string videoPath = System.IO.Path.Combine(CacheManager.GetCacheDirectory(), diskKey + ".mp4");

                if (System.IO.File.Exists(videoPath))
                {
                    // Background-remove portrait/bodyshot clips once (offline u2netp). "special"
                    // is skipped inside EnsureMatted so its scenic background is kept. Until the
                    // matte finishes, StartPlayback falls back to the original mp4.
                    VideoMatteService.EnsureMatted(videoPath, dispFraming);
                    VideoPlaybackManager.StartPlayback(pawn.ThingID, videoPath, dispFraming);
                    Texture videoTex = VideoPlaybackManager.GetActiveTexture();
                    if (videoTex != null)
                    {
                        GUI.DrawTexture(portraitRect, videoTex, ScaleMode.ScaleAndCrop);
                    }
                    else if (portrait != null)
                    {
                        GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleAndCrop);
                    }
                }
                else
                {
                    GenerationStatus vStatus; string vError;
                    AIPortraitsManager.GetVideoStatus(pawn, out vStatus, out vError);

                    // Video MODE is on for this framing but no clip exists yet. Do NOT
                    // auto-generate \u2014 show the static portrait and wait for the user to
                    // click the refresh (\u21bb) button to animate it.
                    if (portrait != null)
                    {
                        GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleAndCrop);
                    }
                    else
                    {
                        Widgets.DrawBoxSolid(portraitRect, new Color(0.06f, 0.06f, 0.06f, 0.8f));
                        GUI.color = new Color(1f, 1f, 1f, 0.15f);
                        Widgets.DrawBox(portraitRect, 1);
                        GUI.color = Color.white;
                    }

                    if (vStatus == GenerationStatus.Generating)
                    {
                        Widgets.DrawBoxSolid(portraitRect, new Color(0f, 0f, 0f, 0.4f));
                        Text.Anchor = TextAnchor.MiddleCenter;
                        GUI.color = new Color(0.6f, 0.85f, 1f);
                        Widgets.Label(portraitRect, strMakingAlive);
                        GUI.color = Color.white;
                        Text.Anchor = TextAnchor.UpperLeft;
                    }
                    else if (vStatus == GenerationStatus.Error)
                    {
                        Widgets.DrawBoxSolid(portraitRect, new Color(0f, 0f, 0f, 0.4f));
                        Text.Anchor = TextAnchor.MiddleCenter;
                        GUI.color = Color.red;
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(portraitRect, "\u2716 " + (vError ?? strVeoError));
                        GUI.color = Color.white;
                        Text.Anchor = TextAnchor.UpperLeft;
                        Text.Font = GameFont.Small;
                    }
                    else
                    {
                        // Idle, no clip yet \u2014 subtle prompt to generate it via refresh.
                        Text.Anchor = TextAnchor.LowerCenter;
                        Text.Font = GameFont.Tiny;
                        GUI.color = new Color(0.6f, 0.85f, 1f, 0.9f);
                        Widgets.Label(portraitRect, strClickToAnimate);
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;
                        Text.Anchor = TextAnchor.UpperLeft;
                    }
                }
            }
            else
            {
                if (portrait != null)
                {
                    GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                }
                else
                {
                    Widgets.DrawBoxSolid(portraitRect, new Color(0.06f, 0.06f, 0.06f, 0.8f));
                    GUI.color = new Color(1f, 1f, 1f, 0.15f);
                    Widgets.DrawBox(portraitRect, 1);
                    GUI.color = Color.white;
                    
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Small;
                    if (status == GenerationStatus.Generating)
                    {
                        GUI.color = new Color(0.6f, 0.85f, 1f);
                        Widgets.Label(portraitRect, strPainting);
                    }
                    else if (status == GenerationStatus.Error)
                    {
                        GUI.color = Color.red;
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(portraitRect, "\u2716 " + (error ?? strError));
                    }
                    else
                    {
                        GUI.color = new Color(0.45f, 0.45f, 0.45f);
                        Widgets.Label(portraitRect, strNoPortraitClickToGenerate);
                    }
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                }
                VideoPlaybackManager.StopPlayback();
            }

            // ── Overlay controls: collapsed to a single ⊕ button by default; click it to
            // expand the full toolbar (P · B · S · V · ↻ · ⚙ Settings · ▽ Collapse). ──
            const float BtnSize = 28f;
            const float BtnMargin = 6f;
            float by = portraitRect.yMax - BtnSize - BtnMargin;     // button row Y
            float bx = portraitRect.xMax - BtnSize - BtnMargin;     // rightmost button X

            if (!overlayExpanded)
            {
                Rect addRect = new Rect(bx, by, BtnSize, BtnSize);
                if (DrawIconButton(addRect, "⊕", "Show portrait controls"))
                {
                    overlayExpanded = true;
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                }
            }
            else
            {
                string currentFraming = "portrait";
                string f;
                if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnFraming != null &&
                    AIPortraitsMod.settings.pawnFraming.TryGetValue(pawn.ThingID, out f))
                {
                    currentFraming = f;
                }

                // Right-aligned row of 7 buttons (▽ collapse sits where the ⊕ was). Scale the
                // button size/gap down to fit narrow portraits (Display Size can be as small as
                // 100px) so the row never overflows off the left edge of the portrait.
                const int RowCount = 7;
                float rowGap = 4f;
                float rowAvail = portraitRect.width - BtnMargin * 2f;
                float ebtn = (rowAvail - (RowCount - 1) * rowGap) / RowCount;
                if (ebtn > BtnSize) ebtn = BtnSize;
                if (ebtn < 12f)
                {
                    ebtn = 12f;
                    rowGap = (rowAvail - RowCount * ebtn) / (RowCount - 1);
                    if (rowGap < 1f) rowGap = 1f;
                }
                float eby = portraitRect.yMax - ebtn - BtnMargin;
                float ebx = portraitRect.xMax - ebtn - BtnMargin;
                float estep = ebtn + rowGap;

                Rect collapseRect = new Rect(ebx,              eby, ebtn, ebtn);
                Rect setRect      = new Rect(ebx - estep,      eby, ebtn, ebtn);
                Rect refreshRect  = new Rect(ebx - estep * 2f, eby, ebtn, ebtn);
                Rect veoRect      = new Rect(ebx - estep * 3f, eby, ebtn, ebtn);
                Rect specRect     = new Rect(ebx - estep * 4f, eby, ebtn, ebtn);
                Rect bodyRect     = new Rect(ebx - estep * 5f, eby, ebtn, ebtn);
                Rect portRect     = new Rect(ebx - estep * 6f, eby, ebtn, ebtn);

                DrawFramingButton(portRect, "P", "portrait", currentFraming, pawn, "Set framing style to standard bust-up portrait.");
                DrawFramingButton(bodyRect, "B", "bodyshot", currentFraming, pawn, "Set framing style to full-length bodyshot.");
                DrawFramingButton(specRect, "S", "special", currentFraming, pawn, "Set framing style to special selfie / thematic scene.");
                DrawVeoButton(veoRect, pawn);
                DrawRefreshButton(refreshRect, pawn);
                DrawSettingsButton(setRect, pawn);

                if (DrawIconButton(collapseRect, "▽", "Collapse controls"))
                {
                    overlayExpanded = false;
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                }
            }

            }
            catch (System.Exception ex)
            {
                // An unhandled exception in this IMGUI postfix can blank the whole overlay
                // (and spam the log). Catch it, record it for backtracking, and keep going.
                DebugLog.Log("DRAW", "EXCEPTION pawn=" + (pawn != null ? pawn.LabelShortCap : "?") + ": " + ex.Message);
                Log.Warning("[Dynamic AI Portraits] overlay draw exception: " + ex);
            }
        }

        private static void DrawFramingButton(Rect rect, string text, string framingName, string currentFraming, Pawn pawn, string tooltip)
        {
            bool active = (currentFraming == framingName);
            bool hovered = Mouse.IsOver(rect);

            // Highlight background if active
            if (active)
            {
                GUI.color = hovered ? new Color(0.15f, 0.55f, 0.85f, 0.95f) : new Color(0.1f, 0.4f, 0.7f, 0.85f);
            }
            else
            {
                GUI.color = hovered ? new Color(0.35f, 0.35f, 0.35f, 0.95f) : new Color(0f, 0f, 0f, 0.65f);
            }

            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            // Text label
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;

            TooltipHandler.TipRegion(rect, tooltip);

            if (Widgets.ButtonInvisible(rect))
            {
                if (AIPortraitsMod.settings != null)
                {
                    if (AIPortraitsMod.settings.pawnFraming == null) AIPortraitsMod.settings.pawnFraming = new System.Collections.Generic.Dictionary<string, string>();
                    AIPortraitsMod.settings.pawnFraming[pawn.ThingID] = framingName;
                    AIPortraitsMod.Instance.WriteSettings();
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                    VideoPlaybackManager.StopPlayback();
                }
            }
        }

        private static void DrawRefreshButton(Rect rect, Pawn pawn)
        {
            // Dim hover state — Mouse.IsOver brightens it
            bool hovered = Mouse.IsOver(rect);

            // Semi-transparent dark backdrop so the icon is readable over any portrait color
            GUI.color = hovered ? new Color(0.15f, 0.45f, 0.65f, 0.95f) : new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            // The ↻ symbol
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(rect, "↻");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            bool videoActive = AIPortraitsManager.IsVideoMode(pawn);

            string tip = videoActive ? "Generate / regenerate video for this framing" : "Refresh portrait using selected framing";
            TooltipHandler.TipRegion(rect, tip);

            if (Widgets.ButtonInvisible(rect))
            {
                if (videoActive)
                {
                    // Video mode: regenerate ONLY the video, reusing the existing portrait
                    // image. If a video is already in flight, surface that instead of firing
                    // a second Veo request.
                    GenerationStatus vStat; string vErr;
                    AIPortraitsManager.GetVideoStatus(pawn, out vStat, out vErr);
                    if (vStat == GenerationStatus.Generating)
                    {
                        Messages.Message(pawn.LabelShortCap + "'s video is already generating...",
                                         MessageTypeDefOf.RejectInput, false);
                    }
                    else
                    {
                        byte[] imgBytes = AIPortraitsManager.GetActivePortraitBytes(pawn);
                        if (imgBytes != null && imgBytes.Length > 0)
                        {
                            AIPortraitsManager.TriggerVideoGeneration(pawn, imgBytes);
                            Messages.Message("Regenerating video for " + pawn.LabelShortCap + "...",
                                             MessageTypeDefOf.NeutralEvent, false);
                        }
                        else
                        {
                            Messages.Message("No portrait image available to animate.", MessageTypeDefOf.RejectInput, false);
                        }
                    }
                }
                else
                {
                    // Static mode: regenerate the portrait image. Skip if one is running.
                    if (AIPortraitsManager.GetStatus(pawn) == GenerationStatus.Generating)
                    {
                        Messages.Message(pawn.LabelShortCap + "'s portrait is already generating...",
                                         MessageTypeDefOf.RejectInput, false);
                    }
                    else
                    {
                        AIPortraitsManager.TriggerNewPortraitWithContinuity(pawn);
                        Messages.Message("Regenerating portrait for " + pawn.LabelShortCap + "...",
                                         MessageTypeDefOf.NeutralEvent, false);
                    }
                }
            }
        }

        // A small overlay button (dark backdrop + glyph). Returns true when clicked.
        private static bool DrawIconButton(Rect rect, string label, string tip)
        {
            bool hovered = Mouse.IsOver(rect);
            GUI.color = hovered ? new Color(0.15f, 0.45f, 0.65f, 0.95f) : new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, tip);
            return Widgets.ButtonInvisible(rect);
        }

        // ⚙ Settings — per-image (pawn + active framing) toggle menu. Each option shows its
        // current state (✓/✗) and flips it on click. Settings are unique to each image.
        private static void DrawSettingsButton(Rect rect, Pawn pawn)
        {
            // NOTE: the gear glyph "⚙" (U+2699) is NOT in RimWorld's UI font and rendered blank.
            // Use a menu/list glyph from the Geometric Shapes block (same block as the working
            // "▽" collapse arrow), which both renders and reads as "options menu".
            if (!DrawIconButton(rect, "▤", "Per-image settings for this shot (helmet / gear ref / reference portrait)")) return;
            string framing = AIPortraitsManager.GetActiveFraming(pawn);
            bool helm = AIPortraitsManager.GetExcludeHelmet(pawn, framing);
            bool gear = AIPortraitsManager.GetGearRef(pawn, framing);
            bool refp = AIPortraitsManager.GetRefPortrait(pawn, framing);
            System.Collections.Generic.List<FloatMenuOption> opts = new System.Collections.Generic.List<FloatMenuOption>();
            opts.Add(new FloatMenuOption((helm ? "✓ " : "✗ ") + "Exclude helmet / headgear", delegate {
                AIPortraitsManager.SetExcludeHelmet(pawn, framing, !helm); AIPortraitsMod.Instance.WriteSettings(); }));
            opts.Add(new FloatMenuOption((gear ? "✓ " : "✗ ") + "Use gear reference sheet", delegate {
                AIPortraitsManager.SetGearRef(pawn, framing, !gear); AIPortraitsMod.Instance.WriteSettings(); }));
            opts.Add(new FloatMenuOption((refp ? "✓ " : "✗ ") + "Reference portrait image (continuity)", delegate {
                AIPortraitsManager.SetRefPortrait(pawn, framing, !refp); AIPortraitsMod.Instance.WriteSettings(); }));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void DrawVeoButton(Rect rect, Pawn pawn)
        {
            bool active = AIPortraitsManager.IsVideoMode(pawn);

            bool hovered = Mouse.IsOver(rect);

            // Highlight background if active (running veo/video mode)
            if (active)
            {
                GUI.color = hovered ? new Color(0.2f, 0.65f, 0.45f, 0.95f) : new Color(0.15f, 0.5f, 0.35f, 0.85f);
            }
            else
            {
                GUI.color = hovered ? new Color(0.35f, 0.35f, 0.35f, 0.95f) : new Color(0f, 0f, 0f, 0.65f);
            }

            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            // Display "V"
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(rect, "V");
            Text.Anchor = TextAnchor.UpperLeft;

            TooltipHandler.TipRegion(rect, active ? "Toggle Video Mode OFF (Show static portrait)" : "Toggle Video Mode ON (Run Veo 3.1 Lite to animate portrait)");

            if (Widgets.ButtonInvisible(rect))
            {
                bool newState = !active;
                if (AIPortraitsMod.settings != null)
                {
                    // Per-framing toggle; enabling video MODE just switches THIS framing to
                    // "live" — it does NOT auto-generate. The user clicks ↻ to generate.
                    AIPortraitsManager.SetVideoMode(pawn, newState);
                    AIPortraitsMod.Instance.WriteSettings();
                    SoundDefOf.Click.PlayOneShotOnCamera(null);

                    if (!newState)
                    {
                        VideoPlaybackManager.StopPlayback();
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class Patch_Window_PostClose
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            if (__instance is MainTabWindow_Inspect)
            {
                VideoPlaybackManager.StopPlayback();
            }
        }
    }

    [HarmonyPatch(typeof(Verse.PawnRenderNode_Apparel), "AppendRequests")]
    public static class Patch_PawnRenderNode_Apparel_AppendRequests
    {
        // Apply only when this RimWorld build actually exposes the target method.
        // Returning false makes Harmony skip the patch cleanly instead of throwing
        // "Undefined target method" during PatchAll (which aborted mod init).
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(Verse.PawnRenderNode_Apparel), "AppendRequests") != null;
        }

        [HarmonyPrefix]
        public static bool Prefix(Verse.PawnRenderNode_Apparel __instance, RimWorld.Apparel ___apparel)
        {
            if (AsyncAIClient.isGeneratingRefPortrait && ___apparel != null)
            {
                if (PromptCompiler.IsHeadgear(___apparel))
                {
                    return false; // Skip appending draw requests!
                }
            }
            return true;
        }
    }
}
