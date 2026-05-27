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
        [HarmonyPostfix]
        public static void Postfix(MainTabWindow_Inspect __instance)
        {
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
                VideoPlaybackManager.StopPlayback();
                return;
            }

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

            // Leave clearance for the inspect-pane tab strip (Log, Health, etc.) which
            // sits in the space just above paneRect.y.
            const float TabStripClearance = 36f;

            float x = paneRect.x + (paneRect.width - side) * 0.5f + offsetX;
            float y = paneRect.y - side - TabStripClearance + offsetY;
            Rect portraitRect = new Rect(x, y, side, side);

            bool videoEnabled = false;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnVideoToggles != null)
            {
                AIPortraitsMod.settings.pawnVideoToggles.TryGetValue(pawn.ThingID, out videoEnabled);
            }

            if (videoEnabled)
            {
                string diskKey = AIPortraitsManager.GetActiveKey(pawn);
                string videoPath = System.IO.Path.Combine(CacheManager.GetCacheDirectory(), diskKey + ".mp4");

                if (System.IO.File.Exists(videoPath))
                {
                    // Background-remove portrait/bodyshot clips once (offline u2netp). "special"
                    // is skipped inside EnsureMatted so its scenic background is kept. Until the
                    // matte finishes, StartPlayback falls back to the original mp4.
                    VideoMatteService.EnsureMatted(videoPath, AIPortraitsManager.GetActiveFraming(pawn));
                    VideoPlaybackManager.StartPlayback(pawn.ThingID, videoPath);
                    RenderTexture videoTex = VideoPlaybackManager.GetActiveTexture();
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

                    if (vStatus == GenerationStatus.Idle)
                    {
                        byte[] imgBytes = AIPortraitsManager.GetActivePortraitBytes(pawn);
                        if (imgBytes != null && imgBytes.Length > 0)
                        {
                            AIPortraitsManager.TriggerVideoGeneration(pawn, imgBytes);
                        }
                    }

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
                        Widgets.Label(portraitRect, "Making alive...");
                        GUI.color = Color.white;
                        Text.Anchor = TextAnchor.UpperLeft;
                    }
                    else if (vStatus == GenerationStatus.Error)
                    {
                        Widgets.DrawBoxSolid(portraitRect, new Color(0f, 0f, 0f, 0.4f));
                        Text.Anchor = TextAnchor.MiddleCenter;
                        GUI.color = Color.red;
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(portraitRect, "\u2716 " + (vError ?? "Veo Error"));
                        GUI.color = Color.white;
                        Text.Anchor = TextAnchor.UpperLeft;
                        Text.Font = GameFont.Small;
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
                        Widgets.Label(portraitRect, "Painting...");
                    }
                    else if (status == GenerationStatus.Error)
                    {
                        GUI.color = Color.red;
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(portraitRect, "\u2716 " + (error ?? "Error"));
                    }
                    else
                    {
                        GUI.color = new Color(0.45f, 0.45f, 0.45f);
                        Widgets.Label(portraitRect, "No portrait\nClick \u21BB to generate");
                    }
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                }
                VideoPlaybackManager.StopPlayback();
            }

            // Refresh button overlaid at the bottom-right of the portrait. Clicking
            // re-extracts pawn state and triggers a fresh generation (clearing the
            // cached image first). This is the equivalent of "Create New Portrait"
            // in the Pawn Gallery, but accessible without opening settings.
            const float BtnSize = 28f;
            const float BtnMargin = 6f;
            Rect refreshRect = new Rect(
                portraitRect.xMax - BtnSize - BtnMargin,
                portraitRect.yMax - BtnSize - BtnMargin,
                BtnSize, BtnSize);

            Rect veoRect = new Rect(refreshRect.x - BtnSize - 4f, refreshRect.y, BtnSize, BtnSize);
            Rect specRect = new Rect(veoRect.x - BtnSize - 4f, refreshRect.y, BtnSize, BtnSize);
            Rect bodyRect = new Rect(specRect.x - BtnSize - 4f, refreshRect.y, BtnSize, BtnSize);
            Rect portRect = new Rect(bodyRect.x - BtnSize - 4f, refreshRect.y, BtnSize, BtnSize);

            string currentFraming = "portrait";
            string f;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnFraming != null &&
                AIPortraitsMod.settings.pawnFraming.TryGetValue(pawn.ThingID, out f))
            {
                currentFraming = f;
            }

            DrawFramingButton(portRect, "P", "portrait", currentFraming, pawn, "Set framing style to standard bust-up portrait.");
            DrawFramingButton(bodyRect, "B", "bodyshot", currentFraming, pawn, "Set framing style to full-length bodyshot.");
            DrawFramingButton(specRect, "S", "special", currentFraming, pawn, "Set framing style to special selfie / thematic scene.");

            DrawVeoButton(veoRect, pawn);
            DrawRefreshButton(refreshRect, pawn);
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

            bool videoActive = false;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnVideoToggles != null)
                AIPortraitsMod.settings.pawnVideoToggles.TryGetValue(pawn.ThingID, out videoActive);

            string tip = videoActive ? "Regenerate video for this pawn" : "Refresh portrait using selected framing";
            TooltipHandler.TipRegion(rect, tip);

            if (Widgets.ButtonInvisible(rect))
            {
                if (videoActive)
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
                else
                {
                    AIPortraitsManager.TriggerNewPortraitWithContinuity(pawn);
                    Messages.Message("Regenerating portrait for " + pawn.LabelShortCap + "...",
                                     MessageTypeDefOf.NeutralEvent, false);
                }
            }
        }

        private static void DrawVeoButton(Rect rect, Pawn pawn)
        {
            bool active = false;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnVideoToggles != null)
            {
                AIPortraitsMod.settings.pawnVideoToggles.TryGetValue(pawn.ThingID, out active);
            }

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
                    if (AIPortraitsMod.settings.pawnVideoToggles == null)
                        AIPortraitsMod.settings.pawnVideoToggles = new System.Collections.Generic.Dictionary<string, bool>();

                    AIPortraitsMod.settings.pawnVideoToggles[pawn.ThingID] = newState;
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
