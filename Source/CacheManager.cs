using System;
using System.IO;
using UnityEngine;
using Verse;

namespace AIPortraits
{
    public static class CacheManager
    {
        // ── AUTO-CACHE (hidden, hash-keyed) ──────────────────────────────────────
        // Used for automatic background generation / state-change caching.

        public static string GetCacheDirectory()
        {
            string path = Path.Combine(Application.persistentDataPath, "AIPortraitsCache");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        public static string GetFilePath(string stateHash)
        {
            return Path.Combine(GetCacheDirectory(), stateHash + ".png");
        }

        public static bool IsCached(string stateHash)
        {
            if (string.IsNullOrEmpty(stateHash)) return false;
            return File.Exists(GetFilePath(stateHash));
        }

        public static Texture2D LoadFromCache(string stateHash)
        {
            if (!IsCached(stateHash)) return null;

            try
            {
                byte[] bytes = File.ReadAllBytes(GetFilePath(stateHash));
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, bytes))
                    return texture;
            }
            catch (Exception ex)
            {
                Log.Error("[Dynamic AI Portraits] Failed to load image from cache: " + ex.Message);
            }
            return null;
        }

        public static void SaveToCache(string stateHash, byte[] bytes)
        {
            try
            {
                File.WriteAllBytes(GetFilePath(stateHash), bytes);
            }
            catch (Exception ex)
            {
                Log.Error("[Dynamic AI Portraits] Failed to write image to cache: " + ex.Message);
            }
        }

        // ── LOCAL PORTRAIT SAVE (user-visible, named files) ───────────────────────
        // Saves to: Documents/RimWorld Portraits/{PawnName}/
        // Named:    {PawnName}_{Style}_{Timestamp}.png

        public static string GetPortraitSaveDirectory(string pawnName)
        {
            // Sanitize pawn name for use as folder name
            string safeName = SanitizeFileName(pawnName);

            // Use Documents folder
            string docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            string path = Path.Combine(docs, "RimWorld Portraits", safeName);

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            return path;
        }

        /// <summary>
        /// Saves the portrait PNG to Documents/RimWorld Portraits/{pawnName}/.
        /// Returns the full file path on success, null on failure.
        /// </summary>
        public static string SavePortraitToDisk(string pawnName, PortraitStyle style, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                Log.Warning("[Dynamic AI Portraits] SavePortraitToDisk: bytes are null or empty.");
                return null;
            }

            try
            {
                string dir  = GetPortraitSaveDirectory(pawnName);
                string ts   = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string suf  = style.ToString();
                string file = SanitizeFileName(pawnName) + "_" + suf + "_" + ts + ".png";
                string path = Path.Combine(dir, file);

                File.WriteAllBytes(path, bytes);
                Log.Message("[Dynamic AI Portraits] Portrait saved to: " + path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("[Dynamic AI Portraits] Failed to save portrait to disk: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Opens the portrait folder for this pawn in the OS file browser.
        /// </summary>
        public static void OpenPortraitFolder(string pawnName)
        {
            try
            {
                OpenInFileExplorer(GetPortraitSaveDirectory(pawnName));
            }
            catch (Exception ex)
            {
                Log.Warning("[Dynamic AI Portraits] Failed to open folder in explorer: " + ex.Message);
            }
        }

        /// <summary>Cross-platform "reveal in file browser" helper.</summary>
        public static void OpenInFileExplorer(string path)
        {
            try
            {
                switch (UnityEngine.Application.platform)
                {
                    case UnityEngine.RuntimePlatform.WindowsPlayer:
                    case UnityEngine.RuntimePlatform.WindowsEditor:
                        System.Diagnostics.Process.Start("explorer.exe", path);
                        break;
                    case UnityEngine.RuntimePlatform.OSXPlayer:
                    case UnityEngine.RuntimePlatform.OSXEditor:
                        System.Diagnostics.Process.Start("open", path);
                        break;
                    case UnityEngine.RuntimePlatform.LinuxPlayer:
                    case UnityEngine.RuntimePlatform.LinuxEditor:
                        System.Diagnostics.Process.Start("xdg-open", path);
                        break;
                    default:
                        System.Diagnostics.Process.Start(path);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Dynamic AI Portraits] Failed to open path '" + path + "': " + ex.Message);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            // Replace path-traversal sequences defensively — Path.GetInvalidFileNameChars()
            // doesn't include '.' so "../etc" would otherwise survive.
            name = name.Replace("..", "_");
            char[] invalid = Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                bool isInvalid = false;
                for (int i = 0; i < invalid.Length; i++)
                    if (invalid[i] == c) { isInvalid = true; break; }
                sb.Append(isInvalid ? '_' : c);
            }
            string result = sb.ToString().Trim('.', ' ');
            return string.IsNullOrEmpty(result) ? "Unknown" : result;
        }
    }
}
