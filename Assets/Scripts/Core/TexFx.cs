using UnityEngine;

namespace CombatPrep.Core
{
    /// <summary>
    /// Weather and effects textures: particle sprites for rain, splashes, smoke and fire, plus
    /// the ground decals (scorch, puddles) and the lens dirt the bloom picks out. All tiny and
    /// drawn once at startup; the particle colour does the tinting, so most are white with
    /// the shape in alpha.
    /// </summary>
    public static partial class Tex
    {
        /// <summary>
        /// One rain streak: soft across its width and faded at both ends, so a particle
        /// stretched along its velocity never shows a hard cap.
        /// </summary>
        public static Texture2D RainStreak(int w = 16, int h = 128)
        {
            var tex = NewClamped(w, h, "RainStreak");
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float v = (y + 0.5f) / h;
                float across = Mathf.Exp(-u * u * 7f);
                float along = Mathf.Pow(Mathf.Sin(v * Mathf.PI), 0.6f);
                px[y * w + x] = new Color(1f, 1f, 1f, across * along);
            }
            return Finish(tex, px);
        }

        /// <summary>Round, soft-edged dot - splash droplets, embers, sparks.</summary>
        public static Texture2D SoftDot(int size = 64, float hardness = 5f)
        {
            var tex = NewClamped(size, size, "SoftDot");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Radius(x, y, size);
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Exp(-r * r * hardness) * Mathf.Clamp01((1f - r) * 6f));
            }
            return Finish(tex, px);
        }

        /// <summary>A thin ring for the ripple a raindrop leaves on a wet surface.</summary>
        public static Texture2D Ring(int size = 128)
        {
            var tex = NewClamped(size, size, "Ring");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Radius(x, y, size);
                float d = (r - 0.74f) / 0.085f;
                float ring = Mathf.Exp(-d * d);
                float inner = Mathf.Exp(-((r - 0.45f) / 0.07f) * ((r - 0.45f) / 0.07f)) * 0.35f;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(ring + inner) * Mathf.Clamp01((1f - r) * 8f));
            }
            return Finish(tex, px);
        }

        /// <summary>
        /// A billow of smoke: a soft disc eaten away by noise, so overlapping puffs build a
        /// ragged column instead of a stack of circles. Lit a touch brighter at the top.
        /// </summary>
        public static Texture2D SmokePuff(int size = 128, int seed = 0)
        {
            var tex = NewClamped(size, size, "SmokePuff");
            var px = new Color32[size * size];
            float o = seed * 13.7f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float r = Radius(x, y, size);
                float n = Mathf.PerlinNoise(u * 4.2f + o, v * 4.2f + o) * 0.65f
                        + Mathf.PerlinNoise(u * 9.5f + o, v * 9.5f - o) * 0.35f;
                float body = Mathf.SmoothStep(1f, 0.15f, r);
                float a = body * Mathf.SmoothStep(0.18f, 0.62f, n + (1f - r) * 0.45f);
                float shade = 0.72f + 0.28f * v;
                px[y * size + x] = new Color(shade, shade, shade, a);
            }
            return Finish(tex, px);
        }

        /// <summary>A flame tongue: a hot core in a teardrop that narrows toward the top.</summary>
        public static Texture2D Flame(int w = 64, int h = 128)
        {
            var tex = NewClamped(w, h, "Flame");
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float v = (y + 0.5f) / h;
                // Width swells from the base, peaks low, then tapers to a point.
                float width = Mathf.Sin(Mathf.Clamp01(v * 1.25f) * Mathf.PI * 0.5f) * Mathf.Pow(1f - v, 0.75f) * 1.15f;
                float across = width > 0.001f ? Mathf.Clamp01(1f - Mathf.Abs(u) / width) : 0f;
                float a = Mathf.Pow(across, 1.4f) * Mathf.Clamp01(v * 8f);
                float heat = Mathf.Lerp(1f, 0.55f, v);
                px[y * w + x] = new Color(heat, heat, heat, a);
            }
            return Finish(tex, px);
        }

        /// <summary>Blast scorch: a charred centre with streaks thrown outward, fading to nothing.</summary>
        public static Texture2D Scorch(int size = 256, int seed = 0)
        {
            var tex = NewClamped(size, size, "Scorch");
            var px = new Color32[size * size];
            float o = seed * 5.3f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float rays = Mathf.PerlinNoise(ang * 2.2f + o, o) * 0.55f + Mathf.PerlinNoise(ang * 7f + o, 3f) * 0.25f;
                float grit = Mathf.PerlinNoise(dx * 9f + o, dy * 9f - o);
                float reach = 0.42f + rays * 0.55f;
                float a = Mathf.Clamp01(1f - Mathf.SmoothStep(reach * 0.35f, reach, r)) * (0.75f + grit * 0.25f);
                float c = Mathf.Lerp(0.02f, 0.07f, grit);
                px[y * size + x] = new Color(c, c * 0.95f, c * 0.9f, a * 0.92f);
            }
            return Finish(tex, px);
        }

        /// <summary>An irregular puddle outline with a soft, damp rim.</summary>
        public static Texture2D Puddle(int size = 256, int seed = 0)
        {
            var tex = NewClamped(size, size, "Puddle");
            var px = new Color32[size * size];
            float o = seed * 9.1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float r = Radius(x, y, size);
                float n = Mathf.PerlinNoise(u * 3.1f + o, v * 3.1f + o) * 0.7f
                        + Mathf.PerlinNoise(u * 8f - o, v * 8f + o) * 0.3f;
                float edge = r + (n - 0.5f) * 0.62f;
                float water = Mathf.SmoothStep(0.72f, 0.58f, edge);
                float damp = Mathf.SmoothStep(0.9f, 0.62f, edge) * 0.45f;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Max(water * 0.9f, damp));
            }
            return Finish(tex, px);
        }

        /// <summary>
        /// Smudges and droplet marks on the "lens". Bloom multiplies its glow by this, so a
        /// muzzle flash or lightning strike briefly lights up grime on the glass - the same
        /// trick film cameras get for free in the rain.
        /// </summary>
        public static Texture2D LensDirt(int w = 512, int h = 288, int seed = 3)
        {
            var tex = New(w, h, "LensDirt");
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[w * h];

            var prev = Random.state;
            Random.InitState(seed);

            // Large faint smears.
            for (int i = 0; i < 14; i++)
                Blob(px, w, h, Random.Range(0f, w), Random.Range(0f, h), Random.Range(40f, 120f), Random.Range(0.05f, 0.14f));
            // Droplet marks: small, brighter, with a crisp rim.
            for (int i = 0; i < 90; i++)
                Blob(px, w, h, Random.Range(0f, w), Random.Range(0f, h), Random.Range(3f, 14f), Random.Range(0.12f, 0.45f));

            Random.state = prev;

            var px32 = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                float c = Mathf.Clamp01(px[i].r);
                px32[i] = new Color(c * 0.92f, c * 0.96f, c, 1f);
            }
            return Finish(tex, px32);
        }

        // ------------------------------------------------------------------------ utils

        static void Blob(Color[] px, int w, int h, float cx, float cy, float radius, float strength)
        {
            int x0 = Mathf.Max(0, (int)(cx - radius)), x1 = Mathf.Min(w - 1, (int)(cx + radius));
            int y0 = Mathf.Max(0, (int)(cy - radius)), y1 = Mathf.Min(h - 1, (int)(cy + radius));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d >= 1f) continue;
                float rim = Mathf.Exp(-((d - 0.85f) / 0.1f) * ((d - 0.85f) / 0.1f)) * 0.6f;
                px[y * w + x].r += strength * ((1f - d * d) * 0.6f + rim);
            }
        }

        static float Radius(int x, int y, int size)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Texture2D NewClamped(int w, int h, string name)
        {
            var t = New(w, h, name);
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        static Texture2D Finish(Texture2D tex, Color32[] px)
        {
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }
    }
}
