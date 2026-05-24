using System.Collections.Generic;
using UnityEngine;

namespace AIPortraits
{
    /// <summary>
    /// Removes a near-uniform background from a generated portrait by flood-filling
    /// edge-connected pixels that match the dominant edge colour.
    ///
    /// Reliability improvements over the original (corner-average) approach:
    ///   - Edge colour sampled by MODE across ALL edge pixels, not the 4 corners.
    ///     Works correctly when the subject's hair/shoulders touch a corner.
    ///   - Two-pass flood fill: first pass with strict tolerance to seed reliable
    ///     background regions, second pass with looser tolerance from already-cleared
    ///     pixels to catch gradient halos.
    ///   - Stricter "already transparent" early-out (alpha &lt; 10 in all corners) so
    ///     near-opaque images don't skip processing.
    ///   - Flood-fill only — background-coloured pixels INSIDE the subject (e.g. a white
    ///     shirt button on a white background) are preserved.
    /// </summary>
    public static class BackgroundRemover
    {
        // Per-channel tolerance for first pass — strict enough to avoid the subject.
        // Lowered from 40→35 because painterly outputs have subject pixels (skin highlights,
        // pale-fabric folds) within 40 units of common pale backgrounds, causing bleed.
        private const int ToleranceStrict = 35;

        // Looser tolerance for halo cleanup — only applied from already-transparent neighbours.
        // Lowered from 70→48 because the previous value was so wide it routinely flooded into
        // pale skin / light hair tones adjacent to a light background.
        private const int ToleranceLoose = 48;

        // Maximum depth (in pixels) the halo cleanup pass is allowed to walk past the strict
        // pass-1 boundary. Without this cap the BFS can erode arbitrarily deep into the
        // subject — e.g. walking down an arm because the skin tone is within loose tolerance
        // of the background. 3 pixels is enough to clean anti-aliased edges and gradient
        // halos without eating limbs.
        private const int HaloMaxDepth = 3;

        // Sanity floor: if the flood fill grabbed more than this fraction of the image, the
        // edge sample is probably wrong (subject filled the frame) and we should abort to
        // avoid producing a mostly-transparent portrait.
        private const float MaxRemovedFraction = 0.80f;

        // Only skip processing if all four corners are EXTREMELY transparent.
        private const byte AlreadyTransparentAlpha = 10;

        /// <summary>
        /// Returns a new Texture2D with the background made transparent, or the
        /// original texture unchanged if it already has transparent corners.
        /// </summary>
        public static Texture2D Process(Texture2D source)
        {
            if (source == null) return null;

            int w = source.width;
            int h = source.height;
            Color32[] pixels = source.GetPixels32();

            // Strict early-out: image must be near-fully transparent at all 4 corners
            if (pixels[0].a              < AlreadyTransparentAlpha &&
                pixels[w - 1].a          < AlreadyTransparentAlpha &&
                pixels[(h - 1) * w].a    < AlreadyTransparentAlpha &&
                pixels[h * w - 1].a      < AlreadyTransparentAlpha)
            {
                return source;
            }

            // Sample the dominant background colour by MODE across all edge pixels.
            // Corner averaging fails when subject touches a corner; mode finds the
            // actual most-common edge colour regardless of where the subject lands.
            int bgR, bgG, bgB;
            SampleDominantEdgeColor(pixels, w, h, out bgR, out bgG, out bgB);

            // ── PASS 1: strict-tolerance flood fill from edge ────────────────────
            bool[] visited = new bool[w * h];
            Queue<int> queue = new Queue<int>(w * h / 4);

            for (int x = 0; x < w; x++)
            {
                TrySeed(pixels, visited, queue, x, 0,     w, bgR, bgG, bgB, ToleranceStrict);
                TrySeed(pixels, visited, queue, x, h - 1, w, bgR, bgG, bgB, ToleranceStrict);
            }
            for (int y = 0; y < h; y++)
            {
                TrySeed(pixels, visited, queue, 0,     y, w, bgR, bgG, bgB, ToleranceStrict);
                TrySeed(pixels, visited, queue, w - 1, y, w, bgR, bgG, bgB, ToleranceStrict);
            }

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w;
                int y = idx / w;

                if (x > 0)     TrySeed(pixels, visited, queue, x - 1, y, w, bgR, bgG, bgB, ToleranceStrict);
                if (x < w - 1) TrySeed(pixels, visited, queue, x + 1, y, w, bgR, bgG, bgB, ToleranceStrict);
                if (y > 0)     TrySeed(pixels, visited, queue, x, y - 1, w, bgR, bgG, bgB, ToleranceStrict);
                if (y < h - 1) TrySeed(pixels, visited, queue, x, y + 1, w, bgR, bgG, bgB, ToleranceStrict);
            }

