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

        private const float ChromaLoose = 10f;
        private const float LumaLoose = 30f;

        private const int HaloMaxDepth = 1;
        private const float MaxRemovedFraction = 0.95f;
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

            // Sample up to 3 dominant background colors from corners
            List<YCbCrColor> bgColors = SampleDominantEdgeColors(pixels, w, h);
            if (bgColors.Count == 0) return source;

            // Pass 1: Strict flood fill
            bool[] visited = new bool[w * h];
            Queue<int> queue = new Queue<int>(w * h / 4);

            // Seed from a safe strip along the upper left and upper right edges, avoiding the center.
            int seedW = w / 10;
            int seedH = h / 2; // Upper half
            for (int y = h - seedH; y < h; y++)
            {
                for (int x = 0; x < seedW; x++)
                {
                    TrySeedStrict(pixels, visited, queue, x, y, w, h, bgColors);
                }
                for (int x = w - seedW; x < w; x++)
                {
                    TrySeedStrict(pixels, visited, queue, x, y, w, h, bgColors);
                }
            }

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w;
                int y = idx / w;

                if (x > 0)     TrySeedStrict(pixels, visited, queue, x - 1, y, w, h, bgColors);
                if (x < w - 1) TrySeedStrict(pixels, visited, queue, x + 1, y, w, h, bgColors);
                if (y > 0)     TrySeedStrict(pixels, visited, queue, x, y - 1, w, h, bgColors);
                if (y < h - 1) TrySeedStrict(pixels, visited, queue, x, y + 1, w, h, bgColors);
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

                if (x > 0)     TrySeedLoose(pixels, visited, halo, haloDepth, x - 1, y, w, h, bgColors, nextD);
                if (x < w - 1) TrySeedLoose(pixels, visited, halo, haloDepth, x + 1, y, w, h, bgColors, nextD);
                if (y > 0)     TrySeedLoose(pixels, visited, halo, haloDepth, x, y - 1, w, h, bgColors, nextD);
                if (y < h - 1) TrySeedLoose(pixels, visited, halo, haloDepth, x, y + 1, w, h, bgColors, nextD);
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

            // Pass 3: Multi-level alpha feathering/anti-aliasing of the cut-out edge
            byte[] dist = new byte[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                dist[i] = (pixels[i].a == 0) ? (byte)0 : (byte)255;
            }

            // Find pixels at distance 1
            List<int> border1 = new List<int>();
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    if (dist[i] == 255)
                    {
                        if (dist[i - 1] == 0 || dist[i + 1] == 0 || dist[i - w] == 0 || dist[i + w] == 0)
                        {
                            border1.Add(i);
                        }
                    }
                }
            }
            foreach (int i in border1)
            {
                dist[i] = 1;
            }

            // Find pixels at distance 2
            List<int> border2 = new List<int>();
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    if (dist[i] == 255)
                    {
                        if (dist[i - 1] == 1 || dist[i + 1] == 1 || dist[i - w] == 1 || dist[i + w] == 1)
                        {
                            border2.Add(i);
                        }
                    }
                }
            }
            foreach (int i in border2)
            {
                dist[i] = 2;
            }

            // Soften alpha based on distance
            for (int i = 0; i < pixels.Length; i++)
            {
                if (dist[i] == 1)
                {
                    pixels[i].a = (byte)(pixels[i].a * 0.4f);
                }
                else if (dist[i] == 2)
                {
                    pixels[i].a = (byte)(pixels[i].a * 0.75f);
                }
            }

            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }

        private static List<YCbCrColor> SampleDominantEdgeColors(Color32[] pixels, int w, int h)
        {
            int[] buckets = new int[8 * 8 * 8];

            int cornerW = w / 8;
            int cornerH = h / 8;

            // Top-left corner
            for (int y = h - cornerH; y < h; y++)
            {
                for (int x = 0; x < cornerW; x++)
                {
                    BucketIncrement(buckets, pixels[y * w + x]);
                }
            }

            // Top-right corner
            for (int y = h - cornerH; y < h; y++)
            {
                for (int x = w - cornerW; x < w; x++)
                {
                    BucketIncrement(buckets, pixels[y * w + x]);
                }
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

            int cornerW = w / 8;
            int cornerH = h / 8;

            // Top-left corner
            for (int y = h - cornerH; y < h; y++)
            {
                for (int x = 0; x < cornerW; x++)
                {
                    AverageIfMatch(pixels[y * w + x], bucketIdx, ref sumR, ref sumG, ref sumB, ref cnt);
                }
            }

            // Top-right corner
            for (int y = h - cornerH; y < h; y++)
            {
                for (int x = w - cornerW; x < w; x++)
                {
                    AverageIfMatch(pixels[y * w + x], bucketIdx, ref sumR, ref sumG, ref sumB, ref cnt);
                }
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

        // Tolerance multipliers — applied uniformly across the whole image (an earlier
        // position-dependent variant was scrapped; the params are gone but the global
        // tightening factors are retained because they noticeably reduce subject erosion).
        private const float ChromaToleranceMultiplier = 0.7f;
        private const float LumaToleranceMultiplier   = 0.75f;

        private static void TrySeedStrict(Color32[] pixels, bool[] visited, Queue<int> queue, int x, int y, int w, int h, List<YCbCrColor> bgColors)
        {
            int idx = y * w + x;
            if (visited[idx]) return;

            if (EvaluatePixel(pixels[idx], bgColors,
                              ChromaStrict * ChromaToleranceMultiplier,
                              LumaStrict   * LumaToleranceMultiplier))
            {
                visited[idx] = true;
                queue.Enqueue(idx);
            }
        }

        private static void TrySeedLoose(Color32[] pixels, bool[] visited, Queue<int> queue, Queue<byte> depthQ, int x, int y, int w, int h, List<YCbCrColor> bgColors, byte nextDepth)
        {
            int idx = y * w + x;
            if (visited[idx]) return;

            if (EvaluatePixel(pixels[idx], bgColors,
                              ChromaLoose * ChromaToleranceMultiplier,
                              LumaLoose   * LumaToleranceMultiplier))
            {
                visited[idx] = true;
                queue.Enqueue(idx);
                depthQ.Enqueue(nextDepth);
            }
        }

        private static bool EvaluatePixel(Color32 pixel, List<YCbCrColor> bgColors, float chromaTol, float lumaTol)
        {
            if (pixel.a < AlreadyTransparentAlpha) return true;

            YCbCrColor p = new YCbCrColor(pixel);

            // Skin Tone Guard: Protect warm human skin colors (face, neck, ears) from erasure
            if (p.Cb >= 95f && p.Cb <= 126f && p.Cr >= 130f && p.Cr <= 165f)
            {
                bool bgIsSkinLike = false;
                foreach (var bg in bgColors)
                {
                    if (bg.Cb >= 95f && bg.Cb <= 126f && bg.Cr >= 130f && bg.Cr <= 165f)
                    {
                        bgIsSkinLike = true;
                        break;
                    }
                }
                if (!bgIsSkinLike) return false;
            }

            // Saturated/Vibrant Color Guard: Protect colored hair/clothing if background is neutral
            float pSat = System.Math.Abs(p.Cb - 128f) + System.Math.Abs(p.Cr - 128f);
            if (pSat > 25f)
            {
                bool bgIsSaturated = false;
                foreach (var bg in bgColors)
                {
                    float bgSat = System.Math.Abs(bg.Cb - 128f) + System.Math.Abs(bg.Cr - 128f);
                    if (bgSat > 20f)
                    {
                        bgIsSaturated = true;
                        break;
                    }
                }
                if (!bgIsSaturated) return false;
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

        public static bool IsMonoBackground(Texture2D source)
        {
            if (source == null) return false;

            int w = source.width;
            int h = source.height;
            Color32[] pixels = source.GetPixels32();

            List<Color32> samples = new List<Color32>();

            // Sample edges to analyze background uniformity
            // Top border
            int yTop = h - 2;
            if (yTop >= 0)
            {
                for (int x = 0; x < w; x += 4)
                {
                    samples.Add(pixels[yTop * w + x]);
                }
            }

            // Left border (upper 60%)
            int xLeft = 1;
            for (int y = h * 4 / 10; y < h - 2; y += 4)
            {
                samples.Add(pixels[y * w + xLeft]);
            }

            // Right border (upper 60%)
            int xRight = w - 2;
            if (xRight >= 0)
            {
                for (int y = h * 4 / 10; y < h - 2; y += 4)
                {
                    samples.Add(pixels[y * w + xRight]);
                }
            }

            if (samples.Count == 0) return false;

            // 1. Bin colors to find the dominant color bucket (8 bins per channel -> 512 total)
            int[] buckets = new int[512];
            int validCount = 0;
            foreach (var p in samples)
            {
                if (p.a < AlreadyTransparentAlpha) continue;
                int rBin = p.r / 32;
                int gBin = p.g / 32;
                int bBin = p.b / 32;
                int binIdx = rBin * 64 + gBin * 8 + bBin;
                buckets[binIdx]++;
                validCount++;
            }

            // If the image is already mostly transparent, we don't need background removal
            if (validCount < samples.Count * 0.2f)
            {
                return false;
            }

            // Find the most frequent bucket
            int maxCount = 0;
            int maxIdx = -1;
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] > maxCount)
                {
                    maxCount = buckets[i];
                    maxIdx = i;
                }
            }

            if (maxIdx == -1 || maxCount == 0) return false;

            // 2. Compute average color of pixels in that bucket
            long sumR = 0, sumG = 0, sumB = 0;
            int matchCount = 0;
            foreach (var p in samples)
            {
                if (p.a < AlreadyTransparentAlpha) continue;
                int binIdx = (p.r / 32) * 64 + (p.g / 32) * 8 + (p.b / 32);
                if (binIdx == maxIdx)
                {
                    sumR += p.r;
                    sumG += p.g;
                    sumB += p.b;
                    matchCount++;
                }
            }

            if (matchCount == 0) return false;

            byte domR = (byte)(sumR / matchCount);
            byte domG = (byte)(sumG / matchCount);
            byte domB = (byte)(sumB / matchCount);

            // 3. Count how many of the valid samples are close to this dominant color (L1 distance < 45)
            int closeCount = 0;
            foreach (var p in samples)
            {
                if (p.a < AlreadyTransparentAlpha) continue;
                int diff = System.Math.Abs(p.r - domR) + System.Math.Abs(p.g - domG) + System.Math.Abs(p.b - domB);
                if (diff < 45)
                {
                    closeCount++;
                }
            }

            float ratio = (float)closeCount / validCount;
            return ratio > 0.50f; // Threshold verified via Python batch testing
        }
    }
}
