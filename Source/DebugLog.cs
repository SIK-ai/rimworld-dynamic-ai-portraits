using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Verse;

namespace AIPortraits
{
    /// <summary>
    /// Opt-in structured debug logger (toggled in mod settings → "Debug Mode").
    ///
    /// When enabled it writes a timestamped, per-session log to
    /// <c>AIPortraitsCache/DebugLogs/session_yyyyMMdd_HHmmss.log</c> capturing the metadata
    /// needed to backtrack the awkward bugs ("all UI vanished", "API call failed",
    /// "wrong video shown"): FSM transitions, API calls + fallbacks, overlay-draw decisions,
    /// and video-playback decisions.
    ///
    /// Every line is flushed to disk immediately (File.AppendAllText), so the log survives a
    /// crash — useful since "UI gone" sometimes precedes one. The whole class is a near-zero-cost
    /// no-op when the toggle is off, and is safe to call from background threads (the matte
    /// ONNX worker) thanks to a single lock.
    /// </summary>
    public static class DebugLog
    {
        private static readonly object gate = new object();
        private static string sessionFile;
        private static long bytesWritten;
        private const long MaxBytes = 8 * 1024 * 1024;   // 8 MB cap per session file
        private const int KeepSessions = 15;             // keep the last N session logs

        // Per-key last message, so per-frame call sites (draw/playback) only log on CHANGE
        // instead of flooding the file 60x/second.
        private static readonly Dictionary<string, string> lastByKey = new Dictionary<string, string>();

        public static bool Enabled
        {
            get
            {
                try { return AIPortraitsMod.settings != null && AIPortraitsMod.settings.debugMode; }
                catch { return false; }
            }
        }

        public static string LogFolder()
        {
            return Path.Combine(CacheManager.GetCacheDirectory(), "DebugLogs");
        }

        /// <summary>
        /// Writes a session header with a settings + environment snapshot. Safe to call
        /// repeatedly (e.g. mod init, then again when a save loads).
        /// </summary>
        public static void Begin(string reason)
        {
            if (!Enabled) return;
            lock (gate)
            {
                try
                {
                    EnsureSession();
                    WriteRaw("==== " + reason + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                    AIPortraitsSettings s = AIPortraitsMod.settings;
                    if (s != null)
                    {
                        WriteRaw("  style=" + s.portraitStyle + "  backend=" + s.backendType +
                                 "  llmModel=" + s.llmModelType + "  aiBgRemoval=" + s.useAIBgRemoval);
                        WriteRaw("  keys: prompt=" + Has(s.llmApiKey) + " cloudflare=" + Has(s.cfApiKey) +
                                 " google=" + Has(s.giApiKey) + " video=" + Has(s.videoApiKey));
                    }
                    WriteRaw("  u2netp(ONNX) available=" + SafeOnnx());
                }
                catch (Exception ex) { TryWrite("  (debug header error: " + ex.Message + ")"); }
            }
        }

        /// <summary>Log an event under a category, e.g. Log("FSM", "image GEN start ...").</summary>
        public static void Log(string category, string message)
        {
            if (!Enabled) return;
            lock (gate)
            {
                try { EnsureSession(); WriteRaw(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + category + "] " + message); }
                catch (System.Exception ex) { if (Verse.Prefs.DevMode) Verse.Log.Warning("[Dynamic AI Portraits] Silent exception: " + ex.Message); }
            }
        }

        /// <summary>
        /// Logs only when <paramref name="message"/> differs from the last message recorded for
        /// <paramref name="dedupKey"/>. Use at per-frame call sites (overlay draw, playback) so
        /// only state CHANGES are recorded — exactly what's useful for backtracking.
        /// </summary>
        public static void LogOnChange(string dedupKey, string category, string message)
        {
            if (!Enabled) return;
            lock (gate)
            {
                string prev;
                if (lastByKey.TryGetValue(dedupKey, out prev) && prev == message) return;
                lastByKey[dedupKey] = message;
                try { EnsureSession(); WriteRaw(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + category + "] " + message); }
                catch (System.Exception ex) { if (Verse.Prefs.DevMode) Verse.Log.Warning("[Dynamic AI Portraits] Silent exception: " + ex.Message); }
            }
        }

        private static string Has(string k) { return string.IsNullOrEmpty(k) ? "no" : "yes"; }

        private static string SafeOnnx()
        {
            try { return U2NetRemover.Available.ToString(); }
            catch { return "?"; }
        }

        // ── internals (all called under `gate`) ──────────────────────────────────────
        private static void EnsureSession()
        {
            if (sessionFile != null) return;
            string dir = LogFolder();
            Directory.CreateDirectory(dir);
            sessionFile = Path.Combine(dir, "session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            bytesWritten = 0;
            Prune(dir);
        }

        private static void WriteRaw(string line)
        {
            if (sessionFile == null) return;
            if (bytesWritten > MaxBytes) return;   // cap reached — stop appending
            File.AppendAllText(sessionFile, line + Environment.NewLine);
            bytesWritten += Encoding.UTF8.GetByteCount(line) + 2;
            if (bytesWritten > MaxBytes)
                File.AppendAllText(sessionFile, "... [debug log size cap reached; further entries suppressed] ..." + Environment.NewLine);
        }

        private static void TryWrite(string line) { try { WriteRaw(line); } catch (System.Exception ex) { if (Verse.Prefs.DevMode) Verse.Log.Warning("[Dynamic AI Portraits] Silent exception: " + ex.Message); } }

        private static void Prune(string dir)
        {
            try
            {
                List<string> files = new List<string>(Directory.GetFiles(dir, "session_*.log"));
                files.Sort();   // names are timestamp-sortable
                while (files.Count > KeepSessions)
                {
                    try { File.Delete(files[0]); } catch (System.Exception ex) { if (Verse.Prefs.DevMode) Verse.Log.Warning("[Dynamic AI Portraits] Silent exception: " + ex.Message); }
                    files.RemoveAt(0);
                }
            }
            catch (System.Exception ex) { if (Verse.Prefs.DevMode) Verse.Log.Warning("[Dynamic AI Portraits] Silent exception: " + ex.Message); }
        }
    }
}
