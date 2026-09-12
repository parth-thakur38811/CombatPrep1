using System.Collections.Generic;
using UnityEngine;
using CombatPrep.Core;
using CombatPrep.Weapons;

namespace CombatPrep.Skins
{
    public enum PatternKind { None, Camo, Digital, Stripes, Hex, Weathered }

    /// <summary>
    /// A skin is pure data: a colour per part group, plus an optional generated pattern
    /// applied to selected groups. Because the weapon is ~20 individually tagged primitives
    /// and every texture is drawn in code, this buys unlimited skins for no assets at all.
    /// </summary>
    public class SkinDefinition
    {
        public string Id;
        public string DisplayName;
        public Color Swatch;                 // menu preview colour

        public Color Receiver = Mat.Gunmetal;
        public Color Furniture = Mat.Polymer;
        public Color Barrel = Mat.Steel;
        public Color Magazine = Mat.Polymer;
        public Color Optic = Mat.Gunmetal;
        public Color Accent = Mat.Steel;

        public PatternKind Pattern = PatternKind.None;
        public Color[] PatternPalette;
        public PartGroup[] PatternGroups;
        public float PatternScale = 6f;
        public int Seed;

        public float Metallic = 0.5f;
        public float Smoothness = 0.35f;
        public float AccentEmission;         // > 0 makes accents glow

        public Color ColorFor(PartGroup g) => g switch
        {
            PartGroup.Receiver => Receiver,
            PartGroup.Furniture => Furniture,
            PartGroup.Barrel => Barrel,
            PartGroup.Magazine => Magazine,
            PartGroup.Optic => Optic,
            _ => Accent
        };
    }

    public static class SkinLibrary
    {
        static SkinDefinition[] _all;
        public static SkinDefinition[] All => _all ??= Build();

        static SkinDefinition[] Build() => new[]
        {
            new SkinDefinition
            {
                Id = "factory", DisplayName = "FACTORY", Swatch = new Color(0.20f, 0.21f, 0.23f),
                Metallic = 0.6f, Smoothness = 0.40f
            },

            new SkinDefinition
            {
                Id = "desert", DisplayName = "DESERT TAN", Swatch = new Color(0.76f, 0.63f, 0.42f),
                Receiver = new Color(0.72f, 0.60f, 0.40f),
                Furniture = new Color(0.64f, 0.53f, 0.35f),
                Barrel = new Color(0.38f, 0.34f, 0.29f),
                Magazine = new Color(0.66f, 0.55f, 0.36f),
                Optic = new Color(0.55f, 0.47f, 0.33f),
                Accent = new Color(0.42f, 0.38f, 0.32f),
                Pattern = PatternKind.Weathered,
                PatternPalette = new[] { new Color(0.74f, 0.62f, 0.41f), Mat.RustDark },
                PatternGroups = new[] { PartGroup.Receiver, PartGroup.Furniture, PartGroup.Magazine },
                Metallic = 0.25f, Smoothness = 0.22f, Seed = 2
            },

            new SkinDefinition
            {
                Id = "woodland", DisplayName = "WOODLAND", Swatch = new Color(0.30f, 0.40f, 0.22f),
                Receiver = Color.white, Furniture = Color.white, Magazine = Color.white,
                Barrel = new Color(0.24f, 0.25f, 0.22f),
                Optic = new Color(0.26f, 0.31f, 0.22f),
                Accent = new Color(0.35f, 0.38f, 0.30f),
                Pattern = PatternKind.Camo,
                PatternPalette = new[]
                {
                    new Color(0.20f, 0.26f, 0.16f), new Color(0.32f, 0.40f, 0.22f),
                    new Color(0.45f, 0.44f, 0.28f), new Color(0.17f, 0.17f, 0.14f)
                },
                PatternGroups = new[] { PartGroup.Receiver, PartGroup.Furniture, PartGroup.Magazine, PartGroup.Optic },
                PatternScale = 7f, Metallic = 0.2f, Smoothness = 0.20f, Seed = 5
            },

            new SkinDefinition
            {
                Id = "urban", DisplayName = "URBAN DIGITAL", Swatch = new Color(0.55f, 0.57f, 0.60f),
                Receiver = Color.white, Furniture = Color.white, Magazine = Color.white,
                Barrel = new Color(0.30f, 0.31f, 0.33f),
                Optic = new Color(0.34f, 0.36f, 0.38f),
                Accent = new Color(0.62f, 0.64f, 0.66f),
                Pattern = PatternKind.Digital,
                PatternPalette = new[]
                {
                    new Color(0.18f, 0.19f, 0.21f), new Color(0.40f, 0.42f, 0.45f),
                    new Color(0.62f, 0.64f, 0.67f), new Color(0.28f, 0.30f, 0.33f)
                },
                PatternGroups = new[] { PartGroup.Receiver, PartGroup.Furniture, PartGroup.Magazine },
                Metallic = 0.45f, Smoothness = 0.35f, Seed = 8
            },

            new SkinDefinition
            {
                Id = "redline", DisplayName = "REDLINE", Swatch = new Color(0.82f, 0.14f, 0.12f),
                Receiver = Color.white, Furniture = new Color(0.09f, 0.09f, 0.10f),
                Barrel = new Color(0.12f, 0.12f, 0.13f),
                Magazine = Color.white,
                Optic = new Color(0.10f, 0.10f, 0.11f),
                Accent = new Color(0.90f, 0.16f, 0.13f),
                Pattern = PatternKind.Stripes,
                PatternPalette = new[] { new Color(0.10f, 0.10f, 0.11f), new Color(0.82f, 0.14f, 0.12f) },
                PatternGroups = new[] { PartGroup.Receiver, PartGroup.Magazine },
                Metallic = 0.7f, Smoothness = 0.72f, AccentEmission = 1.6f, Seed = 12
            },

            new SkinDefinition
            {
                Id = "cyber", DisplayName = "NIGHTWIRE", Swatch = new Color(0.10f, 0.85f, 0.85f),
                Receiver = Color.white, Furniture = new Color(0.07f, 0.08f, 0.11f),
                Barrel = new Color(0.10f, 0.11f, 0.14f),
                Magazine = Color.white,
                Optic = new Color(0.08f, 0.09f, 0.12f),
                Accent = new Color(0.12f, 0.92f, 0.92f),
                Pattern = PatternKind.Hex,
                PatternPalette = new[] { new Color(0.08f, 0.10f, 0.14f), new Color(0.10f, 0.55f, 0.58f) },
                PatternGroups = new[] { PartGroup.Receiver, PartGroup.Magazine },
                Metallic = 0.8f, Smoothness = 0.80f, AccentEmission = 3.2f, Seed = 17
            },
        };
    }

