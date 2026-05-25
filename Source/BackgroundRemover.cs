using System.Collections.Generic;
using UnityEngine;

namespace AIPortraits
{
    /// <summary>
    /// Removes a near-uniform background from a generated portrait using an advanced
    /// perceptual YCbCr flood-fill algorithm. Features:
    ///   - Perceptual conversion to YCbCr (separating Luma from Chroma).
    ///   - Multi-peak edge color profiling (extracts up to 3 dominant background shades).
    ///   - Core zone guard (face protection area).
    ///   - Human skin tone protection.
    ///   - Vibrant color preservation.
    ///   - Two-pass flood fill (strict propagation + loose depth-limited halo cleanup).
    /// </summary>
    public static class BackgroundRemover
    {
        private const float ChromaStrict = 8f;
        private const float LumaStrict = 25f;

        private const float ChromaLoose = 12f;
        private const float LumaLoose = 40f;

        private const int HaloMaxDepth = 3;
        private const float MaxRemovedFraction = 0.80f;
        private const byte AlreadyTransparentAlpha = 10;

        private struct YCbCrColor
        {
            public float Y;
            public float Cb;
            public float Cr;

            public YCbCrColor(Color32 color)
            {
                Y  =  0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
                Cb = -0.168736f * color.r - 0.331264f * color.g + 0.5f * color.b + 128f;
                Cr =  0.5f * color.r - 0.418688f * color.g - 0.081312f * color.b + 128f;
            }
        }

        // Precomputed state for the flood-fill process to avoid expensive per-pixel operations.
        private struct FloodContext
        {
            public int width;
            public int coreMinX;
            public int coreMaxX;
            public int coreMinY;
            public int coreMaxY;
            public bool bgIsSkinLike;
            public bool bgIsSaturated;
        }

        public static Texture2D Process(Texture2D source)
        {
            if (source == null) return null;

            int w = source.width;
            int h = source.height;
            Color32[] pixels = source.GetPixels32();

            // Strict early-out: image must not already be transparent at all 4 corners
            if (pixels[0].a              < AlreadyTransparentAlpha &&
                pixels[w - 1].a          < AlreadyTransparentAlpha &&
                pixels[(h - 1) * w].a    < AlreadyTransparentAlpha &&
                pixels[h * w - 1].a      < AlreadyTransparentAlpha)
            {
                return source;
            }

            // Sample up to 3 dominant background colors from edges
            List<YCbCrColor> bgColors = SampleDominantEdgeColors(pixels, w, h);
            if (bgColors.Count == 0) return source;

            // Precompute context
            FloodContext ctx = new FloodContext();
            ctx.width = w;
            ctx.coreMinX = (int)(w * 0.28f);
            ctx.coreMaxX = (int)(w * 0.72f);
            ctx.coreMinY = (int)(h * 0.32f);
            ctx.coreMaxY = (int)(h * 0.82f);

            ctx.bgIsSkinLike = false;
            ctx.bgIsSaturated = false;
            foreach (var bg in bgColors)
            {
                if (bg.Cb >= 95f && bg.Cb <= 126f && bg.Cr >= 130f && bg.Cr <= 165f)
                {
                    ctx.bgIsSkinLike = true;
                }
                float bgSat = System.Math.Abs(bg.Cb - 128f) + System.Math.Abs(bg.Cr - 128f);
                if (bgSat > 20f)
                {
                    ctx.bgIsSaturated = true;
                }
            }

            // Pass 1: Strict flood fill
            bool[] visited = new bool[w * h];
            Queue<int> queue = new Queue<int>(w * h / 4);

            for (int x = 0; x < w; x++)
            {
                TrySeedStrict(pixels, visited, queue, x, 0, bgColors, ref ctx);
                TrySeedStrict(pixels, visited, queue, x, h - 1, bgColors, ref ctx);
            }
            for (int y = 0; y < h; y++)
            {
                TrySeedStrict(pixels, visited, queue, 0, y, bgColors, ref ctx);
                TrySeedStrict(pixels, visited, queue, w - 1, y, bgColors, ref ctx);
            }

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w;
                int y = idx / w;

                if (x > 0)     TrySeedStrict(pixels, visited, queue, x - 1, y, bgColors, ref ctx);
                if (x < w - 1) TrySeedStrict(pixels, visited, queue, x + 1, y, bgColors, ref ctx);
                if (y > 0)     TrySeedStrict(pixels, visited, queue, x, y - 1, bgColors, ref ctx);
                if (y < h - 1) TrySeedStrict(pixels, visited, queue, x, y + 1, bgColors, ref ctx);
            }

