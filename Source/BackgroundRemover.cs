using System.Collections.Generic;
using UnityEngine;

namespace AIPortraits
{
    /// <summary>
    /// Removes a near-uniform background from a generated portrait by flood-filling
    /// edge-connected pixels that match the corner-sampled background color.
    ///
    /// Why flood-fill instead of naive chroma-key: a chroma key would also erase
    /// background-coloured patches *inside* the subject (e.g. a white shirt button
    /// on a white background). Flood fill only removes the contiguous region that
    /// touches the image edge, so the subject stays intact.
    /// </summary>
    public static class BackgroundRemover
    {
        // Per-channel tolerance (0–255) for matching the background colour.
        // 30 catches gradients and JPEG compression noise without eating the subject.
        private const int Tolerance = 30;

        // If all four corners already have alpha < this, assume the image is already
        // transparent and skip processing entirely.
        private const byte AlreadyTransparentAlpha = 50;

        /// <summary>
        /// Returns a new Texture2D with the background made transparent, or the
        /// original texture unchanged if it already has transparent corners.
        /// Caller is responsible for destroying the original if a new one is returned.
        /// </summary>
        public static Texture2D Process(Texture2D source)
        {
            if (source == null) return null;

            int w = source.width;
            int h = source.height;
            Color32[] pixels = source.GetPixels32();

            // Early out — already transparent
            if (pixels[0].a < AlreadyTransparentAlpha &&
                pixels[w - 1].a < AlreadyTransparentAlpha &&
                pixels[(h - 1) * w].a < AlreadyTransparentAlpha &&
                pixels[h * w - 1].a < AlreadyTransparentAlpha)
            {
                return source;
            }

            // Sample background color: average of the four corners
            Color32 c0 = pixels[0];
            Color32 c1 = pixels[w - 1];
            Color32 c2 = pixels[(h - 1) * w];
            Color32 c3 = pixels[h * w - 1];
            int bgR = (c0.r + c1.r + c2.r + c3.r) / 4;
            int bgG = (c0.g + c1.g + c2.g + c3.g) / 4;
            int bgB = (c0.b + c1.b + c2.b + c3.b) / 4;

            // Flood fill from every edge pixel that matches the background
            bool[] visited = new bool[w * h];
            Queue<int> queue = new Queue<int>(w * h / 4);

            for (int x = 0; x < w; x++)
            {
                TrySeed(pixels, visited, queue, x, 0,     w, bgR, bgG, bgB);
                TrySeed(pixels, visited, queue, x, h - 1, w, bgR, bgG, bgB);
            }
            for (int y = 0; y < h; y++)
            {
                TrySeed(pixels, visited, queue, 0,     y, w, bgR, bgG, bgB);
                TrySeed(pixels, visited, queue, w - 1, y, w, bgR, bgG, bgB);
            }

            // BFS to expand to all connected background pixels
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % w;
                int y = idx / w;

                if (x > 0)     TrySeed(pixels, visited, queue, x - 1, y, w, bgR, bgG, bgB);
                if (x < w - 1) TrySeed(pixels, visited, queue, x + 1, y, w, bgR, bgG, bgB);
                if (y > 0)     TrySeed(pixels, visited, queue, x, y - 1, w, bgR, bgG, bgB);
                if (y < h - 1) TrySeed(pixels, visited, queue, x, y + 1, w, bgR, bgG, bgB);
            }

            // Apply alpha = 0 to every background pixel
            int removed = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (visited[i])
                {
                    pixels[i].a = 0;
                    removed++;
                }
            }

            // Sanity guard — if we somehow flagged the entire image, return original
            if (removed >= pixels.Length * 0.99f)
            {
                return source;
            }

            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }

        private static void TrySeed(Color32[] pixels, bool[] visited, Queue<int> queue,
                                    int x, int y, int w, int bgR, int bgG, int bgB)
        {
            int idx = y * w + x;
            if (visited[idx]) return;

            Color32 p = pixels[idx];
            if (System.Math.Abs(p.r - bgR) <= Tolerance &&
                System.Math.Abs(p.g - bgG) <= Tolerance &&
                System.Math.Abs(p.b - bgB) <= Tolerance)
            {
                visited[idx] = true;
                queue.Enqueue(idx);
            }
        }
    }
}
