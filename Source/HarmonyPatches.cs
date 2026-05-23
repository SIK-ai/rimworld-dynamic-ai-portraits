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

            // Respect the user's tab choice — only overlay when no tab is open.
            if (activePane.OpenTabType != null) return;

            Rect paneRect = __instance.windowRect;
            float portraitW = paneRect.width;
            float totalH = UI_AIPortraitCard.TotalHeight(portraitW);
            Rect portraitRect = new Rect(paneRect.x, paneRect.y - totalH, portraitW, totalH);

            UI_AIPortraitCard.DrawPortraitBio(portraitRect, pawn);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // PATCH — Portraits Cache to replace general pawn portrait renders in game UI
    // ─────────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(PortraitsCache), "Get")]
    public static class Patch_PortraitsCache_Get
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref RenderTexture __result, Vector2 size, Rot4 rotation)
        {
            if (pawn == null || __result == null) return;

            // Only override front-facing portraits (Rot4.South)
            if (rotation == Rot4.South)
            {
                GenerationStatus status;
                string error;
                Texture2D customTex = AIPortraitsManager.GetPortraitTexture(pawn, out status, out error);
                if (customTex != null)
                {
                    Graphics.Blit(customTex, __result);
                }
            }
        }
    }
}
