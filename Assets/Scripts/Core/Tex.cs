using UnityEngine;

namespace CombatPrep.Core
{
    /// <summary>
    /// Every texture in the game is drawn here, pixel by pixel, at startup. This is the
    /// piece that makes "no imported assets" survive contact with wanting things to
    /// actually look like something: paper targets with printed silhouettes and scoring
    /// rings, weapon skins, ground and concrete surfaces.
    /// </summary>
    public static class Tex
    {
        // ------------------------------------------------------------------ target sheet

        /// <summary>
        /// A paper silhouette target: aged card stock, a printed torso silhouette, and a
        /// concentric scoring bullseye over centre mass. Drawn at a 2:3 aspect so the
        /// rings come out circular on a board of the same proportions.
        /// </summary>
        public static Texture2D TargetSheet(int width = 512, int seed = 0)
        {
            int height = Mathf.RoundToInt(width * 1.5f);
            var tex = New(width, height, "TargetSheet");
            var px = new Color32[width * height];

            Random.State prev = Random.state;
            Random.InitState(seed);

            Color paper = Mat.TargetPaper;
            Color ink = new Color(0.17f, 0.16f, 0.15f);
            Color ringA = new Color(0.13f, 0.70f, 0.26f);
            Color ringB = new Color(0.09f, 0.52f, 0.20f);
            Color ringCore = new Color(0.05f, 0.26f, 0.12f);
            Color line = new Color(0.95f, 0.95f, 0.92f);

            float aspect = (float)height / width;

            // Silhouette, laid out in "u units" where 1.0 == texture width.
            const float headY = 1.245f, headR = 0.086f;
            const float shoulderY = 1.020f, shoulderRX = 0.300f, shoulderRY = 0.150f;
            const float torsoY = 0.700f, torsoRX = 0.266f, torsoRY = 0.360f;

            // Scoring rings over centre mass.
            const float ringCX = 0.5f, ringCY = 0.880f, ringOuter = 0.232f;
            const int bands = 5;

            for (int y = 0; y < height; y++)
            {
                float v = y / (height - 1f);
                float py = v * aspect;

                for (int x = 0; x < width; x++)
                {
                    float u = x / (width - 1f);

                    // --- paper: warm card stock with fibre grain and edge soiling ---
                    float grain = Mathf.PerlinNoise(u * 120f + seed, py * 120f) * 0.10f
                                + Mathf.PerlinNoise(u * 14f, py * 14f + seed) * 0.12f;
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float soil = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - edge * 9f)) * 0.22f;
                    Color c = paper * (0.92f + grain) * (1f - soil * 0.55f);

                    // --- printed silhouette ---
                    bool inHead = Circle(u, py, 0.5f, headY, headR);
                    bool inNeck = u > 0.452f && u < 0.548f && py > 1.105f && py < 1.190f;
                    bool inShoulder = Ellipse(u, py, 0.5f, shoulderY, shoulderRX, shoulderRY);
                    bool inTorso = Ellipse(u, py, 0.5f, torsoY, torsoRX, torsoRY);

                    if (inHead || inNeck || inShoulder || inTorso)
                    {
                        // Slight ink mottling so it does not read as flat vector art.
                        float mott = Mathf.PerlinNoise(u * 40f, py * 40f) * 0.12f;
                        c = ink * (0.9f + mott);
                    }

                    // --- scoring rings, printed over the silhouette ---
                    float dx = u - ringCX;
                    float dy = py - ringCY;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    if (d <= ringOuter)
                    {
                        float step = ringOuter / bands;
                        int band = Mathf.Clamp(Mathf.FloorToInt(d / step), 0, bands - 1);
                        float withinBand = d - band * step;

                        c = band == 0 ? ringCore : (band % 2 == 1 ? ringA : ringB);

                        // White separator at each band edge.
                        if (withinBand < 0.0055f || withinBand > step - 0.0055f) c = line;

                        // Print wear, so the ink sits on the paper rather than replacing it.
                        float wear = Mathf.PerlinNoise(u * 55f + 9f, py * 55f) * 0.16f;
                        c *= (0.92f + wear);
                    }

                    // Outer border rule.
                    if (edge < 0.012f) c = ink * 0.8f;

                    px[y * width + x] = c;
                }
            }

            Random.state = prev;
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        // ----------------------------------------------------------------- weapon skins

