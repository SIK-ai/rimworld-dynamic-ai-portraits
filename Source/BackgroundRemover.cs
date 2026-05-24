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
        private const int ToleranceStrict = 40;

        // Looser tolerance for second-pass cleanup — only applied to pixels that are
        // already adjacent to a transparent pixel, so halos get caught without bleeding
        // into the subject's interior.
        private const int ToleranceLoose = 70;

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

            // ── PASS 2: halo cleanup with looser tolerance ───────────────────────
            // Seed from any visited pixel — only expand outward into pixels that are
            // still within the LOOSE tolerance. This catches anti-aliased edges and
            // gradient halos without bleeding into unrelated subject pixels.
            Queue<int> halo = new Queue<int>();
            for (int i = 0; i < visited.Length; i++)
                if (visited[i]) halo.Enqueue(i);

            while (halo.Count > 0)
            {
                int idx = halo.Dequeue();
                int x = idx % w;
                int y = idx / w;

                if (x > 0)     TrySeedHalo(pixels, visited, halo, x - 1, y, w, bgR, bgG, bgB);
                if (x < w - 1) TrySeedHalo(pixels, visited, halo, x + 1, y, w, bgR, bgG, bgB);
                if (y > 0)     TrySeedHalo(pixels, visited, halo, x, y - 1, w, bgR, bgG, bgB);
                if (y < h - 1) TrySeedHalo(pixels, visited, halo, x, y + 1, w, bgR, bgG, bgB);
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

            // Sanity guard
            if (removed >= pixels.Length * 0.99f) return source;

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
                visited[idx] = true;
                queue.Enqueue(idx);
            }
        }
    }
}