            // Pass 2: Loose flood fill (limited depth) from the edges of the strict pass
            Queue<int> halo = new Queue<int>();
            Queue<byte> haloDepth = new Queue<byte>();
            for (int i = 0; i < visited.Length; i++)
            {
                if (visited[i])
                {
                    halo.Enqueue(i);
                    haloDepth.Enqueue(0);
                }
            }

            while (halo.Count > 0)
            {
                int idx = halo.Dequeue();
                byte d = haloDepth.Dequeue();
                if (d >= HaloMaxDepth) continue;

                int x = idx % w;
                int y = idx / w;
                byte nextD = (byte)(d + 1);

                if (x > 0)     TrySeedLoose(pixels, visited, halo, haloDepth, x - 1, y, bgColors, nextD, ref ctx);
                if (x < w - 1) TrySeedLoose(pixels, visited, halo, haloDepth, x + 1, y, bgColors, nextD, ref ctx);
                if (y > 0)     TrySeedLoose(pixels, visited, halo, haloDepth, x, y - 1, bgColors, nextD, ref ctx);
                if (y < h - 1) TrySeedLoose(pixels, visited, halo, haloDepth, x, y + 1, bgColors, nextD, ref ctx);
            }

            int removed = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (visited[i])
                {
                    pixels[i].a = 0;
                    removed++;
                }
            }