        /// <summary>Blotchy multi-colour camouflage from layered value noise.</summary>
        public static Texture2D Camo(Color[] palette, float scale = 6f, int seed = 0, int size = 256)
        {
            var tex = New(size, size, "Camo");
            var px = new Color32[size * size];
            float o = seed * 37.7f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float n = Mathf.PerlinNoise(u * scale + o, v * scale + o) * 0.6f
                        + Mathf.PerlinNoise(u * scale * 2.3f + o, v * scale * 2.3f + o) * 0.3f
                        + Mathf.PerlinNoise(u * scale * 5.1f + o, v * scale * 5.1f + o) * 0.1f;

                int idx = Mathf.Clamp(Mathf.FloorToInt(n * palette.Length), 0, palette.Length - 1);
                float grain = Mathf.PerlinNoise(u * 200f, v * 200f) * 0.08f;
                px[y * size + x] = palette[idx] * (0.94f + grain);
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Hard-edged blocky camo, the "digital" look.</summary>
        public static Texture2D Digital(Color[] palette, int cells = 26, int seed = 0, int size = 256)
        {
            var tex = New(size, size, "Digital");
            var px = new Color32[size * size];
            float o = seed * 51.3f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int cx = x * cells / size, cy = y * cells / size;
                float n = Mathf.PerlinNoise(cx * 0.42f + o, cy * 0.42f + o);
                int idx = Mathf.Clamp(Mathf.FloorToInt(n * palette.Length), 0, palette.Length - 1);
                px[y * size + x] = palette[idx];
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Diagonal racing stripes over a base colour.</summary>
        public static Texture2D Stripes(Color baseColor, Color stripe, int count = 7,
                                        float angle = 32f, float duty = 0.42f, int size = 256)
        {
            var tex = New(size, size, "Stripes");
            var px = new Color32[size * size];
            float rad = angle * Mathf.Deg2Rad;
            float ca = Mathf.Cos(rad), sa = Mathf.Sin(rad);

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float p = (u * ca + v * sa) * count;
                float f = p - Mathf.Floor(p);
                float grain = Mathf.PerlinNoise(u * 180f, v * 180f) * 0.07f;
                px[y * size + x] = (f < duty ? stripe : baseColor) * (0.95f + grain);
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Hex mesh over a base colour - the "tech" skin family.</summary>
        public static Texture2D Hex(Color baseColor, Color mesh, float cells = 13f, int size = 256)
        {
            var tex = New(size, size, "Hex");
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size * cells;
                float v = y / (float)size * cells;

                // Offset every other row to interlock the cells.
                if (Mathf.FloorToInt(v) % 2 == 1) u += 0.5f;
                float fu = Mathf.Abs(u - Mathf.Floor(u) - 0.5f);
                float fv = Mathf.Abs(v - Mathf.Floor(v) - 0.5f);
                float edge = Mathf.Max(fu, fv * 0.9f + fu * 0.5f);

                px[y * size + x] = edge > 0.42f ? mesh : baseColor;
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Worn metal: brushed streaks with rust blooming from the edges.</summary>
        public static Texture2D Weathered(Color metal, Color rust, float rustAmount = 0.4f, int seed = 0, int size = 256)
        {
            var tex = New(size, size, "Weathered");
            var px = new Color32[size * size];
            float o = seed * 19.1f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float brush = Mathf.PerlinNoise(u * 220f + o, v * 6f) * 0.16f;
                float blotch = Mathf.PerlinNoise(u * 5f + o, v * 5f + o);
                float r = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((blotch - (1f - rustAmount)) * 3.5f));
                px[y * size + x] = Color.Lerp(metal * (0.92f + brush), rust * (0.9f + brush), r);
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ------------------------------------------------------------------- environment

        /// <summary>Tiling sand/gravel ground with darker patches and scattered grit.</summary>
        public static Texture2D Ground(int size = 256, int seed = 0)
        {
            var tex = New(size, size, "Ground");
            var px = new Color32[size * size];
            float o = seed * 11.3f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float broad = Mathf.PerlinNoise(u * 4f + o, v * 4f + o);
                float fine = Mathf.PerlinNoise(u * 42f + o, v * 42f + o);
                float grit = Mathf.PerlinNoise(u * 190f, v * 190f);

                Color c = Color.Lerp(Mat.Sand, Mat.SandDark, broad * 0.75f);
                c = Color.Lerp(c, Mat.Dirt, Mathf.SmoothStep(0f, 1f, (broad - 0.62f) * 3f) * 0.55f);
                c *= 0.90f + fine * 0.14f + grit * 0.08f;

                px[y * size + x] = c;
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Tiling concrete with pour mottling and fine speckle.</summary>
        public static Texture2D Concrete(int size = 256, int seed = 0)
        {
            var tex = New(size, size, "Concrete");
            var px = new Color32[size * size];
            float o = seed * 7.7f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float mott = Mathf.PerlinNoise(u * 6f + o, v * 6f + o) * 0.18f
                           + Mathf.PerlinNoise(u * 28f + o, v * 28f + o) * 0.10f;
                float speck = Mathf.PerlinNoise(u * 240f, v * 240f) > 0.72f ? -0.07f : 0f;
                px[y * size + x] = Mat.Concrete * (0.90f + mott + speck);
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Corrugated panel shading - shipping containers, shed walls.</summary>
        public static Texture2D Corrugated(Color baseColor, int ribs = 18, int seed = 0, int size = 256)
        {
            var tex = New(size, size, "Corrugated");
            var px = new Color32[size * size];
            float o = seed * 23.9f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                // Sine across the panel fakes the rib highlight and shadow.
                float rib = (Mathf.Sin(u * ribs * Mathf.PI * 2f) * 0.5f + 0.5f);
                float shade = Mathf.Lerp(0.74f, 1.12f, rib);
                float wear = Mathf.PerlinNoise(u * 7f + o, v * 7f + o);
                float streak = Mathf.PerlinNoise(u * 90f + o, v * 4f) * 0.10f;

                Color c = baseColor * (shade + streak);
                c = Color.Lerp(c, Mat.RustDark, Mathf.SmoothStep(0f, 1f, (wear - 0.68f) * 3.2f) * 0.6f);
                px[y * size + x] = c;
            }

            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ------------------------------------------------------------------------ utils

        static Texture2D New(int w, int h, string name) => new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4
        };

        static bool Circle(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        static bool Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return dx * dx + dy * dy <= 1f;
        }
    }
}
