using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace AIPortraits
{
    public enum GenerationStatus { Idle, Generating, Error }

    public class PawnPortraitData
    {
        public Texture2D texture;
        public byte[]    rawBytes;
        public PortraitStyle style;
        public string    savedPath;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MANAGER — async generation, caching, triggers
    // ─────────────────────────────────────────────────────────────────────────────
    public static class AIPortraitsManager
    {
        private const string LockedSuffix = "_locked";
        private const int StateCacheLifetimeTicks = 60; // ~1 second of game time

        private class CachedState
        {
            public PawnState state;
            public int tickComputed;
        }

        private static Dictionary<string, Texture2D>         loadedTextures = new Dictionary<string, Texture2D>();
        private static Dictionary<string, string>            activeRequests = new Dictionary<string, string>();
        private static Dictionary<string, GenerationStatus>  requestStatus  = new Dictionary<string, GenerationStatus>();
        private static Dictionary<string, string>            requestError   = new Dictionary<string, string>();
        private static Dictionary<string, PawnPortraitData>  portraitData   = new Dictionary<string, PawnPortraitData>();
        private static Dictionary<string, CachedState>       stateCache     = new Dictionary<string, CachedState>();

        // Build the per-save key used by the activePortraits dict. Prefixed with the world's
        // persistentRandomValue so saves with overlapping ThingIDs don't bleed into each other.
        public static string GetActiveKey(Pawn pawn)
        {
            string worldId = "global";
            if (Find.World != null && Find.World.info != null)
                worldId = Find.World.info.persistentRandomValue.ToString();
            return worldId + "_" + pawn.ThingID;
        }

        public static PawnState GetCachedPawnState(Pawn pawn)
        {
            if (pawn == null) return null;
            int now = (Find.TickManager != null) ? Find.TickManager.TicksGame : 0;

            CachedState cs;
            if (stateCache.TryGetValue(pawn.ThingID, out cs) && (now - cs.tickComputed) < StateCacheLifetimeTicks)
                return cs.state;

            PawnState fresh = PawnStateExtractor.ExtractState(pawn);
            stateCache[pawn.ThingID] = new CachedState { state = fresh, tickComputed = now };
            return fresh;
        }

        public static Texture2D GetPortraitTexture(Pawn pawn, out GenerationStatus status, out string error)
        {
            status = GenerationStatus.Idle; error = null;
            if (pawn == null || pawn.Destroyed) return null;

            // Locked portrait (user pinned a specific image)
            string lockedPath;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.activePortraits.TryGetValue(GetActiveKey(pawn), out lockedPath))
            {
                if (!string.IsNullOrEmpty(lockedPath) && System.IO.File.Exists(lockedPath))
                {
                    string cacheKey = pawn.ThingID + LockedSuffix;
                    Texture2D lockedTex;
                    if (loadedTextures.TryGetValue(cacheKey, out lockedTex))
                        return lockedTex;

                    try
                    {
                        byte[] bytes = System.IO.File.ReadAllBytes(lockedPath);
                        Texture2D newTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (ImageConversion.LoadImage(newTex, bytes))
                        {
                            loadedTextures[cacheKey] = newTex;
                            return newTex;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[Dynamic AI Portraits] Failed to load locked portrait from " + lockedPath + ": " + ex.Message);
                    }
                }
            }

            PawnState state = GetCachedPawnState(pawn);
            if (state == null) return null;

            string stateHash = state.GetStateHash();
            string key = pawn.ThingID + "_" + stateHash;

            // In-memory hit
            Texture2D tex;
            if (loadedTextures.TryGetValue(key, out tex)) return tex;

            // Disk cache
            if (CacheManager.IsCached(stateHash))
            {
                Texture2D diskTex = CacheManager.LoadFromCache(stateHash);
                if (diskTex != null)
                {
                    ClearOldDynamicTextures(pawn.ThingID);
                    loadedTextures[key] = diskTex;
                    return diskTex;
                }
            }

            // Already generating for this pawn? Don't pile on more requests, regardless of hash.
            // (State can flicker every tick — drafted/sleeping/etc. — and would otherwise fire
            // a new API call each frame while one is already in flight.)
            if (activeRequests.ContainsKey(pawn.ThingID))
            {
                GenerationStatus cs;
                if (requestStatus.TryGetValue(pawn.ThingID, out cs)) status = cs;
                string ce;
                if (requestError.TryGetValue(pawn.ThingID, out ce)) error = ce;
                return null;
            }

            // Start generation
            TriggerGeneration(pawn, state, stateHash, null);
            status = GenerationStatus.Generating;
            return null;
        }

        public static void TriggerNewPortraitWithContinuity(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return;
            string pawnId = pawn.ThingID;
            string continuityToken = PromptCompiler.GetContinuityToken(AIPortraitsMod.settings.portraitStyle);
            activeRequests.Remove(pawnId); requestStatus.Remove(pawnId); requestError.Remove(pawnId);
            stateCache.Remove(pawnId); // force fresh extraction
            PawnState state = PawnStateExtractor.ExtractState(pawn);
            if (state == null) return;
            string stateHash = state.GetStateHash() + "_new_" + DateTime.Now.Ticks;
            TriggerGeneration(pawn, state, stateHash, continuityToken);
        }

        public static string SaveCurrentPortrait(Pawn pawn)
        {
            if (pawn == null) return null;
            PawnPortraitData data;
            if (!portraitData.TryGetValue(pawn.ThingID, out data)) return null;
            if (data.rawBytes == null || data.rawBytes.Length == 0) return null;
            return CacheManager.SavePortraitToDisk(pawn.LabelShortCap, data.style, data.rawBytes);
        }

        public static GenerationStatus GetStatus(Pawn pawn)
        {
            if (pawn == null) return GenerationStatus.Idle;
            GenerationStatus s;
            return requestStatus.TryGetValue(pawn.ThingID, out s) ? s : GenerationStatus.Idle;
        }

        public static string GetError(Pawn pawn)
        {
            if (pawn == null) return null;
            string e;
            return requestError.TryGetValue(pawn.ThingID, out e) ? e : null;
        }

        private static void TriggerGeneration(Pawn pawn, PawnState state, string stateHash, string continuityToken)
        {
            string pawnId = pawn.ThingID;
            PortraitStyle currentStyle = AIPortraitsMod.settings.portraitStyle;

            activeRequests[pawnId] = stateHash;
            requestStatus[pawnId] = GenerationStatus.Generating;
            requestError[pawnId] = null;

            string positivePrompt = PromptCompiler.CompilePositivePrompt(state, AIPortraitsMod.settings, continuityToken);
            // LOG the full prompt so user can verify context accuracy
            Log.Message("[Dynamic AI Portraits] PROMPT for " + pawn.LabelShortCap + ":\n" + positivePrompt);

            AsyncAIClient.QueueGeneration(state, AIPortraitsMod.settings, continuityToken, delegate(Texture2D tex, byte[] bytes, string err)
            {
                if (err != null)
                {
                    requestStatus[pawnId] = GenerationStatus.Error;
                    requestError[pawnId] = err;
                }
                else if (tex != null && bytes != null)
                {
                    CacheManager.SaveToCache(stateHash, bytes);

                    // Save to user documents directory automatically
                    string savedPath = CacheManager.SavePortraitToDisk(pawn.LabelShortCap, currentStyle, bytes);
                    if (savedPath != null)
                    {
                        try
                        {
                            string promptFile = System.IO.Path.ChangeExtension(savedPath, ".txt");
                            System.IO.File.WriteAllText(promptFile, positivePrompt);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Dynamic AI Portraits] Failed to save prompt text to disk: " + ex.Message);
                        }
                    }

                    ClearOldDynamicTextures(pawnId);
                    string key = pawnId + "_" + stateHash;
                    loadedTextures[key] = tex;
                    portraitData[pawnId] = new PawnPortraitData { texture = tex, rawBytes = bytes, style = currentStyle, savedPath = savedPath };
                    requestStatus.Remove(pawnId);
                    activeRequests.Remove(pawnId);
                    requestError.Remove(pawnId);

                    // Force refresh of Portraits Cache for Rimworld's UI
                    PortraitsCache.SetDirty(pawn);
                }
            });
        }

        // Sweep dynamic state-hash textures for this pawn — but never the locked entry, which is
        // owned by the user's pinned active-portrait selection.
        private static void ClearOldDynamicTextures(string pawnId)
        {
            string lockedKey = pawnId + LockedSuffix;
            List<string> keysToRemove = new List<string>();
            foreach (KeyValuePair<string, Texture2D> pair in loadedTextures)
            {
                if (pair.Key == lockedKey) continue;
                if (pair.Key.StartsWith(pawnId + "_"))
                    keysToRemove.Add(pair.Key);
            }
            foreach (string key in keysToRemove)
            {
                Texture2D oldTex = loadedTextures[key];
                if (oldTex != null) UnityEngine.Object.Destroy(oldTex);
                loadedTextures.Remove(key);
            }
        }

        public static void ClearPawnActiveTextureCache(Pawn pawn)
        {
            if (pawn == null) return;
            string cacheKey = pawn.ThingID + LockedSuffix;
            Texture2D tex;
            if (loadedTextures.TryGetValue(cacheKey, out tex))
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                loadedTextures.Remove(cacheKey);
            }
            PortraitsCache.SetDirty(pawn);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // UI — Minimal Bio tab portrait + config-based style
    // ─────────────────────────────────────────────────────────────────────────────
    public static class UI_AIPortraitCard
    {
        private const float StyleBarH = 26f;
        private const float ActionBtnH = 28f;
        private const float Gap = 4f;

        /// <summary>Total height needed in the Bio tab: square image + style bar + action bar + gaps</summary>
        public static float TotalHeight(float portraitWidth)
        {
            return portraitWidth + StyleBarH + ActionBtnH + Gap * 3f;
        }

        /// <summary>Bio tab — full-width square portrait + style bar + actions</summary>
        public static void DrawPortraitBio(Rect rect, Pawn pawn)
        {
            if (pawn == null) return;

            float imgSize = rect.width; // Perfect square using full tab width
            Rect imgRect = new Rect(rect.x, rect.y, imgSize, imgSize);
            Rect styleRect = new Rect(rect.x, imgRect.yMax + Gap, rect.width, StyleBarH);
            Rect actionRect = new Rect(rect.x, styleRect.yMax + Gap, rect.width, ActionBtnH);

            // Portrait background
            Widgets.DrawBoxSolid(imgRect, new Color(0.06f, 0.06f, 0.06f, 1f));
            
            GenerationStatus status; string error;
            Texture2D portrait = AIPortraitsManager.GetPortraitTexture(pawn, out status, out error);

            Rect inner = imgRect.ContractedBy(2f);
            if (portrait != null)
            {
                GUI.DrawTexture(inner, portrait, ScaleMode.ScaleToFit);
            }
            else if (status == GenerationStatus.Generating)
            {
                DrawCenteredLabel(inner, "Painting...", GameFont.Small, new Color(0.6f, 0.85f, 1f));
            }
            else if (status == GenerationStatus.Error)
            {
                DrawCenteredLabel(inner, "\u2716 " + (error ?? "Error"), GameFont.Tiny, Color.red);
            }
            else
            {
                DrawCenteredLabel(inner, "No portrait", GameFont.Small, new Color(0.45f, 0.45f, 0.45f));
            }

            // Image border
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Widgets.DrawBox(imgRect, 1);
            GUI.color = Color.white;

            // Style selector bar
            DrawStyleSelector(styleRect);

            // Action bar
            DrawActionBar(actionRect, pawn);
        }

        /// <summary>Draw clean portrait image only, no buttons</summary>
        public static void DrawPortraitClean(Rect rect, Pawn pawn)
        {
            if (pawn == null) return;

            GenerationStatus status; string error;
            Texture2D portrait = AIPortraitsManager.GetPortraitTexture(pawn, out status, out error);

            Rect inner = rect.ContractedBy(2f);
            if (portrait != null)
            {
                GUI.DrawTexture(inner, portrait, ScaleMode.ScaleToFit);
            }
            else
            {
                // Only draw background/border when no image is loaded
                Widgets.DrawBoxSolid(rect, new Color(0.06f, 0.06f, 0.06f, 0.8f));

                if (status == GenerationStatus.Generating)
                {
                    DrawCenteredLabel(inner, "Painting...", GameFont.Small, new Color(0.6f, 0.85f, 1f));
                }
                else if (status == GenerationStatus.Error)
                {
                    DrawCenteredLabel(inner, "\u2716 " + (error ?? "Error"), GameFont.Tiny, Color.red);
                }
                else
                {
                    DrawCenteredLabel(inner, "No portrait", GameFont.Small, new Color(0.45f, 0.45f, 0.45f));
                }

                // Image border
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                Widgets.DrawBox(rect, 1);
                GUI.color = Color.white;
            }
        }

        private static void DrawStyleSelector(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.06f, 0.06f, 0.06f, 0.9f));
            GUI.color = new Color(1f, 1f, 1f, 0.15f); Widgets.DrawBox(rect, 1); GUI.color = Color.white;

            float btnW = rect.width / 3f;
            float btnH = rect.height;

            Rect btnKorean = new Rect(rect.x, rect.y, btnW, btnH);
            Rect btnWestern = new Rect(rect.x + btnW, rect.y, btnW, btnH);
            Rect btnPixel = new Rect(rect.x + btnW * 2f, rect.y, btnW, btnH);

            DrawStyleButton(btnKorean, "🎨 Korean", PortraitStyle.Realistic_Korean, "Semi-realistic Korean RPG / manhwa style");
            DrawStyleButton(btnWestern, "⚔ Western", PortraitStyle.Realistic_Western, "Western dark fantasy oil painting");
            DrawStyleButton(btnPixel, "🟦 Pixel", PortraitStyle.DotPixel, "Retro pixel art / dot style");
        }

        private static void DrawStyleButton(Rect btn, string label, PortraitStyle style, string tooltip)
        {
            bool isActive = AIPortraitsMod.settings.portraitStyle == style;

            Color bgColor = isActive
                ? new Color(0.2f, 0.55f, 0.8f, 0.85f)
                : new Color(0.15f, 0.15f, 0.15f, 0.7f);

            Widgets.DrawBoxSolid(btn, bgColor);
            GUI.color = new Color(1f, 1f, 1f, 0.15f); Widgets.DrawBox(btn, 1); GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = isActive ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(btn, label);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(btn, tooltip);

            if (Widgets.ButtonInvisible(btn))
            {
                AIPortraitsMod.settings.portraitStyle = style;
                AIPortraitsMod.Instance.WriteSettings();
            }
        }

        private static void DrawActionBar(Rect rect, Pawn pawn)
        {
            float btnW = rect.width / 3f;
            float btnH = rect.height;

            Rect btnNew = new Rect(rect.x, rect.y, btnW, btnH);
            Rect btnSave = new Rect(rect.x + btnW, rect.y, btnW, btnH);
            Rect btnFolder = new Rect(rect.x + btnW * 2f, rect.y, btnW, btnH);

            // Button 1: New
            DrawButton(btnNew, "♻ New", new Color(0.2f, 0.55f, 0.35f), "Generate a new portrait using current traits and character vibe.");
            if (Widgets.ButtonInvisible(btnNew))
            {
                AIPortraitsManager.TriggerNewPortraitWithContinuity(pawn);
            }

            // Button 2: Save
            DrawButton(btnSave, "💾 Save", new Color(0.2f, 0.45f, 0.6f), "Save the current portrait permanently to your Documents folder.");
            if (Widgets.ButtonInvisible(btnSave))
            {
                string path = AIPortraitsManager.SaveCurrentPortrait(pawn);
                if (path != null)
                {
                    Messages.Message("Portrait saved to: " + System.IO.Path.GetFileName(path), MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    Messages.Message("No portrait available to save.", MessageTypeDefOf.RejectInput, false);
                }
            }

            // Button 3: Folder
            DrawButton(btnFolder, "📁 Folder", new Color(0.35f, 0.35f, 0.35f), "Open the folder containing saved portraits for this pawn.");
            if (Widgets.ButtonInvisible(btnFolder))
            {
                CacheManager.OpenPortraitFolder(pawn.LabelShortCap);
            }
        }

        private static void DrawButton(Rect rect, string label, Color bgColor, string tooltip)
        {
            Widgets.DrawBoxSolid(rect, bgColor);
            GUI.color = new Color(1f, 1f, 1f, 0.15f); Widgets.DrawBox(rect, 1); GUI.color = Color.white;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(rect, tooltip);
        }

        private static void DrawCenteredLabel(Rect rect, string text, GameFont font, Color color)
        {
            Text.Font = font; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = color; Widgets.Label(rect, text); GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
        }
    }
}