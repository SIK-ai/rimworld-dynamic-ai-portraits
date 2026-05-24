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

            // Cap the overlay so it doesn't dominate the screen. Use the inspect pane
            // width but clamp to a sane portrait size.
            float side = Mathf.Min(paneRect.width, 260f);

            // Leave clearance for the inspect-pane tab strip (Log, Health, etc.) which
            // sits in the space just above paneRect.y. Without this, the portrait's
            // bottom edge overlaps the tab buttons.
            const float TabStripClearance = 36f;

            // Center the portrait horizontally over the pane.
            float x = paneRect.x + (paneRect.width - side) * 0.5f;
            float y = paneRect.y - side - TabStripClearance;
            Rect portraitRect = new Rect(x, y, side, side);

            GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
        }
    }

    // NOTE: The PortraitsCache.Get patch was removed. It was blitting our square
    // 1:1 AI portrait into RimWorld's vertical pawn-render textures (colonist bar,
    // Bio tab, etc.), which stretched faces and produced distorted portraits in
    // every UI element that uses PortraitsCache. The AI portrait now only appears
    // as the clean overlay above the inspect pane.
}
