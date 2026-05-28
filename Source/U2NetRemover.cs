using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AIPortraits
{
    /// <summary>
    /// Local, offline background removal using the u2netp ONNX model via ONNX Runtime.
    /// Replaces the YCbCr flood-fill for portrait/bodyshot framings. If the native runtime
    /// or model fails to load (e.g. ONNX Runtime not supported under this Mono build),
    /// every entry point transparently falls back to the legacy BackgroundRemover, so the
    /// mod degrades gracefully instead of breaking.
    /// </summary>
    public static class U2NetRemover
    {
        private const int ModelSize = 320;
        private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] Std  = { 0.229f, 0.224f, 0.225f };

        private static InferenceSession session;
        private static string inputName;
        private static bool initTried;
        private static bool initOk;
        private static readonly object initLock = new object();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        public static bool Available
        {
            get { EnsureInit(); return initOk; }
        }

        private static void EnsureInit()
        {
            if (initTried) return;
            lock (initLock)
            {
                if (initTried) return;
                initTried = true;
                try
                {
                    // Help Mono's P/Invoke find the native onnxruntime.dll that ships next to our managed DLL.
                    string asmDir = null;
                    try { asmDir = Path.GetDirectoryName(typeof(U2NetRemover).Assembly.Location); } catch { }
                    if (!string.IsNullOrEmpty(asmDir))
                    {
                        SetDllDirectory(asmDir);
                        string nativePath = Path.Combine(asmDir, "onnxruntime.dll");
                        if (File.Exists(nativePath)) LoadLibrary(nativePath);
                    }

                    string modelPath = ResolveModelPath(asmDir);
                    if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                    {
                        Log.Warning("[Dynamic AI Portraits] u2netp model not found; falling back to legacy background remover.");
                        return;
                    }

                    SessionOptions opts = new SessionOptions();
                    opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    session = new InferenceSession(modelPath, opts);
                    foreach (string k in session.InputMetadata.Keys) { inputName = k; break; }
                    initOk = !string.IsNullOrEmpty(inputName);
                    if (initOk)
                        Log.Message("[Dynamic AI Portraits] u2netp ONNX background remover initialized.");
                }
                catch (Exception ex)
                {
                    initOk = false;
                    Log.Warning("[Dynamic AI Portraits] u2netp init failed (" + ex.GetType().Name + ": " + ex.Message +
                                "); falling back to legacy background remover.");
                }
            }
        }

        private static string ResolveModelPath(string asmDir)
        {
            try
            {
                if (AIPortraitsMod.Instance != null && AIPortraitsMod.Instance.Content != null)
                {
                    string p = Path.Combine(Path.Combine(AIPortraitsMod.Instance.Content.RootDir, "Models"), "u2netp.onnx");
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(asmDir))
            {
                string p1 = Path.Combine(Path.Combine(asmDir, "Models"), "u2netp.onnx");
                if (File.Exists(p1)) return p1;
                DirectoryInfo parent = Directory.GetParent(asmDir);
                if (parent != null)
                {
                    string p2 = Path.Combine(Path.Combine(parent.FullName, "Models"), "u2netp.onnx");
                    if (File.Exists(p2)) return p2;
                }
            }
            return null;
        }

        /// <summary>
        /// Remove the background from a still portrait. Returns a new RGBA texture with the
        /// background made transparent, or the legacy remover's result if ONNX is unavailable.
        /// </summary>
        public static Texture2D Process(Texture2D source)
        {
            if (source == null) return null;
            EnsureInit();
            if (!initOk) return BackgroundRemover.Process(source);

            try
            {
                int w = source.width, h = source.height;
                Color32[] px = source.GetPixels32();
                float[] alpha = ComputeAlpha(px, w, h);
                if (alpha == null) return BackgroundRemover.Process(source);

                // The raw u2netp mask under-segments these generated images and shaves off thin
                // subject parts (arms, clothing edges). Grow the foreground mask a few pixels so
                // the matte keeps the whole character — a thin background halo is far better than
                // amputated arms/clothes. (Images only; the video pipeline calls ComputeAlpha
                // directly and does its own temporal-window handling.)
                alpha = DilateMask(alpha, w, h, 8);

                for (int i = 0; i < px.Length; i++)
                {
                    byte na = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha[i] * 255f), 0, 255);
                    if (na < px[i].a) px[i].a = na;   // only ever remove opacity, never add
                }
                Texture2D outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                outTex.SetPixels32(px);
                outTex.Apply();
                return outTex;
            }
            catch (Exception ex)
            {
                Log.Warning("[Dynamic AI Portraits] u2netp Process failed (" + ex.Message + "); using legacy remover.");
                return BackgroundRemover.Process(source);
            }
        }

        // Separable max-dilation of a 0..1 alpha mask by 'r' pixels — grows the foreground so
        // background removal does not shave off thin subject parts. Square structuring element.
        private static float[] DilateMask(float[] a, int w, int h, int r)
        {
            if (a == null || r <= 0 || w <= 0 || h <= 0) return a;
            float[] tmp = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int x0 = x - r; if (x0 < 0) x0 = 0;
                    int x1 = x + r; if (x1 > w - 1) x1 = w - 1;
                    float m = 0f;
                    for (int xx = x0; xx <= x1; xx++) { float v = a[row + xx]; if (v > m) m = v; }
                    tmp[row + x] = m;
                }
            }
            float[] outp = new float[w * h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int y0 = y - r; if (y0 < 0) y0 = 0;
                    int y1 = y + r; if (y1 > h - 1) y1 = h - 1;
                    float m = 0f;
                    for (int yy = y0; yy <= y1; yy++) { float v = tmp[yy * w + x]; if (v > m) m = v; }
                    outp[y * w + x] = m;
                }
            }
            return outp;
        }

        /// <summary>
        /// Core inference: returns a per-pixel foreground alpha (0..1) sized w*h, in the same
        /// row order as Color32[] from GetPixels32. Touches no Unity objects, so it is safe to
        /// call from a background thread (the video pipeline relies on this). Returns null if
        /// ONNX is unavailable or inputs are malformed.
        /// </summary>
        public static float[] ComputeAlpha(Color32[] px, int w, int h)
        {
            EnsureInit();
            if (!initOk || px == null || w <= 0 || h <= 0 || px.Length != w * h) return null;

            float[] sr = new float[ModelSize * ModelSize];
            float[] sg = new float[ModelSize * ModelSize];
            float[] sb = new float[ModelSize * ModelSize];
            float maxv = 1f;
            for (int y = 0; y < ModelSize; y++)
            {
                float fy = (y + 0.5f) * h / ModelSize - 0.5f;
                for (int x = 0; x < ModelSize; x++)
                {
                    float fx = (x + 0.5f) * w / ModelSize - 0.5f;
                    Color32 c = SampleBilinear(px, w, h, fx, fy);
                    int idx = y * ModelSize + x;
                    sr[idx] = c.r; sg[idx] = c.g; sb[idx] = c.b;
                    if (c.r > maxv) maxv = c.r;
                    if (c.g > maxv) maxv = c.g;
                    if (c.b > maxv) maxv = c.b;
                }
            }

            DenseTensor<float> input = new DenseTensor<float>(new int[] { 1, 3, ModelSize, ModelSize });
            for (int y = 0; y < ModelSize; y++)
            {
                for (int x = 0; x < ModelSize; x++)
                {
                    int idx = y * ModelSize + x;
                    input[0, 0, y, x] = (sr[idx] / maxv - Mean[0]) / Std[0];
                    input[0, 1, y, x] = (sg[idx] / maxv - Mean[1]) / Std[1];
                    input[0, 2, y, x] = (sb[idx] / maxv - Mean[2]) / Std[2];
                }
            }

            float[] mask = new float[ModelSize * ModelSize];
            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>();
            inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, input));
            using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs))
            {
                DisposableNamedOnnxValue first = null;
                foreach (DisposableNamedOnnxValue r in results) { first = r; break; }
                if (first == null) return null;
                Tensor<float> outT = first.AsTensor<float>();
                float mn = float.MaxValue, mx = float.MinValue;
                int k = 0;
                for (int y = 0; y < ModelSize; y++)
                {
                    for (int x = 0; x < ModelSize; x++)
                    {
                        float v = outT[0, 0, y, x];
                        mask[k++] = v;
                        if (v < mn) mn = v;
                        if (v > mx) mx = v;
                    }
                }
                float range = mx - mn;
                if (range < 1e-6f) range = 1e-6f;
                for (int i = 0; i < mask.Length; i++) mask[i] = (mask[i] - mn) / range;
            }

            float[] alpha = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                float my = (y + 0.5f) * ModelSize / h - 0.5f;
                for (int x = 0; x < w; x++)
                {
                    float mx2 = (x + 0.5f) * ModelSize / w - 0.5f;
                    alpha[y * w + x] = SampleMaskBilinear(mask, ModelSize, ModelSize, mx2, my);
                }
            }
            return alpha;
        }

        private static Color32 SampleBilinear(Color32[] px, int w, int h, float fx, float fy)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, h - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1);
            int y1 = Mathf.Min(y0 + 1, h - 1);
            float dx = Mathf.Clamp01(fx - x0);
            float dy = Mathf.Clamp01(fy - y0);
            Color32 c00 = px[y0 * w + x0], c10 = px[y0 * w + x1], c01 = px[y1 * w + x0], c11 = px[y1 * w + x1];
            float r = Lerp2(c00.r, c10.r, c01.r, c11.r, dx, dy);
            float g = Lerp2(c00.g, c10.g, c01.g, c11.g, dx, dy);
            float b = Lerp2(c00.b, c10.b, c01.b, c11.b, dx, dy);
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        private static float SampleMaskBilinear(float[] m, int w, int h, float fx, float fy)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, h - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1);
            int y1 = Mathf.Min(y0 + 1, h - 1);
            float dx = Mathf.Clamp01(fx - x0);
            float dy = Mathf.Clamp01(fy - y0);
            return Lerp2(m[y0 * w + x0], m[y0 * w + x1], m[y1 * w + x0], m[y1 * w + x1], dx, dy);
        }

        private static float Lerp2(float v00, float v10, float v01, float v11, float dx, float dy)
        {
            float a = v00 + (v10 - v00) * dx;
            float b = v01 + (v11 - v01) * dx;
            return a + (b - a) * dy;
        }
    }
}