            // Safety guard: if we removed too much, abort to prevent invisible pawns
            if (removed >= pixels.Length * MaxRemovedFraction) return source;
            if (removed == 0) return source;

            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }

        private static List<YCbCrColor> SampleDominantEdgeColors(Color32[] pixels, int w, int h)
        {
            int[] buckets = new int[8 * 8 * 8];

            for (int x = 0; x < w; x++)
            {
                BucketIncrement(buckets, pixels[x]);
                BucketIncrement(buckets, pixels[(h - 1) * w + x]);
            }
            for (int y = 1; y < h - 1; y++)
            {
                BucketIncrement(buckets, pixels[y * w]);
                BucketIncrement(buckets, pixels[y * w + (w - 1)]);
            }

            // Find top 3 peaks in the edge color histogram
            int maxC1 = 0, maxIdx1 = -1;
            int maxC2 = 0, maxIdx2 = -1;
            int maxC3 = 0, maxIdx3 = -1;

            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] > maxC1)
                {
                    maxC3 = maxC2; maxIdx3 = maxIdx2;
                    maxC2 = maxC1; maxIdx2 = maxIdx1;
                    maxC1 = buckets[i]; maxIdx1 = i;
                }
                else if (buckets[i] > maxC2)
                {
                    maxC3 = maxC2; maxIdx3 = maxIdx2;
                    maxC2 = buckets[i]; maxIdx2 = i;
                }
                else if (buckets[i] > maxC3)
                {
                    maxC3 = buckets[i]; maxIdx3 = i;
                }
            }

            List<YCbCrColor> colors = new List<YCbCrColor>();
            if (maxIdx1 != -1 && maxC1 > 0)
                colors.Add(GetAverageColorForBucket(pixels, w, h, maxIdx1));
            
            // Add secondary/tertiary peaks if they represent at least 15% of the primary peak
            if (maxIdx2 != -1 && maxC2 >= maxC1 * 0.15f)
                colors.Add(GetAverageColorForBucket(pixels, w, h, maxIdx2));
            if (maxIdx3 != -1 && maxC3 >= maxC1 * 0.15f)
                colors.Add(GetAverageColorForBucket(pixels, w, h, maxIdx3));

            return colors;
        }

        private static YCbCrColor GetAverageColorForBucket(Color32[] pixels, int w, int h, int bucketIdx)
        {
            int br = (bucketIdx / 64) * 32;
            int bg = ((bucketIdx / 8) % 8) * 32;
            int bb = (bucketIdx % 8) * 32;

            long sumR = 0, sumG = 0, sumB = 0;
            int cnt = 0;

            for (int x = 0; x < w; x++)
            {
                AverageIfMatch(pixels[x], bucketIdx, ref sumR, ref sumG, ref sumB, ref cnt);
                AverageIfMatch(pixels[(h - 1) * w + x], bucketIdx, ref sumR, ref sumG, ref sumB, ref cnt);
            }
            for (int y = 1; y < h - 1; y++)
            {
                AverageIfMatch(pixels[y * w], bucketIdx, ref sumR, ref sumG, ref sumB, ref cnt);
                AverageIfMatch(pixels[y * w + (w - 1)], bucketIdx, ref sumR, ref sumG, ref sumB, ref cnt);
            }

            if (cnt > 0)
                return new YCbCrColor(new Color32((byte)(sumR / cnt), (byte)(sumG / cnt), (byte)(sumB / cnt), 255));
            
            return new YCbCrColor(new Color32((byte)br, (byte)bg, (byte)bb, 255));
        }

        private static void BucketIncrement(int[] buckets, Color32 p)
        {
            if (p.a < AlreadyTransparentAlpha) return;
            buckets[(p.r / 32) * 64 + (p.g / 32) * 8 + (p.b / 32)]++;
        }

        private static void AverageIfMatch(Color32 p, int bucketIdx, ref long sR, ref long sG, ref long sB, ref int cnt)
        {
            if (p.a < AlreadyTransparentAlpha) return;
            int pBucketIdx = (p.r / 32) * 64 + (p.g / 32) * 8 + (p.b / 32);
            if (pBucketIdx == bucketIdx)
            {
                sR += p.r; sG += p.g; sB += p.b; cnt++;
            }
        }

        private static void TrySeedStrict(Color32[] pixels, bool[] visited, Queue<int> queue, int x, int y, List<YCbCrColor> bgColors, ref FloodContext ctx)
        {
            int idx = y * ctx.width + x;
            if (visited[idx]) return;

            float chromaTol, lumaTol;
            GetLocalTolerances(x, y, ChromaStrict, LumaStrict, out chromaTol, out lumaTol, ref ctx);

            if (EvaluatePixel(pixels[idx], bgColors, chromaTol, lumaTol, ref ctx))
            {
                visited[idx] = true;
                queue.Enqueue(idx);
            }
        }

        private static void TrySeedLoose(Color32[] pixels, bool[] visited, Queue<int> queue, Queue<byte> depthQ, int x, int y, List<YCbCrColor> bgColors, byte nextDepth, ref FloodContext ctx)
        {
            int idx = y * ctx.width + x;
            if (visited[idx]) return;

            float chromaTol, lumaTol;
            GetLocalTolerances(x, y, ChromaLoose, LumaLoose, out chromaTol, out lumaTol, ref ctx);

            if (EvaluatePixel(pixels[idx], bgColors, chromaTol, lumaTol, ref ctx))
            {
                visited[idx] = true;
                queue.Enqueue(idx);
                depthQ.Enqueue(nextDepth);
            }
        }

        private static bool EvaluatePixel(Color32 pixel, List<YCbCrColor> bgColors, float chromaTol, float lumaTol, ref FloodContext ctx)
        {
            if (pixel.a < AlreadyTransparentAlpha) return true;

            YCbCrColor p = new YCbCrColor(pixel);

            // Skin Tone Guard: Protect warm human skin colors (face, neck, ears) from erasure
            if (p.Cb >= 95f && p.Cb <= 126f && p.Cr >= 130f && p.Cr <= 165f)
            {
                if (!ctx.bgIsSkinLike) return false;
            }

            // Saturated/Vibrant Color Guard: Protect colored hair/clothing if background is neutral
            float pSat = System.Math.Abs(p.Cb - 128f) + System.Math.Abs(p.Cr - 128f);
            if (pSat > 25f)
            {
                if (!ctx.bgIsSaturated) return false;
            }

            // Compare against dominant background color profiles
            foreach (var bg in bgColors)
            {
                float chromaDist = System.Math.Abs(p.Cb - bg.Cb) + System.Math.Abs(p.Cr - bg.Cr);
                float lumaDist   = System.Math.Abs(p.Y - bg.Y);

                if (chromaDist <= chromaTol && lumaDist <= lumaTol)
                    return true;
            }

            return false;
        }

        private static void GetLocalTolerances(int x, int y, float baseChroma, float baseLuma, out float chromaTol, out float lumaTol, ref FloodContext ctx)
        {
            // Tighten tolerances by 50% in the core face/neck zone to protect facial details
            if (x >= ctx.coreMinX && x <= ctx.coreMaxX && y >= ctx.coreMinY && y <= ctx.coreMaxY)
            {
                chromaTol = baseChroma * 0.5f;
                lumaTol   = baseLuma * 0.6f;
            }
            else
            {
                chromaTol = baseChroma;
                lumaTol   = baseLuma;
            }
        }
    }
}
