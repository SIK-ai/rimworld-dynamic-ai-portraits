using UnityEngine;
using Verse;
using HarmonyLib;
using RimWorld;

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
            Pawn pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null || pawn.Destroyed) return;

            IInspectPane activePane = __instance as IInspectPane;
            if (activePane == null) return;

            // Only draw when no inspect tab is open.
            if (activePane.OpenTabType != null) return;

            // Only draw when there is an actual portrait to show — no placeholder box.
            GenerationStatus status; string error;
            Texture2D portrait = AIPortraitsManager.GetPortraitTexture(pawn, out status, out error);
            if (portrait == null) return;

            Rect paneRect = __instance.windowRect;

            // Cap the overlay so it doesn't dominate the screen.
            float side = Mathf.Min(paneRect.width, 260f);

            // Leave clearance for the inspect-pane tab strip (Log, Health, etc.) which
            // sits in the space just above paneRect.y.
            const float TabStripClearance = 36f;

            float x = paneRect.x + (paneRect.width - side) * 0.5f;
            float y = paneRect.y - side - TabStripClearance;
            Rect portraitRect = new Rect(x, y, side, side);

            GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);

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

            DrawRefreshButton(refreshRect, pawn);
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

            TooltipHandler.TipRegion(rect,
                "Refresh portrait — re-reads pawn state and generates a new image.\n" +
                "Costs $0.02 on the paid (Imagen) backend, free on Pollinations.");

            if (Widgets.ButtonInvisible(rect))
            {
                AIPortraitsManager.TriggerNewPortraitWithContinuity(pawn);
                Messages.Message("Regenerating portrait for " + pawn.LabelShortCap + "...",
                                 MessageTypeDefOf.NeutralEvent, false);
            }
        }
    }

    // NOTE: The PortraitsCache.Get patch was removed. It was blitting our square
    // 1:1 AI portrait into RimWorld's vertical pawn-render textures (colonist bar,
    // Bio tab, etc.), which stretched faces and produced distorted portraits in
    // every UI element that uses PortraitsCache. The AI portrait now only appears
    // as the clean overlay above the inspect pane.
}
