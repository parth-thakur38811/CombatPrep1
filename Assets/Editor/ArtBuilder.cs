using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CombatPrep.Core;

namespace CombatPrep.EditorTools
{
    /// <summary>
    /// Import settings for everything under Assets/Art, applied the moment a file lands:
    /// normal maps flagged as normal maps, data maps kept linear, the HDRI made a cubemap,
    /// models imported without their own materials and with Y up - and Mixamo animations as
    /// Humanoid clips (see SoldierBuilder).
    /// </summary>
    public class ArtImportSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (ArtBuilder.IsArt(assetPath)) ArtBuilder.Configure((TextureImporter)assetImporter);
        }

        void OnPreprocessModel()
        {
            if (!ArtBuilder.IsArt(assetPath)) return;
            if (SoldierBuilder.IsMixamo(assetPath)) SoldierBuilder.Configure((ModelImporter)assetImporter);
            else ArtBuilder.Configure((ModelImporter)assetImporter);
        }

        void OnPreprocessAnimation()
        {
            if (SoldierBuilder.IsMixamo(assetPath)) SoldierBuilder.ConfigureClips((ModelImporter)assetImporter);
        }

        /// <summary>New, changed or removed source art: check whether the library needs a rebuild.</summary>
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var list in new[] { imported, deleted, moved, movedFrom })
            foreach (var path in list)
            {
                if (!ArtBuilder.IsSource(path)) continue;
                ArtAutoBuild.Schedule();
                return;
            }
        }
    }

    /// <summary>
    /// Rebuilds the art library by itself whenever source files in Assets/Art are added,
    /// changed or removed, and after any script reload or return to edit mode - so dropping
    /// new files in needs no menu click. Only if something actually changed since the last
    /// build, and never in play mode.
    /// </summary>
    [InitializeOnLoad]
    static class ArtAutoBuild
    {
        static ArtAutoBuild()
        {
            Schedule();
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredEditMode) Schedule();
            };
        }

        public static void Schedule()
        {
            EditorApplication.delayCall -= Check;
            EditorApplication.delayCall += Check;
        }

        static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Check;
                return;
            }
            if (ArtBuilder.NeedsBuild()) ArtBuilder.Build();
        }
    }

    /// <summary>
    /// Turns the downloaded Poly Haven files into what the game uses: URP Lit materials and
    /// the ArtLibrary asset that lists them.
    ///
    /// Poly Haven ships each surface as colour + OpenGL normal + an "ARM" map (occlusion,
    /// roughness, metalness in R, G, B). URP Lit wants metalness in R and smoothness in A, and
    /// reads occlusion from G - so one repacked "mask" texture (R metal, G occlusion, A
    /// smoothness) feeds both slots. The repack is also where the rain goes in: smoothness is
    /// pushed toward a wet sheen, most of all in the crevices where water would sit. Masks are
    /// written at 512 px - roughness and occlusion carry little fine detail, and it keeps the
    /// repository lean.
    /// </summary>
    public static class ArtBuilder
    {
        public const string Root = "Assets/Art";
        const string Generated = Root + "/Generated";
        const string Materials = Root + "/Materials";
        const string LibraryPath = Root + "/Resources/ArtLibrary.asset";
        const string SkyPath = Root + "/Sky/overcast_soil_puresky_2k.hdr";

        /// <summary>Bump to force every machine to rebuild after changing this file.</summary>
        const int Version = 5;
        const int MaskSize = 512;

        public static bool IsArt(string path) => path.StartsWith(Root + "/");

        /// <summary>A file the library is built *from*, as opposed to one the builder writes.</summary>
        public static bool IsSource(string path) =>
            IsArt(path) && !path.EndsWith(".meta") && !path.Contains("/Generated/")
            && !path.Contains("/Materials/") && !path.Contains("/Resources/");

        // ---------------------------------------------------------------- specs

        /// <param name="wet">How far smoothness is pushed toward a wet film (0 dry .. 1 soaked).</param>
        /// <param name="tint">Albedo multiplier: wet surfaces are darker, and it recolours the containers.</param>
        /// <param name="tiling">Material tiling, for primitives UV'd 0-1 (sandbag capsules); world-UV'd meshes use 1.</param>
        // Plain classes rather than records: records need IsExternalInit, which Unity's
        // runtime library doesn't ship.
        sealed class SurfaceSpec
        {
            public string Name, Source, Res;
            public float Tile, Wet;
            public Color Tint;
            public Vector2 Tiling;
        }

        sealed class PropSpec
        {
            public string Name, Folder, TexPrefix;
            public Vector3 Size;
            public float Wet;
            public Color Tint;

            public PropSpec(string name, string folder, string texPrefix, Vector3 size, float wet, Color tint)
            {
                Name = name; Folder = folder; TexPrefix = texPrefix; Size = size; Wet = wet; Tint = tint;
            }
        }

        static SurfaceSpec S(string name, string source, string res, float tile, float wet, Color tint, Vector2? tiling = null)
            => new SurfaceSpec { Name = name, Source = source, Res = res, Tile = tile, Wet = wet, Tint = tint,
                                 Tiling = tiling ?? Vector2.one };

        static readonly SurfaceSpec[] Surfaces =
        {
            S("ground",      "brown_mud_02",           "2k", 2.2f,  0.45f, Grey(0.85f)),
            S("berm",        "burned_ground_01",       "1k", 2.5f,  0.35f, Grey(0.90f)),
            S("floor",       "damaged_concrete_floor", "1k", 4.5f,  0.40f, Grey(0.85f)),
            S("concrete",    "concrete_layers_02",     "1k", 2.0f,  0.30f, Grey(0.66f)),
            S("rubble",      "concrete_debris",        "1k", 2.0f,  0.30f, Grey(0.78f)),
            S("container_a", "container_side",         "2k", 1.94f, 0.30f, Grey(1.00f)),
            S("container_b", "container_side",         "2k", 1.94f, 0.30f, new Color(0.80f, 0.64f, 0.56f)),
            S("container_c", "container_side",         "2k", 1.94f, 0.30f, new Color(0.62f, 0.70f, 0.82f)),
            S("sandbag_a",   "hessian_230",            "1k", 0.27f, 0.12f, new Color(0.74f, 0.68f, 0.54f), new Vector2(3.5f, 1.6f)),
            S("sandbag_b",   "hessian_230",            "1k", 0.27f, 0.12f, new Color(0.58f, 0.52f, 0.41f), new Vector2(3.5f, 1.6f)),
            S("wood",        "weathered_planks",       "1k", 2.0f,  0.30f, Grey(0.75f)),
            S("metal",       "green_metal_rust",       "1k", 1.0f,  0.25f, Grey(0.90f)),
        };

        // Sizes are Poly Haven's published dimensions, converted to Unity's Y-up axes.
        static readonly PropSpec[] Props =
        {
            new("barrel",      "Barrel_01",             "Barrel_01_oxide_green", new Vector3(0.563f, 0.880f, 0.563f), 0.30f, Grey(0.90f)),
            new("burn_barrel", "barrel_stove",          "barrel_stove",          new Vector3(0.599f, 0.856f, 0.598f), 0.20f, Grey(0.90f)),
            new("barrier",     "concrete_road_barrier", "concrete_road_barrier", new Vector3(1.545f, 0.835f, 0.641f), 0.30f, Grey(0.80f)),
            new("tyre",        "old_tyre",              "old_tyre",              new Vector3(0.600f, 0.600f, 0.165f), 0.40f, Grey(0.90f)),
            new("crate",       "wooden_crate_02",       "wooden_crate_02",       new Vector3(1.166f, 0.464f, 0.529f), 0.30f, Grey(0.80f)),
        };

        static Color Grey(float v) => new(v, v, v, 1f);

        // ---------------------------------------------------------------- import settings

        public static void Configure(TextureImporter ti)
        {
            string file = Path.GetFileNameWithoutExtension(ti.assetPath).ToLowerInvariant();
            string ext = Path.GetExtension(ti.assetPath).ToLowerInvariant();

            ti.mipmapEnabled = true;

            if (ext == ".hdr" || ext == ".exr")
            {
                ti.textureType = TextureImporterType.Default;
                ti.textureShape = TextureImporterShape.TextureCube;
                ti.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;   // lat-long panorama
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.maxTextureSize = 2048;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.anisoLevel = 0;
                return;
            }

            ti.textureShape = TextureImporterShape.Texture2D;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 8;   // ground seen at a grazing angle stays sharp
            ti.textureCompression = TextureImporterCompression.Compressed;

            if (file.Contains("_nor_gl"))
            {
                ti.textureType = TextureImporterType.NormalMap;   // OpenGL (Y+) - Unity's convention
                ti.sRGBTexture = false;
            }
            else if (file.Contains("_arm") || file.EndsWith("_mask"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;                           // data, not colour
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
            }
        }

        public static void Configure(ModelImporter mi)
        {
            mi.materialImportMode = ModelImporterMaterialImportMode.None;   // the builder supplies materials
            mi.bakeAxisConversion = true;                                    // Blender Z-up baked to Y-up
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.addCollider = false;
            mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
        }

        /// <summary>
        /// Files imported before the post-processor existed keep their old settings until
        /// reimported, so re-apply them explicitly and reimport only what actually changed.
        /// A file Unity hasn't seen yet (just copied in) is imported now, so the build below
        /// never runs ahead of its own sources; the post-processor configures it on the way in.
        /// </summary>
        static void EnsureSettings(string path)
        {
            var imp = AssetImporter.GetAtPath(path);
            if (imp == null)
            {
                AssetDatabase.ImportAsset(path);
                return;
            }
            if (imp is TextureImporter ti)
            {
                string before = Signature(ti);
                Configure(ti);
                if (Signature(ti) != before) ti.SaveAndReimport();
            }
            else if (imp is ModelImporter mi)
            {
                string before = Signature(mi);
                if (SoldierBuilder.IsMixamo(path)) SoldierBuilder.Configure(mi);
                else Configure(mi);
                if (Signature(mi) != before) mi.SaveAndReimport();
            }
        }

        static string Signature(TextureImporter t) =>
            $"{t.textureType}|{t.textureShape}|{t.sRGBTexture}|{t.generateCubemap}|{t.textureCompression}|" +
            $"{t.maxTextureSize}|{t.anisoLevel}|{t.mipmapEnabled}|{t.wrapMode}";

        static string Signature(ModelImporter m) =>
            $"{m.materialImportMode}|{m.bakeAxisConversion}|{m.importAnimation}|{m.animationType}|" +
            $"{m.importCameras}|{m.importLights}|{m.addCollider}|{m.importTangents}";

        // ---------------------------------------------------------------- staleness

        /// <summary>A fingerprint of the source files plus the builder version.</summary>
        static string ComputeStamp()
        {
            var sb = new StringBuilder($"v{Version};");
            if (!Directory.Exists(Root)) return sb.ToString();
            var files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var f in files)
            {
                var p = f.Replace('\\', '/');
                if (!IsSource(p)) continue;
                sb.Append(Path.GetFileName(p)).Append(':').Append(new FileInfo(f).Length).Append(';');
            }
            return sb.ToString();
        }

        public static bool NeedsBuild()
        {
            if (!Directory.Exists(Root + "/Textures") && !Directory.Exists(Root + "/Props")) return false;
            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibraryPath);
            return lib == null || lib.SourceStamp != ComputeStamp();
        }

        // ---------------------------------------------------------------- build

        [MenuItem("CombatPrep/Rebuild Art Library")]
        public static void Build()
        {
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));

            // 1. Import settings on every source file.
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                {
                    var p = f.Replace('\\', '/');
                    if (IsSource(p)) EnsureSettings(p);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<ArtLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            // 2. Surfaces. Masks are shared by the variants of one source (container a/b/c).
            var built = new System.Collections.Generic.Dictionary<string, (Texture2D mask, Color avg)>();
            int ok = 0;
            foreach (var s in Surfaces)
            {
                var entry = BuildSurface(s, built);
                if (entry != null) ok++;
                switch (s.Name)
                {
                    case "ground": lib.Ground = entry; break;
                    case "berm": lib.Berm = entry; break;
                    case "floor": lib.Floor = entry; break;
                    case "concrete": lib.Concrete = entry; break;
                    case "rubble": lib.Rubble = entry; break;
                    case "container_a": lib.ContainerA = entry; break;
                    case "container_b": lib.ContainerB = entry; break;
                    case "container_c": lib.ContainerC = entry; break;
                    case "sandbag_a": lib.SandbagA = entry; break;
                    case "sandbag_b": lib.SandbagB = entry; break;
                    case "wood": lib.Wood = entry; break;
                    case "metal": lib.Metal = entry; break;
                }
            }

            // 3. Props.
            int props = 0;
            foreach (var p in Props)
            {
                var entry = BuildProp(p);
                if (entry != null) props++;
                switch (p.Name)
                {
                    case "barrel": lib.Barrel = entry; break;
                    case "burn_barrel": lib.BurnBarrel = entry; break;
                    case "barrier": lib.Barrier = entry; break;
                    case "tyre": lib.Tyre = entry; break;
                    case "crate": lib.Crate = entry; break;
                }
            }

            // 4. Sky. Measured from the file: ~1.8 overhead, ~0.75 near the horizon - scaled
            // down hard, because a storm sits far darker than the overcast day it was shot on.
            lib.Sky = AssetDatabase.LoadAssetAtPath<Texture>(SkyPath);
            lib.SkyExposure = 0.12f;

            // 5. The online soldier, and its animations if the Mixamo clips are in.
            lib.Soldier = SoldierBuilder.Build();

            lib.SourceStamp = ComputeStamp();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();

            string soldier = lib.Soldier == null || lib.Soldier.Prefab == null ? "missing"
                           : lib.Soldier.Controller == null ? "model only (no animations yet)"
                           : "animated";
            Debug.Log($"<b>CombatPrep</b>: art library built - {ok}/{Surfaces.Length} surfaces, " +
                      $"{props}/{Props.Length} props, sky {(lib.Sky != null ? "yes" : "missing")}, soldier {soldier}.");
        }

        static ArtLibrary.Surface BuildSurface(SurfaceSpec s,
            System.Collections.Generic.Dictionary<string, (Texture2D mask, Color avg)> built)
        {
            string dir = $"{Root}/Textures/{s.Source}";
            string diff = $"{dir}/{s.Source}_diff_{s.Res}.jpg";
            string nor = $"{dir}/{s.Source}_nor_gl_{s.Res}.jpg";
            string arm = $"{dir}/{s.Source}_arm_{s.Res}.jpg";
            if (!File.Exists(diff) || !File.Exists(nor) || !File.Exists(arm))
            {
                Debug.LogWarning($"[Art] {s.Name}: source files missing in {dir} - using the procedural look.");
                return null;
            }

            if (!built.TryGetValue(s.Source, out var shared))
            {
                shared = (BuildMask(arm, $"{Generated}/{s.Source}_mask.png", s.Wet), AverageColour(diff));
                built[s.Source] = shared;
            }

            var mat = LitMaterial($"{Materials}/{s.Name}.mat",
                                  AssetDatabase.LoadAssetAtPath<Texture2D>(diff),
                                  AssetDatabase.LoadAssetAtPath<Texture2D>(nor),
                                  shared.mask, s.Tint, s.Tiling);

            return new ArtLibrary.Surface
            {
                Material = mat,
                TileMeters = s.Tile,
                ImpactColor = shared.avg * s.Tint
            };
        }

        static ArtLibrary.Prop BuildProp(PropSpec p)
        {
            string dir = $"{Root}/Props/{p.Folder}";
            string fbx = $"{dir}/{p.Folder}_1k.fbx";
            string diff = $"{dir}/{p.TexPrefix}_diff_1k.jpg";
            string nor = $"{dir}/{p.TexPrefix}_nor_gl_1k.jpg";
            string arm = $"{dir}/{p.TexPrefix}_arm_1k.jpg";
            if (!File.Exists(fbx) || !File.Exists(diff) || !File.Exists(nor) || !File.Exists(arm))
            {
                Debug.LogWarning($"[Art] {p.Name}: source files missing in {dir} - using the primitive prop.");
                return null;
            }

            var mask = BuildMask(arm, $"{Generated}/{p.TexPrefix}_mask.png", p.Wet);
            var mat = LitMaterial($"{Materials}/{p.Name}.mat",
                                  AssetDatabase.LoadAssetAtPath<Texture2D>(diff),
                                  AssetDatabase.LoadAssetAtPath<Texture2D>(nor),
                                  mask, p.Tint, Vector2.one);

            return new ArtLibrary.Prop
            {
                Model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx),
                Material = mat,
                Size = p.Size,
                ImpactColor = AverageColour(diff) * p.Tint
            };
        }

        // ---------------------------------------------------------------- textures

        /// <summary>ARM (R occlusion, G roughness, B metal) -> mask (R metal, G occlusion, A wet smoothness).</summary>
        static Texture2D BuildMask(string armPath, string outPath, float wet)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            src.LoadImage(File.ReadAllBytes(armPath));   // raw bytes: no colour conversion
            int sw = src.width, sh = src.height;
            var sp = src.GetPixels32();

            int fx = Mathf.Max(1, sw / MaskSize), fy = Mathf.Max(1, sh / MaskSize);
            int ow = sw / fx, oh = sh / fy;
            var px = new Color32[ow * oh];
            float inv = 1f / (fx * fy * 255f);

            for (int oy = 0; oy < oh; oy++)
            for (int ox = 0; ox < ow; ox++)
            {
                float ao = 0f, rough = 0f, metal = 0f;
                for (int y = 0; y < fy; y++)
                for (int x = 0; x < fx; x++)
                {
                    var c = sp[(oy * fy + y) * sw + ox * fx + x];
                    ao += c.r; rough += c.g; metal += c.b;
                }
                ao *= inv; rough *= inv; metal *= inv;

                // Water pools in the cavities first, so wetness follows the occlusion.
                float cavity = 1f - ao;
                float w = wet * (0.6f + 0.4f * cavity);
                float smooth = Mathf.Lerp(1f - rough, 0.86f, w);

                px[oy * ow + ox] = new Color(metal, ao, 0f, smooth);
            }

            var dst = new Texture2D(ow, oh, TextureFormat.RGBA32, false, true);
            dst.SetPixels32(px);
            dst.Apply();
            File.WriteAllBytes(outPath, dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            EnsureSettings(outPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        /// <summary>Average colour of an image, sampled on a coarse grid - what dust off it looks like.</summary>
        static Color AverageColour(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(path));
            var p = t.GetPixels32();
            long r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i < p.Length; i += 37) { r += p[i].r; g += p[i].g; b += p[i].b; n++; }
            Object.DestroyImmediate(t);
            return n == 0 ? Color.gray : new Color(r / (255f * n), g / (255f * n), b / (255f * n), 1f);
        }

        // ---------------------------------------------------------------- materials

        static Material LitMaterial(string path, Texture2D albedo, Texture2D normal, Texture2D mask,
                                    Color tint, Vector2 tiling)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.shaderKeywords = System.Array.Empty<string>();
            mat.SetFloat("_WorkflowMode", 1f);                 // metallic
            mat.SetFloat("_Surface", 0f);
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetColor("_BaseColor", tint);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetFloat("_Smoothness", 1f);                   // multiplier on the mask's alpha
            mat.SetFloat("_SmoothnessTextureChannel", 0f);     // smoothness from the metallic map's alpha
            mat.SetTexture("_OcclusionMap", mask);             // URP reads occlusion from green
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