            // ── PASS 2: halo cleanup with looser tolerance, DEPTH-LIMITED ────────
            // Seed from any pass-1 pixel at depth 0. Each BFS step increments depth.
            // Stop expanding at HaloMaxDepth — this prevents the loose-tolerance flood
            // from walking deep into the subject (e.g. down an arm whose skin tone is
            // within 48 RGB of a warm background).
            Queue<int>   halo      = new Queue<int>();
            Queue<byte>  haloDepth = new Queue<byte>();
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
                int idx   = halo.Dequeue();
                byte d    = haloDepth.Dequeue();
                if (d >= HaloMaxDepth) continue;

                int x = idx % w;
                int y = idx / w;
                byte nextD = (byte)(d + 1);

                if (x > 0)     TrySeedHaloDepth(pixels, visited, halo, haloDepth, x - 1, y, w, bgR, bgG, bgB, nextD);
                if (x < w - 1) TrySeedHaloDepth(pixels, visited, halo, haloDepth, x + 1, y, w, bgR, bgG, bgB, nextD);
                if (y > 0)     TrySeedHaloDepth(pixels, visited, halo, haloDepth, x, y - 1, w, bgR, bgG, bgB, nextD);
                if (y < h - 1) TrySeedHaloDepth(pixels, visited, halo, haloDepth, x, y + 1, w, bgR, bgG, bgB, nextD);
            }