    /// <summary>Generates each skin's texture once, then paints a built weapon with it.</summary>
    public static class SkinApplier
    {
        static readonly Dictionary<string, Texture2D> PatternCache = new();

        public static void Apply(WeaponModel model, SkinDefinition skin)
        {
            Texture2D pattern = GetPattern(skin);
            var patternGroups = skin.PatternGroups;

            // One material per (group) rather than per part, so the SRP batcher stays happy.
            var byGroup = new Dictionary<PartGroup, Material>();

            foreach (var part in model.Parts)
            {
                if (part == null || part.Renderer == null) continue;

                if (!byGroup.TryGetValue(part.Group, out var mat))
                {
                    Color c = skin.ColorFor(part.Group);
                    bool patterned = pattern != null && Contains(patternGroups, part.Group);

                    if (patterned)
                        mat = Mat.Textured(pattern, skin.Metallic, skin.Smoothness, c);
                    else if (part.Group == PartGroup.Accent && skin.AccentEmission > 0f)
                        mat = Mat.Get(c, skin.Metallic, skin.Smoothness, skin.AccentEmission);
                    else
                        mat = Mat.Get(c, skin.Metallic, skin.Smoothness);

                    byGroup[part.Group] = mat;
                }

                part.Renderer.sharedMaterial = mat;
            }
        }

        static bool Contains(PartGroup[] groups, PartGroup g)
        {
            if (groups == null) return false;
            foreach (var x in groups) if (x == g) return true;
            return false;
        }

        static Texture2D GetPattern(SkinDefinition s)
        {
            if (s.Pattern == PatternKind.None || s.PatternPalette == null) return null;
            if (PatternCache.TryGetValue(s.Id, out var cached) && cached != null) return cached;

            Texture2D tex = s.Pattern switch
            {
                PatternKind.Camo => Tex.Camo(s.PatternPalette, s.PatternScale, s.Seed),
                PatternKind.Digital => Tex.Digital(s.PatternPalette, 26, s.Seed),
                PatternKind.Stripes => Tex.Stripes(s.PatternPalette[0], s.PatternPalette[1], 7, 32f, 0.42f),
                PatternKind.Hex => Tex.Hex(s.PatternPalette[0], s.PatternPalette[1]),
                PatternKind.Weathered => Tex.Weathered(s.PatternPalette[0], s.PatternPalette[1], 0.45f, s.Seed),
                _ => null
            };

            PatternCache[s.Id] = tex;
            return tex;
        }
    }
}
