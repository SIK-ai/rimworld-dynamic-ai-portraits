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
                if (inProgress.Contains(mp4Path)) return;   // a matte is already running for this clip
                inProgress.Add(mp4Path);
            }
            try
            {
                // Now that we own this clip, clear any stale/incomplete matte dir (no manifest)
                // left by a prior interrupted run. This MUST happen after the in-progress guard
                // so a concurrent EnsureMatted call (e.g. the per-frame draw trigger) can never
                // delete the folder out from under an actively-running matte.
                string staleDir = MatteDir(mp4Path);
                if (Directory.Exists(staleDir))
                {
                    try { Directory.Delete(staleDir, true); }
                    catch (Exception ex) { Log.Warning("[Dynamic AI Portraits] Could not clear stale matte dir: " + ex.Message); }
                }

                GameObject go = new GameObject("AIPortraits_VideoMatte");
                UnityEngine.Object.DontDestroyOnLoad(go);
                VideoMatteProcessor proc = go.AddComponent<VideoMatteProcessor>();
                proc.Begin(mp4Path, delegate { lock (inProgress) { inProgress.Remove(mp4Path); } });
                if (Prefs.DevMode) Log.Message("[Dynamic AI Portraits] Video matte queued (" + framing + "): " + Path.GetFileName(mp4Path));
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
            int n = keys.Count;
            int npix = width * height;

            // ── Pass 1: matte EVERY frame independently into a per-frame alpha mask. ──
            // We used to take a whole-clip UNION/max alpha to stop the body flickering when
            // u2netp drops the torso on some frames. But the union keeps every pixel the subject
            // occupied in ANY frame, so wherever the subject moves it leaves the background
            // showing — measured at ~4-6% of the frame on real clips ("still some background").
            // Instead we keep per-frame masks and, in pass 2, take only a small ±Window temporal
            // max: that still fills brief u2netp dropouts (stable body, no flicker) while cutting
            // the leftover background to ~0.1%.
            int wpx = width, hpx = height;
            const byte AlphaFloor = 48;   // faint low-confidence alpha below this -> transparent
            if (Prefs.DevMode) Log.Message("[Dynamic AI Portraits] Video matte START: " + n + " frames (per-frame, temporal window) -> " + outDir);
            List<byte[]> alphas = new List<byte[]>(n);
            for (int i = 0; i < n; i++)
            {
                Color32[] frame = captured[keys[i]];
                float[] alpha = null;
                bool threadDone = false;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { alpha = U2NetRemover.ComputeAlpha(frame, wpx, hpx); }
                    catch { alpha = null; }
                    threadDone = true;
                });
                while (!threadDone) yield return null;      // keep the game responsive

                byte[] a = new byte[npix];
                if (alpha != null)
                {
                    int lim = npix < alpha.Length ? npix : alpha.Length;
                    for (int p = 0; p < lim; p++)
                    {
                        byte na = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha[p] * 255f), 0, 255);
                        a[p] = na < AlphaFloor ? (byte)0 : na;   // floor faint background bleed
                    }
                }
                alphas.Add(a);
                if (Prefs.DevMode && (i % 24) == 0) Log.Message("[Dynamic AI Portraits] matte alpha pass " + i + "/" + n);
                yield return null;
            }

            // ── Pass 2: build a clean cutout that KEEPS hands/fingers. ──
            // u2netp detects hands/fingers only intermittently (they're small/thin), so we must
            // NOT punish intermittent detections:
            // (1) Temporal MAX over a ±Window window -> any frame that catches a hand keeps it.
            //     This is what stops hands/fingers vanishing.
            // (2) Reliable "core" = majority vote (the body detected in most frames). Keep only
            //     MAX-blobs that TOUCH the core: hands stay (connected to the body via the arm),
            //     while disconnected false-positive patches are dropped.
            // (3) Defringe the semi-transparent rim (repaint it with neighbouring subject color)
            //     instead of eroding -> the white-contaminated edge goes WITHOUT shrinking the
            //     subject or chewing off fingers. Then a light 1px feather.
            const int Window = 4;           // ±4 (9-frame) temporal MAX: bridges longer u2netp
                                            // dropouts so hands AND clothing/torso that vanish
                                            // for several frames are still recovered.
            const byte BinThresh = 140;     // confidence for the reliable body "core"
            const byte KeepFloor = 48;      // MAX kept above this (matches the pass-1 floor)
            byte[] mx = new byte[npix];
            byte[] core = new byte[npix];
            byte[] keep = new byte[npix];
            byte[] soft = new byte[npix];
            int[] ccVisited = new int[npix];
            int[] ccStack = new int[npix];
            int[] ccComp = new int[npix];
            int written = 0;
            for (int i = 0; i < n; i++)
            {
                // Boundary-stable window: keep the full (2*Window+1)-frame span even at the
                // start/end by SHIFTING instead of clamping. Otherwise the last frames see only a
                // few frames and lose hands/parts that u2netp drops "towards the end" of the clip.
                int lo = i - Window;
                int hi = i + Window;
                if (lo < 0) { hi -= lo; lo = 0; }
                if (hi > n - 1) { lo -= (hi - (n - 1)); hi = n - 1; if (lo < 0) lo = 0; }
                int need = ((hi - lo + 1) / 2) + 1;
                for (int p = 0; p < npix; p++)
                {
                    byte m = 0; int c = 0;
                    for (int j = lo; j <= hi; j++)
                    {
                        byte av = alphas[j][p];
                        if (av > m) m = av;
                        if (av >= BinThresh) c++;
                    }
                    mx[p] = m;
                    core[p] = (c >= need) ? (byte)255 : (byte)0;
                    keep[p] = (m >= KeepFloor) ? (byte)255 : (byte)0;
                }

                // Keep blobs that touch the body core OR are sizeable (a real hand/limb that
                // briefly disconnects when u2netp drops the connecting wrist); drop only tiny
                // disconnected false-positive patches.
                KeepComponentsTouchingCore(keep, core, width, height, ccVisited, ccStack, ccComp, npix / 500);
                for (int p = 0; p < npix; p++) soft[p] = (keep[p] != 0) ? mx[p] : (byte)0;

                Color32[] frame = captured[keys[i]];
                DefringeRGB(frame, soft, width, height, 4);
                byte[] feathered = BoxBlur(soft, width, height, 1);

                int lim = frame.Length < npix ? frame.Length : npix;
                for (int p = 0; p < lim; p++)
                {
                    if (feathered[p] < frame[p].a) frame[p].a = feathered[p];
                }

                Texture2D ft = new Texture2D(width, height, TextureFormat.RGBA32, false);
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

                if (Prefs.DevMode && (i % 24) == 0) Log.Message("[Dynamic AI Portraits] matte write pass " + i + "/" + n);
                yield return null;
            }

            double fps = (vp != null && vp.frameRate > 0f) ? vp.frameRate : 24.0;
            try { File.WriteAllText(Path.Combine(outDir, "manifest.txt"), written + "\n" + fps.ToString("0.###")); }
            catch { }
            if (Prefs.DevMode) Log.Message("[Dynamic AI Portraits] Video matte complete: " + written + " frames -> " + outDir);
            Finish();
        }

        // Keep only the connected blobs (4-connectivity) that OVERLAP the reliable `core`,
        // in-place on `mask`. Hands/extremities stay (connected to the body core via the arm);
        // disconnected false-positive patches are dropped. `visited`/`stack`/`comp` are caller
        // scratch buffers (length width*height) so we don't allocate per frame.
        private static void KeepComponentsTouchingCore(byte[] mask, byte[] core, int w, int h, int[] visited, int[] stack, int[] comp, int minKeepSize)
        {
            int n = w * h;
            for (int i = 0; i < n; i++) visited[i] = 0;
            for (int start = 0; start < n; start++)
            {
                if (mask[start] == 0 || visited[start] != 0) continue;
                int sp = 0, cn = 0; bool touches = false;
                stack[sp++] = start; visited[start] = 1;
                while (sp > 0)
                {
                    int idx = stack[--sp]; comp[cn++] = idx;
                    if (core[idx] != 0) touches = true;
                    int x = idx % w, y = idx / w;
                    if (x > 0)     { int nb = idx - 1; if (mask[nb] != 0 && visited[nb] == 0) { visited[nb] = 1; stack[sp++] = nb; } }
                    if (x < w - 1) { int nb = idx + 1; if (mask[nb] != 0 && visited[nb] == 0) { visited[nb] = 1; stack[sp++] = nb; } }
                    if (y > 0)     { int nb = idx - w; if (mask[nb] != 0 && visited[nb] == 0) { visited[nb] = 1; stack[sp++] = nb; } }
                    if (y < h - 1) { int nb = idx + w; if (mask[nb] != 0 && visited[nb] == 0) { visited[nb] = 1; stack[sp++] = nb; } }
                }
                // Drop a blob only if it neither touches the core NOR is large enough to be a real
                // (briefly-disconnected) hand/limb — i.e. drop only small disconnected patches.
                if (!touches && cn < minKeepSize) for (int c = 0; c < cn; c++) mask[comp[c]] = 0;
            }
        }

        // Edge colour decontamination: repaint every visible-but-not-fully-opaque pixel with the
        // colour of nearby trusted-subject pixels, so the anti-aliased rim can't show the clip's
        // (white) background — without eroding, so hands/fingers are untouched. Grows opaque
        // colour outward `iters` px. Modifies `frame` RGB only; `alpha` is left as-is.
        private static void DefringeRGB(Color32[] frame, byte[] alpha, int w, int h, int iters)
        {
            int n = w * h;
            if (frame.Length < n) return;
            byte[] filled = new byte[n];
            byte[] r = new byte[n]; byte[] g = new byte[n]; byte[] b = new byte[n];
            for (int i = 0; i < n; i++)
            {
                r[i] = frame[i].r; g[i] = frame[i].g; b[i] = frame[i].b;
                filled[i] = (byte)(alpha[i] >= 200 ? 1 : 0);
            }
            for (int pass = 0; pass < iters; pass++)
            {
                byte[] nf = (byte[])filled.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        if (filled[idx] != 0 || alpha[idx] == 0) continue;   // already clean, or fully transparent
                        int sr = 0, sg = 0, sb = 0, cnt = 0;
                        if (x > 0     && filled[idx - 1] != 0) { sr += r[idx - 1]; sg += g[idx - 1]; sb += b[idx - 1]; cnt++; }
                        if (x < w - 1 && filled[idx + 1] != 0) { sr += r[idx + 1]; sg += g[idx + 1]; sb += b[idx + 1]; cnt++; }
                        if (y > 0     && filled[idx - w] != 0) { sr += r[idx - w]; sg += g[idx - w]; sb += b[idx - w]; cnt++; }
                        if (y < h - 1 && filled[idx + w] != 0) { sr += r[idx + w]; sg += g[idx + w]; sb += b[idx + w]; cnt++; }
                        if (cnt > 0) { r[idx] = (byte)(sr / cnt); g[idx] = (byte)(sg / cnt); b[idx] = (byte)(sb / cnt); nf[idx] = 1; }
                    }
                filled = nf;
            }
            for (int i = 0; i < n; i++) { frame[i].r = r[i]; frame[i].g = g[i]; frame[i].b = b[i]; }
        }

        // Separable box blur (radius r) on an 8-bit mask -> fresh buffer. Feathers the alpha edge.
        private static byte[] BoxBlur(byte[] src, int w, int h, int r)
        {
            int n = w * h;
            int win = 2 * r + 1;
            int[] acc = new int[n];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = 0;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx; if (nx < 0) nx = 0; else if (nx >= w) nx = w - 1;
                        s += src[y * w + nx];
                    }
                    acc[y * w + x] = s / win;
                }
            byte[] outm = new byte[n];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = 0;
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int ny = y + dy; if (ny < 0) ny = 0; else if (ny >= h) ny = h - 1;
                        s += acc[ny * w + x];
                    }
                    outm[y * w + x] = (byte)(s / win);
                }
            return outm;
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
