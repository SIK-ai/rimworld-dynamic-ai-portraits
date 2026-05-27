using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Video;
using Verse;

namespace AIPortraits
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  VIDEO BACKGROUND REMOVAL  (FIRST DRAFT — NEEDS IN-GAME VALIDATION)
    //
    //  Strategy: u2netp at ~0.3s/frame can't matte in real time (24fps), so we
    //  pre-process a downloaded Veo clip ONCE into a transparent PNG sequence, then
    //  play that sequence back. Only portrait/bodyshot are matted; "special" keeps
    //  its scenic background and continues to play as the original mp4.
    //
    //  Two parts:
    //    VideoMatteService    — decides whether/when to matte; tracks state.
    //    VideoMatteProcessor  — a temporary MonoBehaviour that drives a VideoPlayer
    //                           frame-by-frame, mattes each frame, writes PNGs.
    //    MattedSequencePlayer — plays the resulting PNG sequence (alpha) by time.
    //
    //  NOTE: ONNX frame matting runs on a background thread; all VideoPlayer / texture
    //  access stays on the main thread. This whole file is untested in-engine.
    // ─────────────────────────────────────────────────────────────────────────────

    public static class VideoMatteService
    {
        private static readonly HashSet<string> inProgress = new HashSet<string>();

        public static string MatteDir(string mp4Path)
        {
            return mp4Path + "_matte";
        }

        public static bool IsMatted(string mp4Path)
        {
            return File.Exists(Path.Combine(MatteDir(mp4Path), "manifest.txt"));
        }

        /// <summary>
        /// Kick off background-removal for a freshly-downloaded clip, if appropriate.
        /// Safe to call repeatedly; no-ops for special framing, when ONNX is unavailable,
        /// or when already done / running.
        /// </summary>
        public static void EnsureMatted(string mp4Path, string framing)
        {
            if (string.IsNullOrEmpty(mp4Path) || !File.Exists(mp4Path)) return;
            if (framing == "special") return;            // keep background for special shots
            if (!U2NetRemover.Available) return;          // ONNX not loaded -> leave video as-is
            if (IsMatted(mp4Path)) return;
            lock (inProgress)
            {
                if (inProgress.Contains(mp4Path)) return;
                inProgress.Add(mp4Path);
            }
            try
            {
                GameObject go = new GameObject("AIPortraits_VideoMatte");
                UnityEngine.Object.DontDestroyOnLoad(go);
                VideoMatteProcessor proc = go.AddComponent<VideoMatteProcessor>();
                proc.Begin(mp4Path, delegate { lock (inProgress) { inProgress.Remove(mp4Path); } });
            }
            catch (Exception ex)
            {
                lock (inProgress) { inProgress.Remove(mp4Path); }
                Log.Warning("[Dynamic AI Portraits] Video matte start failed: " + ex.Message);
            }
        }
    }

    public class VideoMatteProcessor : MonoBehaviour
    {
        private string mp4Path;
        private string outDir;
        private Action onDone;
        private VideoPlayer vp;
        private RenderTexture rt;
        private Texture2D readback;
        private readonly Dictionary<long, Color32[]> captured = new Dictionary<long, Color32[]>();
        private int width, height;
        private long frameCount;
        private bool started;

        public void Begin(string path, Action done)
        {
            mp4Path = path;
            outDir = VideoMatteService.MatteDir(path);
            onDone = done;
            try { Directory.CreateDirectory(outDir); } catch { }

            vp = gameObject.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.isLooping = false;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.url = mp4Path;
            vp.sendFrameReadyEvents = true;
            vp.frameReady += OnFrameReady;
            vp.prepareCompleted += OnPrepared;
            vp.loopPointReached += OnEnded;
            vp.errorReceived += OnError;
            vp.Prepare();
        }

        private void OnPrepared(VideoPlayer src)
        {
            width = (int)src.width;
            height = (int)src.height;
            frameCount = (long)src.frameCount;
            rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            src.targetTexture = rt;
            readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
            started = true;
            src.Play();   // play through once; frameReady fires per decoded frame
        }

        private void OnFrameReady(VideoPlayer src, long frameIdx)
        {
            if (!started || captured.ContainsKey(frameIdx)) return;
            try
            {
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readback.Apply(false);
                RenderTexture.active = prev;
                captured[frameIdx] = readback.GetPixels32();   // copy out (main thread)
            }
            catch (Exception ex)
            {
                Log.Warning("[Dynamic AI Portraits] frame capture failed @" + frameIdx + ": " + ex.Message);
            }
        }

        private void OnEnded(VideoPlayer src)
        {
            src.frameReady -= OnFrameReady;
            StartCoroutine(MatteAndWrite());
        }

        private void OnError(VideoPlayer src, string message)
        {
            Log.Warning("[Dynamic AI Portraits] VideoPlayer error during matte: " + message);
            Finish();
        }

        private IEnumerator MatteAndWrite()
        {
            // Snapshot the captured frames in index order.
            List<long> keys = new List<long>(captured.Keys);
            keys.Sort();

            int written = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                Color32[] frame = captured[keys[i]];

                // Matte on a background thread (ONNX inference is CPU-only, no Unity calls).
                float[] alpha = null;
                bool threadDone = false;
                int w = width, h = height;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { alpha = U2NetRemover.ComputeAlpha(frame, w, h); }
                    catch { alpha = null; }
                    threadDone = true;
                });
                while (!threadDone) yield return null;      // keep the game responsive

                if (alpha != null)
                {
                    for (int p = 0; p < frame.Length; p++)
                    {
                        byte na = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha[p] * 255f), 0, 255);
                        if (na < frame[p].a) frame[p].a = na;
                    }
                }

                // Encode + write (main thread for Texture2D, but file IO is cheap).
                Texture2D ft = new Texture2D(w, h, TextureFormat.RGBA32, false);
                ft.SetPixels32(frame);
                ft.Apply(false);
                byte[] png = ImageConversion.EncodeToPNG(ft);
                UnityEngine.Object.Destroy(ft);
                try
                {
                    File.WriteAllBytes(Path.Combine(outDir, "f" + written.ToString("D5") + ".png"), png);
                    written++;
                }
                catch (Exception ex) { Log.Warning("[Dynamic AI Portraits] frame write failed: " + ex.Message); }

                yield return null;
            }

            double fps = (vp != null && vp.frameRate > 0f) ? vp.frameRate : 24.0;
            try { File.WriteAllText(Path.Combine(outDir, "manifest.txt"), written + "\n" + fps.ToString("0.###")); }
            catch { }
            Log.Message("[Dynamic AI Portraits] Video matte complete: " + written + " frames -> " + outDir);
            Finish();
        }

        private void Finish()
        {
            try { if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); } } catch { }
            try { if (readback != null) UnityEngine.Object.Destroy(readback); } catch { }
            captured.Clear();
            Action cb = onDone; onDone = null;
            if (cb != null) cb();
            UnityEngine.Object.Destroy(gameObject);
        }
    }

    /// <summary>
    /// Plays a matted PNG sequence (with alpha) produced by VideoMatteProcessor, looping
    /// by wall-clock time. Frames are decoded from disk on demand and cached in a small ring.
    /// </summary>
    public class MattedSequencePlayer
    {
        private readonly string dir;
        private readonly int count;
        private readonly double fps;
        private float startTime;
        private readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();
        private readonly Queue<int> cacheOrder = new Queue<int>();
        private const int CacheMax = 24;

        public MattedSequencePlayer(string mp4Path)
        {
            dir = VideoMatteService.MatteDir(mp4Path);
            string[] lines = File.ReadAllLines(Path.Combine(dir, "manifest.txt"));
            int.TryParse(lines[0].Trim(), out count);
            double f; double.TryParse(lines.Length > 1 ? lines[1].Trim() : "24", out f);
            fps = f > 0 ? f : 24.0;
            startTime = Time.realtimeSinceStartup;
        }

        public bool Valid { get { return count > 0; } }

        public Texture2D CurrentFrame()
        {
            if (count <= 0) return null;
            double elapsed = Time.realtimeSinceStartup - startTime;
            int idx = (int)(elapsed * fps) % count;
            if (idx < 0) idx += count;
            return LoadFrame(idx);
        }

        private Texture2D LoadFrame(int idx)
        {
            Texture2D t;
            if (cache.TryGetValue(idx, out t) && t != null) return t;
            try
            {
                string path = Path.Combine(dir, "f" + idx.ToString("D5") + ".png");
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(t, bytes)) { UnityEngine.Object.Destroy(t); return null; }
                cache[idx] = t;
                cacheOrder.Enqueue(idx);
                while (cacheOrder.Count > CacheMax)
                {
                    int old = cacheOrder.Dequeue();
                    Texture2D ot;
                    if (old != idx && cache.TryGetValue(old, out ot)) { cache.Remove(old); if (ot != null) UnityEngine.Object.Destroy(ot); }
                }
                return t;
            }
            catch { return null; }
        }

        public void Dispose()
        {
            foreach (Texture2D t in cache.Values) if (t != null) UnityEngine.Object.Destroy(t);
            cache.Clear(); cacheOrder.Clear();
        }
    }
}