            // Apply alpha = 0 to every flagged pixel
            int removed = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (visited[i])
                {
                    pixels[i].a = 0;
                    removed++;
                }
            }

            // Sanity guard — if more than 80% of pixels are being marked transparent, the
            // edge-mode sample probably caught the subject's colour. Abort to avoid producing
            // a faded/ghosted portrait.
            if (removed >= pixels.Length * MaxRemovedFraction) return source;

            // If nothing was removed, the image had no detectable background — return
            // original to save the cost of a re-encode.
            if (removed == 0) return source;

            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }

        // ── Edge-mode background colour sampling ─────────────────────────────────
        private static void SampleDominantEdgeColor(Color32[] pixels, int w, int h,
                                                    out int bgR, out int bgG, out int bgB)
        {
            // 8×8×8 RGB histogram (each bucket = 32 colour values)
            int[] buckets = new int[8 * 8 * 8];

            // Top + bottom rows
            for (int x = 0; x < w; x++)
            {
                BucketIncrement(buckets, pixels[x]);              // bottom row
                BucketIncrement(buckets, pixels[(h - 1) * w + x]); // top row
            }
            // Left + right columns (skip corners already counted)
            for (int y = 1; y < h - 1; y++)
            {
                BucketIncrement(buckets, pixels[y * w]);
                BucketIncrement(buckets, pixels[y * w + (w - 1)]);
            }

            // Find largest bucket
            int maxCount = 0, maxIdx = 0;
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] > maxCount) { maxCount = buckets[i]; maxIdx = i; }
            }

            int br = (maxIdx / 64) * 32;
            int bg = ((maxIdx / 8) % 8) * 32;
            int bb = (maxIdx % 8) * 32;

            // Average all edge pixels within ±16 of that bucket centre to get a finer colour
            long sumR = 0, sumG = 0, sumB = 0;
            int  cnt  = 0;

            for (int x = 0; x < w; x++)
            {
                AverageIfMatch(pixels[x],                br, bg, bb, ref sumR, ref sumG, ref sumB, ref cnt);
                AverageIfMatch(pixels[(h - 1) * w + x],  br, bg, bb, ref sumR, ref sumG, ref sumB, ref cnt);
            }
            for (int y = 1; y < h - 1; y++)
            {
                AverageIfMatch(pixels[y * w],            br, bg, bb, ref sumR, ref sumG, ref sumB, ref cnt);
                AverageIfMatch(pixels[y * w + (w - 1)],  br, bg, bb, ref sumR, ref sumG, ref sumB, ref cnt);
            }

            if (cnt > 0)
            {
                bgR = (int)(sumR / cnt);
                bgG = (int)(sumG / cnt);
                bgB = (int)(sumB / cnt);
            }
            else
            {
                bgR = br; bgG = bg; bgB = bb;
            }
        }

        private static void BucketIncrement(int[] buckets, Color32 p)
        {
            int r = p.r / 32, g = p.g / 32, b = p.b / 32;
            buckets[r * 64 + g * 8 + b]++;
        }

        private static void AverageIfMatch(Color32 p, int br, int bg, int bb,
                                           ref long sR, ref long sG, ref long sB, ref int cnt)
        {
            if (System.Math.Abs(p.r - br) <= 16 &&
                System.Math.Abs(p.g - bg) <= 16 &&
                System.Math.Abs(p.b - bb) <= 16)
            {
                sR += p.r; sG += p.g; sB += p.b; cnt++;
            }
        }

        private static void TrySeed(Color32[] pixels, bool[] visited, Queue<int> queue,
                                    int x, int y, int w, int bgR, int bgG, int bgB, int tolerance)
        {
            int idx = y * w + x;
            if (visited[idx]) return;

            Color32 p = pixels[idx];
            if (System.Math.Abs(p.r - bgR) <= tolerance &&
                System.Math.Abs(p.g - bgG) <= tolerance &&
                System.Math.Abs(p.b - bgB) <= tolerance)
            {
                visited[idx] = true;
                queue.Enqueue(idx);
            }
        }

        private static void TrySeedHalo(Color32[] pixels, bool[] visited, Queue<int> queue,
                                        int x, int y, int w, int bgR, int bgG, int bgB)
        {
            int idx = y * w + x;
            if (visited[idx]) return;

            Color32 p = pixels[idx];
            if (System.Math.Abs(p.r - bgR) <= ToleranceLoose &&
                System.Math.Abs(p.g - bgG) <= ToleranceLoose &&
                System.Math.Abs(p.b - bgB) <= ToleranceLoose)
            {
                // ── Bright-pixel guard ────────────────────────────────────────────────
                // When the sampled background is near-white (avg > 200), pale skin tones
                // (e.g. albino: ~240,230,225) fall within 48-unit tolerance and get erased.
                // Apply a tighter threshold (22) for very bright candidate pixels so that
                // true edge halos (≤22 from white) are still removed but pale skin (25-30
                // per-channel delta) is preserved.
                int bgBrightness = (bgR + bgG + bgB) / 3;
                if (bgBrightness > 200)
                {
                    int pBrightness = (p.r + p.g + p.b) / 3;
                    if (pBrightness > 210)
                    {
                        const int StrictHaloTol = 22;
                        if (System.Math.Abs(p.r - bgR) > StrictHaloTol ||
                            System.Math.Abs(p.g - bgG) > StrictHaloTol ||
                            System.Math.Abs(p.b - bgB) > StrictHaloTol)
                            return; // Protect — likely pale skin, not a background halo
                    }
                }
                // ─────────────────────────────────────────────────────────────────────

                visited[idx] = true;
                queue.Enqueue(idx);
            }
        }

        // Depth-tracking variant — used by the bounded pass-2 halo cleanup.
        // Mirrors TrySeedHalo's loose-tolerance + bright-pixel guard logic but also
        // enqueues a depth byte in parallel so the BFS can stop at HaloMaxDepth.
        private static void TrySeedHaloDepth(Color32[] pixels, bool[] visited,
                                             Queue<int> queue, Queue<byte> depthQ,
                                             int x, int y, int w,
                                             int bgR, int bgG, int bgB, byte nextDepth)
        {
            int idx = y * w + x;
            if (visited[idx]) return;

            Color32 p = pixels[idx];
            if (System.Math.Abs(p.r - bgR) <= ToleranceLoose &&
                System.Math.Abs(p.g - bgG) <= ToleranceLoose &&
                System.Math.Abs(p.b - bgB) <= ToleranceLoose)
            {
                // Bright-pixel guard — same logic as TrySeedHalo, protects pale skin.
                int bgBrightness = (bgR + bgG + bgB) / 3;
                if (bgBrightness > 200)
                {
                    int pBrightness = (p.r + p.g + p.b) / 3;
                    if (pBrightness > 210)
                    {
                        const int StrictHaloTol = 22;
                        if (System.Math.Abs(p.r - bgR) > StrictHaloTol ||
                            System.Math.Abs(p.g - bgG) > StrictHaloTol ||
                            System.Math.Abs(p.b - bgB) > StrictHaloTol)
                            return;
                    }
                }

                visited[idx] = true;
                queue.Enqueue(idx);
                depthQ.Enqueue(nextDepth);
            }
        }

    }
}
