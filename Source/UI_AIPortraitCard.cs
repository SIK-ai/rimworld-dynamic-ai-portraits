using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
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
    public struct PawnFramingKey : IEquatable<PawnFramingKey>
    {
        public readonly int pawnId;
        public readonly string framing;

        public PawnFramingKey(int pawnId, string framing)
        {
            this.pawnId = pawnId;
            this.framing = framing;
        }

        public bool Equals(PawnFramingKey other)
        {
            return pawnId == other.pawnId && framing == other.framing;
        }

        public override int GetHashCode()
        {
            // C# 5 (csc.exe v4.0.30319) has no null-conditional ?. operator
            return pawnId ^ (framing != null ? framing.GetHashCode() : 0);
        }

        public override bool Equals(object obj)
        {
            // C# 5 (csc.exe v4.0.30319) has no `is T name` pattern matching
            if (!(obj is PawnFramingKey)) return false;
            return Equals((PawnFramingKey)obj);
        }
    }

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

        // Per-frame perf optimisation (originally Jules-bot bolt/optimize-portrait-render-loop).
        // GetActiveKey is called every frame on every selected pawn; without caching it allocates
        // strings on every paint. knownMissingFiles avoids hammering File.Exists on locked-portrait
        // paths the user has deleted.
        private static Dictionary<PawnFramingKey, string> activeKeyCache    = new Dictionary<PawnFramingKey, string>();
        private static HashSet<string>            knownMissingFiles = new HashSet<string>();
        private static int                        lastWorldId       = -1;
        private static string                     lastWorldIdString = "global";

        public static string GetActiveFraming(Pawn pawn)
        {
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnFraming != null)
            {
                string val;
                if (AIPortraitsMod.settings.pawnFraming.TryGetValue(pawn.ThingID, out val) && !string.IsNullOrEmpty(val))
                    return val;
            }
            return "portrait";
        }

        public static string GetActiveKeyForFraming(Pawn pawn, string framing)
        {
            // Invalidate cache if world changed (new save loaded)
            int currentWorldId = -1;
            if (Find.World != null && Find.World.info != null)
                currentWorldId = Find.World.info.persistentRandomValue;
            if (currentWorldId != lastWorldId)
            {
                lastWorldId = currentWorldId;
                lastWorldIdString = currentWorldId == -1 ? "global" : currentWorldId.ToString();
                activeKeyCache.Clear();
                knownMissingFiles.Clear();
            }

            // Cache key composed of pawn + framing — both are inputs to the final key
            PawnFramingKey cacheLookup = new PawnFramingKey(pawn.thingIDNumber, framing);
            string cached;
            if (activeKeyCache.TryGetValue(cacheLookup, out cached))
                return cached;

            string built = lastWorldIdString + "_" + pawn.ThingID + "_" + framing;
            activeKeyCache[cacheLookup] = built;
            return built;
        }

        public static string GetActiveKey(Pawn pawn)
        {
            return GetActiveKeyForFraming(pawn, GetActiveFraming(pawn));
        }

        public static string GetActivePortraitPath(Pawn pawn, string framing)
        {
            if (pawn == null) return null;
            if (AIPortraitsMod.settings == null || AIPortraitsMod.settings.activePortraits == null) return null;
            string key = GetActiveKeyForFraming(pawn, framing);
            string path;
            if (AIPortraitsMod.settings.activePortraits.TryGetValue(key, out path) && !string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                return path;
            }
            if (framing == "portrait")
            {
                // Fallback to legacy key without framing suffix
                string worldId = "global";
                if (Find.World != null && Find.World.info != null)
                    worldId = Find.World.info.persistentRandomValue.ToString();
                string legacyKey = worldId + "_" + pawn.ThingID;
                if (AIPortraitsMod.settings.activePortraits.TryGetValue(legacyKey, out path) && !string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the raw bytes of the currently displayed portrait image for this pawn,
        /// using whichever framing (portrait / bodyshot / special) is currently active.
        /// This is used for video generation input — NOT the reference gear sheet.
        /// </summary>
        public static byte[] GetActivePortraitBytes(Pawn pawn)
        {
            if (pawn == null) return null;
            // Use the active framing so the video matches what the user is looking at.
            string framing = GetActiveFraming(pawn);
            return GetActivePortraitBytesForFraming(pawn, framing);
        }

        /// <summary>
        /// Returns raw bytes for a specific framing, with a fallback to 'portrait'.
        /// Used internally — image generation still passes 'portrait' bytes as its
        /// continuity input, while video generation uses the active framing.
        /// </summary>
        public static byte[] GetActivePortraitBytesForFraming(Pawn pawn, string framing)
        {
            if (pawn == null) return null;
            string path = GetActivePortraitPath(pawn, framing);
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                try { return System.IO.File.ReadAllBytes(path); }
                catch (Exception ex)
                {
                    Log.Warning("[Dynamic AI Portraits] Failed to read portrait file (" + framing + "): " + ex.Message);
                }
            }
            // Fallback to portrait framing if the requested framing has no image yet
            if (framing != "portrait")
            {
                string fallbackPath = GetActivePortraitPath(pawn, "portrait");
                if (!string.IsNullOrEmpty(fallbackPath) && System.IO.File.Exists(fallbackPath))
                {
                    try { return System.IO.File.ReadAllBytes(fallbackPath); }
                    catch { }
                }
            }
            return null;
        }

        // ── Per-pawn-per-framing "live" (video) toggle ────────────────────────────────
        // The [V] button enables video MODE for the CURRENT framing only — portrait,
        // bodyshot, and special each track their own still-vs-live state. Keying the
        // toggle by pawn+framing means switching framing (or pawn) never carries the
        // live state across to a different shot.
        private static string VideoToggleKey(Pawn pawn)
        {
            return pawn.ThingID + "_" + GetActiveFraming(pawn);
        }

        public static bool IsVideoMode(Pawn pawn)
        {
            if (pawn == null || AIPortraitsMod.settings == null || AIPortraitsMod.settings.pawnVideoToggles == null)
                return false;
            bool on;
            return AIPortraitsMod.settings.pawnVideoToggles.TryGetValue(VideoToggleKey(pawn), out on) && on;
        }

        public static void SetVideoMode(Pawn pawn, bool on)
        {
            if (pawn == null || AIPortraitsMod.settings == null) return;
            if (AIPortraitsMod.settings.pawnVideoToggles == null)
                AIPortraitsMod.settings.pawnVideoToggles = new Dictionary<string, bool>();
            AIPortraitsMod.settings.pawnVideoToggles[VideoToggleKey(pawn)] = on;
        }

        // ── Per-image generation Settings (helmet / gear-ref / reference-portrait) ────────
        // Keyed per pawn+framing so each portrait/bodyshot/special keeps its own values.
        private static string ImgKey(Pawn pawn, string framing) { return pawn.ThingID + "_" + framing; }

        public static bool GetExcludeHelmet(Pawn pawn, string framing)
        {
            bool v;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnExcludeHelmet != null &&
                AIPortraitsMod.settings.pawnExcludeHelmet.TryGetValue(ImgKey(pawn, framing), out v)) return v;
            return AIPortraitsMod.settings != null && AIPortraitsMod.settings.excludeHelmet;   // global fallback
        }
        public static void SetExcludeHelmet(Pawn pawn, string framing, bool on)
        { if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnExcludeHelmet != null) AIPortraitsMod.settings.pawnExcludeHelmet[ImgKey(pawn, framing)] = on; }

        public static bool GetGearRef(Pawn pawn, string framing)
        {
            bool v;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnGearRef != null &&
                AIPortraitsMod.settings.pawnGearRef.TryGetValue(ImgKey(pawn, framing), out v)) return v;
            return AIPortraitsMod.settings == null || AIPortraitsMod.settings.useGearReferenceSheet;   // global fallback (default on)
        }
        public static void SetGearRef(Pawn pawn, string framing, bool on)
        { if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnGearRef != null) AIPortraitsMod.settings.pawnGearRef[ImgKey(pawn, framing)] = on; }

        public static bool GetRefPortrait(Pawn pawn, string framing)
        {
            bool v;
            if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnRefPortrait != null &&
                AIPortraitsMod.settings.pawnRefPortrait.TryGetValue(ImgKey(pawn, framing), out v)) return v;
            return framing != "portrait";   // default: bodyshot + special ON, portrait OFF
        }
        public static void SetRefPortrait(Pawn pawn, string framing, bool on)
        { if (AIPortraitsMod.settings != null && AIPortraitsMod.settings.pawnRefPortrait != null) AIPortraitsMod.settings.pawnRefPortrait[ImgKey(pawn, framing)] = on; }

        public static PawnState GetCachedPawnState(Pawn pawn)
        {
            if (pawn == null) return null;
            int now = (Find.TickManager != null) ? Find.TickManager.TicksGame : 0;
            string framing = GetActiveFraming(pawn);
            string cacheKey = pawn.ThingID + "_" + framing;

            CachedState cs;
            if (stateCache.TryGetValue(cacheKey, out cs) && (now - cs.tickComputed) < StateCacheLifetimeTicks)
            {
                if (cs.state != null) cs.state.framing = framing;
                return cs.state;
            }

            PawnState fresh = PawnStateExtractor.ExtractState(pawn);
            if (fresh != null) fresh.framing = framing;
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
            string lockedPath = GetActivePortraitPath(pawn, GetActiveFraming(pawn));
            if (!string.IsNullOrEmpty(lockedPath))
            {
                string lockedCacheKey = pawn.ThingID + "_" + GetActiveFraming(pawn) + LockedSuffix;

                // Memory fast-path (no I/O)
                Texture2D lockedTex;
                if (loadedTextures.TryGetValue(lockedCacheKey, out lockedTex))
                    return lockedTex;

                // Skip File.Exists/ReadAllBytes if we've already learned this path is gone.
                // knownMissingFiles is cleared whenever a portrait is re-saved or world changes.
                if (!knownMissingFiles.Contains(lockedPath))
                {
                    if (System.IO.File.Exists(lockedPath))
                    {
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
                    else
                    {
                        knownMissingFiles.Add(lockedPath);
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

            // We do not have the requested framing.
            // Check if the pawn has ANY portrait for ANY framing.
            bool hasAnyPortrait = false;
            string[] allFramings = new[] { "portrait", "bodyshot", "special" };
            foreach (string f in allFramings)
            {
                if (f == framing) continue;
                string fPawnKey = pawn.ThingID + "_" + f;
                string fDiskKey = GetActiveKeyForFraming(pawn, f);
                string fLockedPath = GetActivePortraitPath(pawn, f);

                if (loadedTextures.ContainsKey(fPawnKey) || loadedTextures.ContainsKey(fPawnKey + LockedSuffix)) { hasAnyPortrait = true; break; }
                if (CacheManager.IsCached(fDiskKey)) { hasAnyPortrait = true; break; }
                if (!string.IsNullOrEmpty(fLockedPath)) { hasAnyPortrait = true; break; }
            }

            // Check if active request is running or failed for the selected framing
            GenerationStatus currentStatus = GenerationStatus.Idle;
            requestStatus.TryGetValue(pawnKey, out currentStatus);

            if (currentStatus == GenerationStatus.Generating)
            {
                status = GenerationStatus.Generating;
                requestError.TryGetValue(pawnKey, out error);
                return null;
            }
            else if (currentStatus == GenerationStatus.Error)
            {
                status = GenerationStatus.Error;
                requestError.TryGetValue(pawnKey, out error);
                return null;
            }

            // If the pawn already has a portrait for ANOTHER framing, do NOT auto-generate!
            // Wait for the user to manually click Refresh.
            if (hasAnyPortrait)
            {
                return null;
            }

            // Trigger first-time generation ONLY if the pawn has NO portraits at all.
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

            // Re-entrancy guard: ignore a refresh while an image generation is already
            // running for this pawn+framing (double-click, or racing the auto-trigger).
            GenerationStatus inFlight;
            if (requestStatus.TryGetValue(pawnKey, out inFlight) && inFlight == GenerationStatus.Generating)
                return;

            string diskKey = GetActiveKey(pawn);
            string continuityToken = PromptCompiler.GetContinuityToken(AIPortraitsMod.settings.portraitStyle);

            // Drop in-memory texture. The locked-cache entry MUST also be dropped because
            // when a portrait is auto-pinned at generation time, both pawnKey and
            // lockedCacheKey end up pointing to the same Texture2D — destroying the pawnKey
            // entry without clearing lockedCacheKey leaves a dangling reference that the
            // overlay would try to render between refresh + new-generation-complete.
            string lockedCacheKey = pawnKey + LockedSuffix;
            Texture2D oldTex;
            if (loadedTextures.TryGetValue(pawnKey, out oldTex))
            {
                if (oldTex != null) UnityEngine.Object.Destroy(oldTex);
                loadedTextures.Remove(pawnKey);
            }
            Texture2D oldLockedRef;
            if (loadedTextures.TryGetValue(lockedCacheKey, out oldLockedRef))
            {
                // Don't double-destroy if it's the same Texture2D we just freed above.
                if (oldLockedRef != null && oldLockedRef != oldTex)
                    UnityEngine.Object.Destroy(oldLockedRef);
                loadedTextures.Remove(lockedCacheKey);
            }

            // Drop disk cache so the next GetPortraitTexture call won't reload the stale one
            try
            {
                string diskPath = CacheManager.GetFilePath(diskKey);
                if (System.IO.File.Exists(diskPath)) System.IO.File.Delete(diskPath);
            }
            catch (Exception ex) { Log.Warning("[Dynamic AI Portraits] Could not clear disk cache: " + ex.Message); }

            // Clear video cache and stop playback if active
            try
            {
                string videoPath = System.IO.Path.Combine(CacheManager.GetCacheDirectory(), diskKey + ".mp4");
                if (System.IO.File.Exists(videoPath)) System.IO.File.Delete(videoPath);
            }
            catch (Exception ex) { Log.Warning("[Dynamic AI Portraits] Could not clear video cache: " + ex.Message); }

            string videoKey = pawn.ThingID + "_" + framing;
            videoStatus.Remove(videoKey);
            videoError.Remove(videoKey);
            VideoPlaybackManager.StopPlayback();

            // Reset request bookkeeping + force fresh state extraction
            activeRequests.Remove(pawnKey); requestStatus.Remove(pawnKey); requestError.Remove(pawnKey);
            stateCache.Remove(pawn.ThingID + "_" + framing);

            PawnState state = PawnStateExtractor.ExtractState(pawn);
            if (state == null) return;
            state.framing = framing;
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
            return CacheManager.SavePortraitToDisk(pawn, data.style, framing, data.rawBytes);
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

            // Re-entrancy guard: never start a second image generation while one is in
            // flight for this pawn+framing (protects the per-frame auto-trigger path).
            GenerationStatus inFlight;
            if (requestStatus.TryGetValue(pawnKey, out inFlight) && inFlight == GenerationStatus.Generating)
            {
                DebugLog.Log("FSM", "image GEN blocked (already Generating) key=" + pawnKey);
                return;
            }

            PortraitStyle currentStyle = AIPortraitsMod.settings.portraitStyle;

            activeRequests[pawnKey] = diskCacheKey;
            requestStatus[pawnKey]  = GenerationStatus.Generating;
            requestError[pawnKey]   = null;
            DebugLog.Log("FSM", "image GEN start key=" + pawnKey + " -> Generating  backend=" + AIPortraitsMod.settings.backendType + " style=" + currentStyle);

            // Apply this image's per-image Settings (helmet / gear-ref / reference-portrait)
            // onto the state so the prompt, reference sheet, and img2img input all honour them.
            if (state != null)
            {
                state.excludeHelmet = GetExcludeHelmet(pawn, framing);
                state.useGearRef    = GetGearRef(pawn, framing);
                state.refPortrait   = GetRefPortrait(pawn, framing);
            }

            string positivePrompt = PromptCompiler.CompilePositivePrompt(state, AIPortraitsMod.settings, continuityToken);
            if (Prefs.DevMode) Log.Message("[Dynamic AI Portraits] PROMPT for " + pawn.LabelShortCap + " (" + framing + "):\n" + positivePrompt);

            // When "reference portrait image" is on for this image, feed the pawn's existing
            // portrait to the generator as an img2img reference (continuity). Default: on for
            // bodyshot/special, off for portrait.
            byte[] portraitBytes = null;
            if (state != null && state.refPortrait)
            {
                portraitBytes = GetActivePortraitBytesForFraming(pawn, "portrait");
            }

            AsyncAIClient.QueueGeneration(state, AIPortraitsMod.settings, continuityToken, portraitBytes, delegate(Texture2D tex, byte[] bytes, string promptUsed, string err)
            {
                if (err != null)
                {
                    requestStatus[pawnKey] = GenerationStatus.Error;
                    requestError[pawnKey] = err;
                    activeRequests.Remove(pawnKey);
                    DebugLog.Log("FSM", "image GEN ERROR key=" + pawnKey + " -> Error: " + err);
                }
                else if (tex != null && bytes != null)
                {
                    // Disk cache (per-save, single file per pawn — overwrites previous)
                    CacheManager.SaveToCache(diskCacheKey, bytes);

                    // User-visible gallery save (Documents/RimWorld Portraits/<name>_<id>/, timestamped)
                    string savedPath = CacheManager.SavePortraitToDisk(pawn, currentStyle, framing, bytes);
                    if (savedPath != null)
                    {
                        try
                        {
                            string promptFile = System.IO.Path.ChangeExtension(savedPath, ".txt");
                            System.IO.File.WriteAllText(promptFile, promptUsed ?? positivePrompt);

                            // Save the gear reference sheet only if this image used it
                            if (state != null && state.useGearRef)
                            {
                                byte[] refSheet = AsyncAIClient.BuildReferenceSheet(state);
                                if (refSheet != null && refSheet.Length > 0)
                                {
                                    string gearFile = System.IO.Path.ChangeExtension(savedPath, null) + "_ref_gear.png";
                                    System.IO.File.WriteAllBytes(gearFile, refSheet);
                                }
                            }
                            if (portraitBytes != null && portraitBytes.Length > 0)
                            {
                                string portFile = System.IO.Path.ChangeExtension(savedPath, null) + "_ref_portrait.png";
                                System.IO.File.WriteAllBytes(portFile, portraitBytes);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Dynamic AI Portraits] Failed to save prompt text or references to disk: " + ex.Message);
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
                    DebugLog.Log("FSM", "image GEN done key=" + pawnKey + " -> Idle  saved=" + (savedPath ?? "(cache only)"));

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

        public static void TriggerCustomGeneration(Pawn pawn, string customPrompt)
        {
            if (pawn == null || pawn.Destroyed) return;
            if (!ShouldGenerateFor(pawn)) return;

            string framing = GetActiveFraming(pawn);
            string pawnKey = pawn.ThingID + "_" + framing;
            string diskKey = GetActiveKey(pawn);
            PortraitStyle currentStyle = AIPortraitsMod.settings.portraitStyle;

            // Re-entrancy guard: ignore a second custom generation while one is running.
            GenerationStatus inFlight;
            if (requestStatus.TryGetValue(pawnKey, out inFlight) && inFlight == GenerationStatus.Generating)
                return;

            // Mark as generating
            activeRequests[pawnKey] = diskKey;
            requestStatus[pawnKey]  = GenerationStatus.Generating;
            requestError[pawnKey]   = null;

            PawnState state = GetCachedPawnState(pawn);
            if (state == null)
            {
                state = PawnStateExtractor.ExtractState(pawn);
            }
            if (state != null)
            {
                state.framing = framing;
            }

            if (state != null)
            {
                state.excludeHelmet = GetExcludeHelmet(pawn, framing);
                state.useGearRef    = GetGearRef(pawn, framing);
                state.refPortrait   = GetRefPortrait(pawn, framing);
            }

            if (Prefs.DevMode) Log.Message("[Dynamic AI Portraits] CUSTOM PROMPT for " + pawn.LabelShortCap + " (" + framing + "):\n" + customPrompt);

            // "reference portrait image" -> feed the existing portrait as an img2img reference.
            byte[] portraitBytes = null;
            if (state != null && state.refPortrait)
            {
                portraitBytes = GetActivePortraitBytesForFraming(pawn, "portrait");
            }

            AsyncAIClient.QueueCustomGeneration(customPrompt, AIPortraitsMod.settings, state, portraitBytes, delegate(Texture2D tex, byte[] bytes, string promptUsed, string err)
            {
                if (err != null)
                {
                    requestStatus[pawnKey] = GenerationStatus.Error;
                    requestError[pawnKey] = err;
                    activeRequests.Remove(pawnKey);
                    DebugLog.Log("FSM", "image GEN ERROR key=" + pawnKey + " -> Error: " + err);
                }
                else if (tex != null && bytes != null)
                {
                    // Disk cache (per-save, single file per pawn — overwrites previous)
                    CacheManager.SaveToCache(diskKey, bytes);

                    // User-visible gallery save (Documents/RimWorld Portraits/<name>_<id>/, timestamped)
                    string savedPath = CacheManager.SavePortraitToDisk(pawn, currentStyle, framing, bytes);
                    if (savedPath != null)
                    {
                        try
                        {
                            string promptFile = System.IO.Path.ChangeExtension(savedPath, ".txt");
                            System.IO.File.WriteAllText(promptFile, promptUsed ?? customPrompt);

                            // Save the gear reference sheet only if this image used it
                            if (state != null && state.useGearRef)
                            {
                                byte[] refSheet = AsyncAIClient.BuildReferenceSheet(state);
                                if (refSheet != null && refSheet.Length > 0)
                                {
                                    string gearFile = System.IO.Path.ChangeExtension(savedPath, null) + "_ref_gear.png";
                                    System.IO.File.WriteAllBytes(gearFile, refSheet);
                                }
                            }
                            if (portraitBytes != null && portraitBytes.Length > 0)
                            {
                                string portFile = System.IO.Path.ChangeExtension(savedPath, null) + "_ref_portrait.png";
                                System.IO.File.WriteAllBytes(portFile, portraitBytes);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Dynamic AI Portraits] Failed to save custom prompt text or references to disk: " + ex.Message);
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
                    DebugLog.Log("FSM", "image GEN done key=" + pawnKey + " -> Idle  saved=" + (savedPath ?? "(cache only)"));

                    // Auto-pin the freshly generated portrait as the active one for this pawn.
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
            // Clear "known missing" set — the file may have been regenerated/restored
            knownMissingFiles.Clear();
            PortraitsCache.SetDirty(pawn);
        }

        // ── Video generation helpers ──────────────────────────────────────────────────
        public static Dictionary<string, GenerationStatus> videoStatus = new Dictionary<string, GenerationStatus>();
        public static Dictionary<string, string>           videoError  = new Dictionary<string, string>();

        public static void GetVideoStatus(Pawn pawn, out GenerationStatus status, out string error)
        {
            status = GenerationStatus.Idle;
            error = null;
            if (pawn == null) return;
            string key = pawn.ThingID + "_" + GetActiveFraming(pawn);
            videoStatus.TryGetValue(key, out status);
            videoError.TryGetValue(key, out error);
        }

        public static void TriggerVideoGeneration(Pawn pawn, byte[] initImageBytes)
        {
            if (pawn == null || pawn.Destroyed) return;
            string framing = GetActiveFraming(pawn);
            string key = pawn.ThingID + "_" + framing;

            // Re-entrancy guard: if a video is already generating for this pawn+framing,
            // ignore the re-fire (double-click, or manual refresh racing the auto-trigger).
            GenerationStatus inFlight;
            if (videoStatus.TryGetValue(key, out inFlight) && inFlight == GenerationStatus.Generating)
            {
                DebugLog.Log("FSM", "video GEN blocked (already Generating) key=" + key);
                return;
            }

            videoStatus[key] = GenerationStatus.Generating;
            videoError[key] = null;
            DebugLog.Log("FSM", "video GEN start key=" + key + " -> Generating");

            PawnState state = GetCachedPawnState(pawn);
            if (state != null)
            {
                state.framing = framing;
            }
            string apiKey = !string.IsNullOrEmpty(AIPortraitsMod.settings.videoApiKey)
                ? AIPortraitsMod.settings.videoApiKey
                : AIPortraitsMod.settings.giApiKey;

            AsyncAIClient.QueueVideoGeneration(pawn, state, initImageBytes, apiKey, delegate(string videoPath, string err)
            {
                if (err != null)
                {
                    videoStatus[key] = GenerationStatus.Error;
                    videoError[key] = err;
                    Log.Error("[Dynamic AI Portraits] Veo Video Generation Error: " + err);
                    DebugLog.Log("FSM", "video GEN ERROR key=" + key + " -> Error: " + err);
                }
                else
                {
                    videoStatus.Remove(key);
                    videoError.Remove(key);
                    if (Prefs.DevMode) Log.Message("[Dynamic AI Portraits] Veo Video Generation Succeeded for " + pawn.LabelShortCap);
                    DebugLog.Log("FSM", "video GEN done key=" + key + " -> Idle  file=" + (string.IsNullOrEmpty(videoPath) ? "?" : System.IO.Path.GetFileName(videoPath)));
                    // Kick off background removal immediately so it runs while the player is
                    // still here, rather than waiting until the clip is next drawn. No-op for
                    // "special" and idempotent (guarded inside EnsureMatted).
                    if (!string.IsNullOrEmpty(videoPath))
                        VideoMatteService.EnsureMatted(videoPath, framing);
                }
            });
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

            DrawStyleButton(btnKorean, "🎨 Webtoon / Manhwa", PortraitStyle.Realistic_Korean, "Korean webtoon manhwa style");
            DrawStyleButton(btnWestern, "📺 Cartoon", PortraitStyle.Realistic_Western, "Rick and Morty / Adult Swim cartoon style");
            DrawStyleButton(btnPixel, "🟦 Pixel", PortraitStyle.DotPixel, "Retro pixel art / dot style");
        }

        private static void DrawStyleButton(Rect btn, string label, PortraitStyle style, string tooltip)
        {
            bool isActive  = AIPortraitsMod.settings.portraitStyle == style;
            bool isHovered = Mouse.IsOver(btn);

            Color bgColor = isActive
                ? new Color(0.2f, 0.55f, 0.8f, 0.85f)
                : new Color(0.15f, 0.15f, 0.15f, 0.7f);

            // Hover brightness boost (originally proposed by Jules-bot palette/add-hover-states)
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

            // Button 1: New — respects the per-framing [V] live toggle
            bool videoModeNew = AIPortraitsManager.IsVideoMode(pawn);

            string newLabel   = videoModeNew ? "\u267b Video" : "\u267b New";
            string newTooltip = videoModeNew ? "Regenerate the animated video for this pawn." : "Generate a new portrait using current traits and character vibe.";
            Color  newColor   = videoModeNew ? new Color(0.2f, 0.45f, 0.7f) : new Color(0.2f, 0.55f, 0.35f);

            DrawButton(btnNew, newLabel, newColor, newTooltip);
            if (Widgets.ButtonInvisible(btnNew))
            {
                if (videoModeNew)
                {
                    byte[] imgBytes = AIPortraitsManager.GetActivePortraitBytes(pawn);
                    if (imgBytes != null && imgBytes.Length > 0)
                        AIPortraitsManager.TriggerVideoGeneration(pawn, imgBytes);
                    else
                        Messages.Message("No portrait image available to animate.", MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    AIPortraitsManager.TriggerNewPortraitWithContinuity(pawn);
                }
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

            // Button 3: Gallery
            DrawButton(btnFolder, "🎬 Gallery", new Color(0.35f, 0.28f, 0.5f), "Browse saved portraits and videos for this pawn.");
            if (Widgets.ButtonInvisible(btnFolder))
            {
                Find.WindowStack.Add(new Dialog_PawnGallery(pawn));
            }
        }

        private static void DrawButton(Rect rect, string label, Color bgColor, string tooltip)
        {
            // Hover brightness boost (Jules-bot palette/add-hover-states)
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

    [StaticConstructorOnStartup]
    public static class VideoPlaybackManager
    {
        private static GameObject activeVideoGo;
        private static VideoPlayer activeVideoPlayer;
        private static RenderTexture activeRenderTexture;
        private static string activePawnId;
        private static string activeVideoPath;
        private static MattedSequencePlayer activeSeq;   // non-null when playing a bg-removed PNG sequence

        public static bool IsPlaying(string pawnId)
        {
            if (activePawnId != pawnId) return false;
            if (activeSeq != null) return true;
            return activeVideoPlayer != null && activeVideoPlayer.isPlaying;
        }

        public static Texture GetActiveTexture()
        {
            if (activeSeq != null)
            {
                // Return the matted frame directly (RGBA, alpha intact). Do NOT route it
                // through Graphics.Blit -> RenderTexture: the default blit forces alpha to 1,
                // which turned the transparent background opaque (it showed the clip's flat
                // white background). GUI.DrawTexture alpha-blends a Texture2D directly.
                return activeSeq.CurrentFrame();
            }
            if (activeVideoPlayer != null && activeVideoPlayer.isPlaying)
            {
                return activeRenderTexture;
            }
            return null;
        }

        public static void StartPlayback(string pawnId, string videoPath, string framing)
        {
            // Already showing the correct thing for this clip? Don't tear down and rebuild
            // every frame. The "correct thing" is the matted PNG sequence once it exists,
            // otherwise the raw mp4. (Rebuilding a MattedSequencePlayer every frame froze it
            // on frame 0 and thrashed the disk, because the matted path has no VideoPlayer.)
            if (activePawnId == pawnId && activeVideoPath == videoPath)
            {
                bool matteReady = VideoMatteService.IsMatted(videoPath);
                if (matteReady && activeSeq != null) return;            // correctly playing the matte
                if (!matteReady && activeVideoPlayer != null) return;   // correctly playing the raw mp4
                // otherwise fall through and (re)start with the right source (e.g. raw -> matte)
            }

            StopPlayback();

            try
            {
                activePawnId = pawnId;
                activeVideoPath = videoPath;
                DebugLog.Log("PLAY", "start pawn=" + pawnId + " framing=" + framing + " matte=" + VideoMatteService.IsMatted(videoPath) + " file=" + System.IO.Path.GetFileName(videoPath));

                // Render-texture aspect must match the clip so it isn't squashed:
                // special is 16:9 (landscape); portrait/bodyshot are 9:16 (tall).
                int rtW = (framing == "special") ? 910 : 512;
                int rtH = (framing == "special") ? 512 : 910;

                // If a background-removed sequence exists for this clip (portrait/bodyshot),
                // play that PNG sequence with alpha instead of the original mp4.
                if (VideoMatteService.IsMatted(videoPath))
                {
                    activeSeq = new MattedSequencePlayer(videoPath);
                    if (activeSeq.Valid)
                    {
                        // Matted frames are drawn directly as Texture2D (alpha preserved) —
                        // no RenderTexture needed. See GetActiveTexture.
                        return;
                    }
                    activeSeq = null;   // invalid manifest -> fall back to original mp4
                }

                activeVideoGo = new GameObject("AIPortraits_VideoPlayer_" + pawnId);
                UnityEngine.Object.DontDestroyOnLoad(activeVideoGo);

                activeVideoPlayer = activeVideoGo.AddComponent<VideoPlayer>();
                activeVideoPlayer.playOnAwake = false;

                // Mute BEFORE assigning the URL / preparing — setting audioOutputMode after
                // the source is configured can be too late to suppress the track. Disable
                // audio output entirely and drop all controlled audio tracks.
                activeVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                activeVideoPlayer.controlledAudioTrackCount = 0;

                activeVideoPlayer.isLooping = true;
                activeVideoPlayer.renderMode = VideoRenderMode.RenderTexture;

                // RT aspect per framing (set above) so the clip renders without squashing.
                activeRenderTexture = new RenderTexture(rtW, rtH, 0, RenderTextureFormat.ARGB32);
                activeRenderTexture.Create();

                activeVideoPlayer.targetTexture = activeRenderTexture;
                activeVideoPlayer.url = videoPath;

                activeVideoPlayer.Play();
            }
            catch (Exception ex)
            {
                Log.Error("[Dynamic AI Portraits] Failed to start video playback: " + ex.Message);
                StopPlayback();
            }
        }

        public static void StopPlayback()
        {
            if (activeSeq != null || activeVideoPlayer != null)
                DebugLog.Log("PLAY", "stop pawn=" + (activePawnId ?? "-") + " file=" + (activeVideoPath != null ? System.IO.Path.GetFileName(activeVideoPath) : "-"));
            if (activeSeq != null)
            {
                activeSeq.Dispose();
                activeSeq = null;
            }
            if (activeVideoPlayer != null)
            {
                activeVideoPlayer.Stop();
                activeVideoPlayer = null;
            }
            if (activeRenderTexture != null)
            {
                activeRenderTexture.Release();
                UnityEngine.Object.Destroy(activeRenderTexture);
                activeRenderTexture = null;
            }
            if (activeVideoGo != null)
            {
                UnityEngine.Object.Destroy(activeVideoGo);
                activeVideoGo = null;
            }
            activePawnId = null;
            activeVideoPath = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Gallery Window — in-game browser for saved portraits and videos
    // ─────────────────────────────────────────────────────────────────────────────
    public class Dialog_PawnGallery : Window
    {
        private readonly Pawn pawn;
        private readonly string galleryDir;

        private struct GalleryEntry
        {
            public string path;
            public bool isVideo;
            public Texture2D thumb;
            public bool thumbLoaded;
        }

        private List<GalleryEntry> entries = new List<GalleryEntry>();
        private Vector2 scrollPos;

        // Video preview inside the gallery
        private GameObject galleryVideoGo;
        private VideoPlayer galleryVideoPlayer;
        private RenderTexture galleryRenderTex;
        private string playingVideoPath;

        private const float TileSize = 128f;
        private const float TilePad  = 6f;
        private const float HeaderH  = 36f;
        private const float FooterH  = 34f;

        public override Vector2 InitialSize { get { return new Vector2(700f, 560f); } }

        public Dialog_PawnGallery(Pawn pawn)
        {
            this.pawn  = pawn;
            galleryDir = CacheManager.GetPortraitSaveDirectory(pawn);
            doCloseX   = true;
            doWindowBackground     = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            RefreshEntries();
        }

        private void RefreshEntries()
        {
            entries.Clear();
            if (!System.IO.Directory.Exists(galleryDir)) return;

            var files = new List<string>();
            files.AddRange(System.IO.Directory.GetFiles(galleryDir, "*.png"));
            string[] mp4Files = System.IO.Directory.GetFiles(galleryDir, "*.mp4");
            files.AddRange(mp4Files);
            // Exclude helper reference files
            files.RemoveAll(f => f.Contains("_ref_gear") || f.Contains("_ref_portrait"));
            // Newest first
            files.Sort((a, b) => System.IO.File.GetLastWriteTime(b).CompareTo(System.IO.File.GetLastWriteTime(a)));

            foreach (string f in files)
            {
                bool vid = f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
                GalleryEntry entry = new GalleryEntry { path = f, isVideo = vid };
                if (vid) entry.thumbLoaded = true; // Videos don't have texture thumbnails loaded via ImageConversion
                entries.Add(entry);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Header
            Rect header = new Rect(inRect.x, inRect.y, inRect.width, HeaderH);
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.9f, 0.85f, 1f);
            Widgets.Label(header, "  \ud83c\udfac  " + pawn.LabelShortCap + "  \u2014  Gallery  (" + entries.Count + ")");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;

            // Footer
            Rect footer = new Rect(inRect.x, inRect.yMax - FooterH, inRect.width, FooterH);
            float fBtnW = 130f;
            Rect btnRefresh = new Rect(footer.x, footer.y + 3f, fBtnW, footer.height - 6f);
            Rect btnFolder  = new Rect(footer.x + fBtnW + 6f, footer.y + 3f, fBtnW, footer.height - 6f);
            Rect btnStop    = new Rect(footer.xMax - fBtnW, footer.y + 3f, fBtnW, footer.height - 6f);

            if (Widgets.ButtonText(btnRefresh, "\u21bb Refresh")) RefreshEntries();
            if (Widgets.ButtonText(btnFolder,  "\ud83d\udcc1 Open Folder")) CacheManager.OpenPortraitFolder(pawn);
            if (!string.IsNullOrEmpty(playingVideoPath) && Widgets.ButtonText(btnStop, "\u23f9 Stop Video")) StopGalleryVideo();

            // Scroll area
            Rect scrollOuter = new Rect(inRect.x, inRect.y + HeaderH + 4f, inRect.width,
                                        inRect.height - HeaderH - FooterH - 8f);

            if (entries.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.55f, 0.55f, 0.55f);
                Widgets.Label(scrollOuter, "No saved portraits or videos yet.\nGenerate portraits and use the Gallery to see them here.");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            int cols = Mathf.Max(1, Mathf.FloorToInt((scrollOuter.width - 16f) / (TileSize + TilePad)));
            int rows = Mathf.CeilToInt((float)entries.Count / cols);
            float innerH = rows * (TileSize + TilePad) + TilePad;

            Rect innerRect = new Rect(0, 0, scrollOuter.width - 20f, innerH);
            Widgets.BeginScrollView(scrollOuter, ref scrollPos, innerRect);

            for (int i = 0; i < entries.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Rect tile = new Rect(
                    TilePad + col * (TileSize + TilePad),
                    TilePad + row * (TileSize + TilePad),
                    TileSize, TileSize);

                // Cull off-screen tiles
                if (tile.yMax < scrollPos.y - TileSize || tile.y > scrollPos.y + scrollOuter.height + TileSize)
                    continue;

                DrawTile(tile, i);
            }

            Widgets.EndScrollView();
        }

        private void DrawTile(Rect tile, int idx)
        {
            GalleryEntry e = entries[idx];
            bool isPlayingThis = !string.IsNullOrEmpty(playingVideoPath) && playingVideoPath == e.path;

            // Background
            Widgets.DrawBoxSolid(tile, isPlayingThis
                ? new Color(0.15f, 0.4f, 0.7f, 0.95f)
                : new Color(0.1f, 0.1f, 0.13f, 1f));
            GUI.color = new Color(1f, 1f, 1f, isPlayingThis ? 0.5f : 0.1f);
            Widgets.DrawBox(tile, 1);
            GUI.color = Color.white;

            Rect imgArea   = new Rect(tile.x + 2f, tile.y + 2f, tile.width - 4f, tile.height - 22f);
            Rect labelArea = new Rect(tile.x + 2f, tile.yMax - 21f, tile.width - 4f, 19f);

            if (e.isVideo)
            {
                if (isPlayingThis && galleryRenderTex != null)
                {
                    GUI.DrawTexture(imgArea, galleryRenderTex, ScaleMode.ScaleAndCrop);
                    // Playing indicator
                    GUI.color = new Color(1f, 1f, 1f, 0.75f);
                    Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(new Rect(imgArea.xMax - 22f, imgArea.y + 2f, 20f, 16f), "\u25b6");
                    Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                    GUI.color = Color.white;
                }
                else
                {
                    // Video placeholder
                    Widgets.DrawBoxSolid(imgArea, new Color(0.04f, 0.04f, 0.09f, 1f));
                    Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = new Color(0.45f, 0.65f, 1f, 0.9f);
                    Widgets.Label(imgArea, "\ud83c\udfac\n\u25b6 Play");
                    GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                }

                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.5f, 0.8f, 1f);
                Widgets.Label(labelArea, "VIDEO");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            }
            else
            {
                // Lazy-load thumbnail
                if (!e.thumbLoaded)
                {
                    try
                    {
                        byte[] bytes = System.IO.File.ReadAllBytes(e.path);
                        Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (ImageConversion.LoadImage(t, bytes)) e.thumb = t;
                    }
                    catch { }
                    e.thumbLoaded = true;
                    entries[idx] = e;
                }

                if (e.thumb != null)
                    GUI.DrawTexture(imgArea, e.thumb, ScaleMode.ScaleAndCrop);
                else
                {
                    Widgets.DrawBoxSolid(imgArea, new Color(0.06f, 0.06f, 0.06f));
                    Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.3f, 0.3f, 0.3f);
                    Widgets.Label(imgArea, "?"); GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                }

                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.55f, 0.9f, 0.55f);
                Widgets.Label(labelArea, "IMAGE");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            }

            // Hover + tooltip
            if (Mouse.IsOver(tile))
            {
                Widgets.DrawHighlight(tile);
                TooltipHandler.TipRegion(tile, System.IO.Path.GetFileName(e.path));
            }

            // Click
            if (Widgets.ButtonInvisible(tile))
            {
                if (e.isVideo)
                    PlayGalleryVideo(e.path);
                else if (e.thumb != null)
                    Find.WindowStack.Add(new Dialog_GalleryImagePreview(e.thumb));
            }
        }

        private void PlayGalleryVideo(string path)
        {
            StopGalleryVideo();
            try
            {
                playingVideoPath = path;
                galleryVideoGo = new GameObject("AIPortraits_GalleryVideo");
                UnityEngine.Object.DontDestroyOnLoad(galleryVideoGo);
                galleryVideoPlayer = galleryVideoGo.AddComponent<VideoPlayer>();
                galleryVideoPlayer.isLooping = true;
                galleryVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
                galleryRenderTex = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32);
                galleryRenderTex.Create();
                galleryVideoPlayer.targetTexture = galleryRenderTex;
                galleryVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                galleryVideoPlayer.url = path;
                galleryVideoPlayer.Play();
            }
            catch (Exception ex)
            {
                Log.Error("[Dynamic AI Portraits] Gallery video playback error: " + ex.Message);
                StopGalleryVideo();
            }
        }

        private void StopGalleryVideo()
        {
            if (galleryVideoPlayer != null) { galleryVideoPlayer.Stop(); galleryVideoPlayer = null; }
            if (galleryRenderTex  != null) { galleryRenderTex.Release(); UnityEngine.Object.Destroy(galleryRenderTex); galleryRenderTex = null; }
            if (galleryVideoGo    != null) { UnityEngine.Object.Destroy(galleryVideoGo); galleryVideoGo = null; }
            playingVideoPath = null;
        }

        public override void PostClose()
        {
            base.PostClose();
            StopGalleryVideo();
            foreach (var e in entries)
                if (!e.isVideo && e.thumb != null)
                    UnityEngine.Object.Destroy(e.thumb);
            entries.Clear();
        }
    }

    // ─── Full-size image preview ───────────────────────────────────────────────
    public class Dialog_GalleryImagePreview : Window
    {
        private readonly Texture2D tex;
        public override Vector2 InitialSize { get { return new Vector2(600f, 650f); } }

        public Dialog_GalleryImagePreview(Texture2D tex)
        {
            this.tex = tex;
            doCloseX = true;
            doWindowBackground = true;
            absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float imgSize = Mathf.Min(inRect.width, inRect.height);
            Rect imgRect = new Rect(inRect.x + (inRect.width - imgSize) * 0.5f, inRect.y, imgSize, imgSize);
            if (tex != null)
            {
                Widgets.DrawBoxSolid(imgRect, Color.black);
                GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit);
            }
        }
    }
}