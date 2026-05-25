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

        public static string GetActiveFraming(Pawn pawn)
        {
            string f = "portrait";
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnFraming != null)
                AIPortraitsMod.settings.pawnFraming.TryGetValue(pawn.ThingID, out f);
            return f;
        }

        public static string GetActiveKeyForFraming(Pawn pawn, string framing)
        {
            string worldId = "global";
            if (Find.World != null && Find.World.info != null)
                worldId = Find.World.info.persistentRandomValue.ToString();
            return worldId + "_" + pawn.ThingID + "_" + framing;
        }

        public static string GetActiveKey(Pawn pawn)
        {
            return GetActiveKeyForFraming(pawn, GetActiveFraming(pawn));
        }

        public static PawnState GetCachedPawnState(Pawn pawn)
        {
            if (pawn == null) return null;
            int now = (Find.TickManager != null) ? Find.TickManager.TicksGame : 0;
            string framing = GetActiveFraming(pawn);
            string cacheKey = pawn.ThingID + "_" + framing;

            CachedState cs;
            if (stateCache.TryGetValue(cacheKey, out cs) && (now - cs.tickComputed) < StateCacheLifetimeTicks)
                return cs.state;

            PawnState fresh = PawnStateExtractor.ExtractState(pawn);
            stateCache[cacheKey] = new CachedState { state = fresh, tickComputed = now };
            return fresh;
        }

        /// <summary>
        /// Only generate portraits for humanlike pawns aligned with the player —
        /// colonists, prisoners, and slaves. Raiders, traders, animals, and mechs
        /// are skipped (no API calls, no overlay).
        /// </summary>
        public static bool ShouldGenerateFor(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return false;
            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return false;

            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return false;

            if (pawn.Faction == player)     return true;  // colonists
            if (pawn.HostFaction == player) return true;  // prisoners + slaves
            return false;
        }

        public static Texture2D GetPortraitTexture(Pawn pawn, out GenerationStatus status, out string error)
        {
            status = GenerationStatus.Idle; error = null;
            if (pawn == null || pawn.Destroyed) return null;

            // Locked portrait check happens first — user pinned this manually, honour it
            // even on pawns who later leave the colony.
            string lockedPath;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.activePortraits.TryGetValue(GetActiveKey(pawn), out lockedPath))
            {
                if (!string.IsNullOrEmpty(lockedPath) && System.IO.File.Exists(lockedPath))
                {
                    string lockedCacheKey = pawn.ThingID + "_" + GetActiveFraming(pawn) + LockedSuffix;
                    Texture2D lockedTex;
                    if (loadedTextures.TryGetValue(lockedCacheKey, out lockedTex))
                        return lockedTex;

                    try
                    {
                        byte[] bytes = System.IO.File.ReadAllBytes(lockedPath);
                        Texture2D newTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (ImageConversion.LoadImage(newTex, bytes))
                        {
                            loadedTextures[lockedCacheKey] = newTex;
                            return newTex;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[Dynamic AI Portraits] Failed to load locked portrait from " + lockedPath + ": " + ex.Message);
                    }
                }
            }

            // Faction gate — no auto-generation for raiders, traders, animals, mechs.
            if (!ShouldGenerateFor(pawn)) return null;

            string framing = GetActiveFraming(pawn);
            string pawnKey = pawn.ThingID + "_" + framing;
            string diskKey = GetActiveKey(pawn); // "worldId_pawnId_framing"

            // In-memory hit
            Texture2D tex;
            if (loadedTextures.TryGetValue(pawnKey, out tex)) return tex;

            // Disk cache — restore across game restarts
            if (CacheManager.IsCached(diskKey))
            {
                Texture2D diskTex = CacheManager.LoadFromCache(diskKey);
                if (diskTex != null)
                {
                    loadedTextures[pawnKey] = diskTex;
                    return diskTex;
                }
            }

            // Find any fallback texture if we don't have the active one yet (including during generation)
            string[] backupFramings = (framing == "portrait") ? new[] { "bodyshot", "special" } :
                                      (framing == "bodyshot") ? new[] { "portrait", "special" } :
                                                                new[] { "portrait", "bodyshot" };
            
            Texture2D fallbackTex = null;
            foreach (string backup in backupFramings)
            {
                string bKey = pawn.ThingID + "_" + backup;
                Texture2D bTex;
                if (loadedTextures.TryGetValue(bKey, out bTex) && bTex != null)
                {
                    fallbackTex = bTex;
                    break;
                }
                
                string bLockedKey = pawn.ThingID + "_" + backup + LockedSuffix;
                if (loadedTextures.TryGetValue(bLockedKey, out bTex) && bTex != null)
                {
                    fallbackTex = bTex;
                    break;
                }

                // Check activePortraits (gallery pinned path) for the backup framing
                string bActiveKey = GetActiveKeyForFraming(pawn, backup);
                string bLockedPath;
                if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.activePortraits.TryGetValue(bActiveKey, out bLockedPath))
                {
                    if (!string.IsNullOrEmpty(bLockedPath) && System.IO.File.Exists(bLockedPath))
                    {
                        try
                        {
                            byte[] bytes = System.IO.File.ReadAllBytes(bLockedPath);
                            Texture2D newTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (ImageConversion.LoadImage(newTex, bytes))
                            {
                                loadedTextures[bLockedKey] = newTex;
                                fallbackTex = newTex;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Dynamic AI Portraits] Failed to load backup locked portrait from " + bLockedPath + ": " + ex.Message);
                        }
                    }
                }

                string bDiskKey = GetActiveKeyForFraming(pawn, backup);
                if (CacheManager.IsCached(bDiskKey))
                {
                    Texture2D bDiskTex = CacheManager.LoadFromCache(bDiskKey);
                    if (bDiskTex != null)
                    {
                        loadedTextures[bKey] = bDiskTex;
                        fallbackTex = bDiskTex;
                        break;
                    }
                }
            }

            // Check if active request is running or failed for the selected framing
            GenerationStatus currentStatus = GenerationStatus.Idle;
            requestStatus.TryGetValue(pawnKey, out currentStatus);

            if (currentStatus == GenerationStatus.Generating)
            {
                status = GenerationStatus.Generating;
                requestError.TryGetValue(pawnKey, out error);
                return fallbackTex;
            }
            else if (currentStatus == GenerationStatus.Error)
            {
                status = GenerationStatus.Error;
                requestError.TryGetValue(pawnKey, out error);
                return fallbackTex;
            }

            // If we have a fallback, return it and remain Idle (we won't auto-generate!)
            if (fallbackTex != null)
            {
                status = GenerationStatus.Idle;
                return fallbackTex;
            }

            // If no cache and no fallback exists, trigger first-time generation for this pawn
            PawnState state = GetCachedPawnState(pawn);
            if (state == null) return null;

            TriggerGeneration(pawn, state, diskKey, null);
            status = GenerationStatus.Generating;
            return null;
        }

        /// <summary>
        /// Manual refresh — discards the current portrait (in-memory + disk cache)
        /// and queues a fresh generation. This is the ONLY path that re-generates;
        /// auto-regen on state change is disabled.
        /// </summary>
        public static void TriggerNewPortraitWithContinuity(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return;
            if (!ShouldGenerateFor(pawn)) return;

            string framing = GetActiveFraming(pawn);
            string pawnKey = pawn.ThingID + "_" + framing;
            string diskKey = GetActiveKey(pawn);
            string continuityToken = PromptCompiler.GetContinuityToken(AIPortraitsMod.settings.portraitStyle);

            // Drop in-memory texture (but preserve any locked one — that's user-pinned)
            Texture2D oldTex;
            if (loadedTextures.TryGetValue(pawnKey, out oldTex))
            {
                if (oldTex != null) UnityEngine.Object.Destroy(oldTex);
                loadedTextures.Remove(pawnKey);
            }

            // Drop disk cache so the next GetPortraitTexture call won't reload the stale one
            try
            {
                string diskPath = CacheManager.GetFilePath(diskKey);
                if (System.IO.File.Exists(diskPath)) System.IO.File.Delete(diskPath);
            }
            catch (Exception ex) { Log.Warning("[Dynamic AI Portraits] Could not clear disk cache: " + ex.Message); }

            // Reset request bookkeeping + force fresh state extraction
            activeRequests.Remove(pawnKey); requestStatus.Remove(pawnKey); requestError.Remove(pawnKey);
            stateCache.Remove(pawn.ThingID + "_" + framing);

            PawnState state = PawnStateExtractor.ExtractState(pawn);
            if (state == null) return;
            TriggerGeneration(pawn, state, diskKey, continuityToken);
        }


        public static string SaveCurrentPortrait(Pawn pawn)
        {
            if (pawn == null) return null;
            PawnPortraitData data;
            string framing = GetActiveFraming(pawn);
            string pawnKey = pawn.ThingID + "_" + framing;
            if (!portraitData.TryGetValue(pawnKey, out data)) return null;
            if (data.rawBytes == null || data.rawBytes.Length == 0) return null;
            return CacheManager.SavePortraitToDisk(pawn.LabelShortCap, data.style, data.rawBytes);
        }

        public static GenerationStatus GetStatus(Pawn pawn)
        {
            if (pawn == null) return GenerationStatus.Idle;
            GenerationStatus s;
            string framing = GetActiveFraming(pawn);
            string pawnKey = pawn.ThingID + "_" + framing;
            return requestStatus.TryGetValue(pawnKey, out s) ? s : GenerationStatus.Idle;
        }

        public static string GetError(Pawn pawn)
        {
            if (pawn == null) return null;
            string e;
            string framing = GetActiveFraming(pawn);
            string pawnKey = pawn.ThingID + "_" + framing;
            return requestError.TryGetValue(pawnKey, out e) ? e : null;
        }

        // `diskCacheKey` is the stable per-pawn-per-save key (worldId_pawnId_framing).
        // The texture is stored in loadedTextures under pawn.ThingID + "_" + framing, and on disk under diskCacheKey.
        private static void TriggerGeneration(Pawn pawn, PawnState state, string diskCacheKey, string continuityToken)
        {
            string framing = state.framing ?? GetActiveFraming(pawn);
            string pawnKey = pawn.ThingID + "_" + framing;
            PortraitStyle currentStyle = AIPortraitsMod.settings.portraitStyle;

            activeRequests[pawnKey] = diskCacheKey;
            requestStatus[pawnKey]  = GenerationStatus.Generating;
            requestError[pawnKey]   = null;

            string positivePrompt = PromptCompiler.CompilePositivePrompt(state, AIPortraitsMod.settings, continuityToken);
            Log.Message("[Dynamic AI Portraits] PROMPT for " + pawn.LabelShortCap + " (" + framing + "):\n" + positivePrompt);

            AsyncAIClient.QueueGeneration(state, AIPortraitsMod.settings, continuityToken, delegate(Texture2D tex, byte[] bytes, string promptUsed, string err)
            {
                if (err != null)
                {
                    requestStatus[pawnKey] = GenerationStatus.Error;
                    requestError[pawnKey] = err;
                    activeRequests.Remove(pawnKey);
                }
                else if (tex != null && bytes != null)
                {
                    // Disk cache (per-save, single file per pawn — overwrites previous)
                    CacheManager.SaveToCache(diskCacheKey, bytes);

                    // User-visible gallery save (Documents/RimWorld Portraits/<name>/, timestamped)
                    string savedPath = CacheManager.SavePortraitToDisk(pawn.LabelShortCap, currentStyle, bytes);
                    if (savedPath != null)
                    {
                        try
                        {
                            string promptFile = System.IO.Path.ChangeExtension(savedPath, ".txt");
                            System.IO.File.WriteAllText(promptFile, promptUsed ?? positivePrompt);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Dynamic AI Portraits] Failed to save prompt text to disk: " + ex.Message);
                        }
                    }

                    // Destroy previous in-memory texture for this pawn (if any) before replacing
                    Texture2D prev;
                    if (loadedTextures.TryGetValue(pawnKey, out prev) && prev != null && prev != tex)
                        UnityEngine.Object.Destroy(prev);

                    loadedTextures[pawnKey] = tex;
                    portraitData[pawnKey] = new PawnPortraitData { texture = tex, rawBytes = bytes, style = currentStyle, savedPath = savedPath };
                    requestStatus.Remove(pawnKey);
                    activeRequests.Remove(pawnKey);
                    requestError.Remove(pawnKey);

                    // Auto-pin the freshly generated portrait as the active one for this pawn.
                    // Without this, a previously-locked portrait would keep taking priority in
                    // GetPortraitTexture() and the refresh would appear to do nothing visually.
                    // Also cache the locked texture so the next render does not do a redundant disk read.
                    if (!string.IsNullOrEmpty(savedPath) && AIPortraitsMod.settings != null)
                    {
                        string activeKey = GetActiveKeyForFraming(pawn, framing);
                        AIPortraitsMod.settings.activePortraits[activeKey] = savedPath;
                        AIPortraitsMod.Instance.WriteSettings();

                        string lockedCacheKey = pawnKey + LockedSuffix;
                        Texture2D oldLocked;
                        if (loadedTextures.TryGetValue(lockedCacheKey, out oldLocked))
                        {
                            if (oldLocked != null && oldLocked != tex) UnityEngine.Object.Destroy(oldLocked);
                        }
                        loadedTextures[lockedCacheKey] = tex;
                    }

                    PortraitsCache.SetDirty(pawn);
                }
            });
        }

        public static void ClearPawnActiveTextureCache(Pawn pawn)
        {
            if (pawn == null) return;
            string framing = GetActiveFraming(pawn);
            string cacheKey = pawn.ThingID + "_" + framing + LockedSuffix;
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

            DrawStyleButton(btnKorean, "🎨 Webtoon", PortraitStyle.Realistic_Korean, "Korean webtoon manhwa (Solo Leveling style)");
            DrawStyleButton(btnWestern, "📺 Cartoon", PortraitStyle.Realistic_Western, "Rick and Morty / Adult Swim cartoon style");
            DrawStyleButton(btnPixel, "🟦 Pixel", PortraitStyle.DotPixel, "Retro pixel art / dot style");
        }

        private static void DrawStyleButton(Rect btn, string label, PortraitStyle style, string tooltip)
        {
            bool isActive = AIPortraitsMod.settings.portraitStyle == style;
            bool isHovered = Mouse.IsOver(btn);

            Color bgColor = isActive
                ? new Color(0.2f, 0.55f, 0.8f, 0.85f)
                : new Color(0.15f, 0.15f, 0.15f, 0.7f);

            if (isHovered)
            {
                bgColor.r += 0.1f;
                bgColor.g += 0.1f;
                bgColor.b += 0.1f;
            }

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
            if (Mouse.IsOver(rect))
            {
                bgColor.r += 0.1f;
                bgColor.g += 0.1f;
                bgColor.b += 0.1f;
            }
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