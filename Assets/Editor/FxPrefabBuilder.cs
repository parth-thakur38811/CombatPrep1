using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using CombatPrep.Core;
using CombatPrep.FX;

namespace CombatPrep.EditorTools
{
    /// <summary>
    /// Makes the effect prefabs in Assets/Prefabs/FX from FxRecipes - once. A prefab that
    /// already exists is left exactly as it is, so anything tuned in the editor survives every
    /// rebuild. Delete one to get the recipe's version back, or use
    /// CombatPrep > Rebuild FX Prefabs to reset them all.
    ///
    /// Recipes make their textures and materials in memory, which a prefab can't point at, so
    /// those are saved beside the prefabs first - textures as PNGs, materials as assets - with
    /// identical ones shared rather than saved twice.
    /// </summary>
    public static class FxPrefabBuilder
    {
        public const string Dir = "Assets/Prefabs/FX";
        const string TexDir = Dir + "/Textures";
        const string MatDir = Dir + "/Materials";

        static readonly (string name, Func<GameObject> recipe)[] Recipes =
        {
            ("MuzzleFlash", FxRecipes.MuzzleFlash),
            ("Tracer", FxRecipes.Tracer),
            ("Impact", FxRecipes.Impact),
            ("BulletHole", FxRecipes.BulletHole),
            ("Explosion", FxRecipes.Explosion),
            ("Rain", () => FxRecipes.Rain()),
            ("Fire", FxRecipes.Fire),
            ("BurningWreck", FxRecipes.BurningWreck),
            ("Puddle", FxRecipes.Puddle),
        };

        // Content-keyed, so the same texture or material made twice is saved once.
        static readonly Dictionary<string, Texture2D> SavedTextures = new();
        static readonly Dictionary<string, Material> SavedMaterials = new();
        static readonly Dictionary<Material, Material> Resolved = new();

        public static bool AnyMissing() =>
            Recipes.Any(r => AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{r.name}.prefab") == null);

        public static ArtLibrary.Effects Build(bool overwrite = false)
        {
            SavedTextures.Clear();
            SavedMaterials.Clear();
            Resolved.Clear();
            Preload();

            var made = new Dictionary<string, GameObject>();
            int created = 0;
            foreach (var (name, recipe) in Recipes)
            {
                string path = $"{Dir}/{name}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || overwrite)
                {
                    prefab = Create(name, recipe, path);
                    if (prefab != null) created++;
                }
                made[name] = prefab;
            }
            if (created > 0) Debug.Log($"<b>CombatPrep</b>: made {created} effect prefab(s) in {Dir}.");

            return new ArtLibrary.Effects
            {
                MuzzleFlash = made["MuzzleFlash"],
                Tracer = made["Tracer"],
                Impact = made["Impact"],
                BulletHole = made["BulletHole"],
                Explosion = made["Explosion"],
                Rain = made["Rain"],
                Fire = made["Fire"],
                BurningWreck = made["BurningWreck"],
                Puddle = made["Puddle"],
            };
        }

        [MenuItem("CombatPrep/Rebuild FX Prefabs")]
        static void RebuildAll()
        {
            if (!EditorUtility.DisplayDialog("Rebuild FX prefabs",
                    $"Replace every prefab in {Dir} with its original recipe? Changes made to them in the editor will be lost.",
                    "Replace", "Cancel"))
                return;

            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(ArtBuilder.LibraryPath);
            var fx = Build(overwrite: true);
            if (lib != null)
            {
                lib.Fx = fx;
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
            }
        }

        static GameObject Create(string name, Func<GameObject> recipe, string path)
        {
            Directory.CreateDirectory(TexDir);
            Directory.CreateDirectory(MatDir);

            // Built in a preview scene, so making and discarding it never touches the open scene.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = recipe();
                go.name = name;
                SceneManager.MoveGameObjectToScene(go, scene);
                Persist(go);
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FX] couldn't make {name}: {e.Message}\n{e.StackTrace}");
                return null;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // ------------------------------------------------------------------ saving

        /// <summary>What's already saved, so remaking one prefab reuses the others' files.</summary>
        static void Preload()
        {
            if (Directory.Exists(TexDir))
                foreach (var f in Directory.GetFiles(TexDir, "*.png"))
                {
                    var path = f.Replace('\\', '/');
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null) SavedTextures[Hash(File.ReadAllBytes(path))] = tex;
                }
            if (Directory.Exists(MatDir))
                foreach (var f in Directory.GetFiles(MatDir, "*.mat"))
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(f.Replace('\\', '/'));
                    if (mat != null) SavedMaterials[Signature(mat)] = mat;
                }
        }

        static string Hash(byte[] bytes) =>
            Convert.ToBase64String(System.Security.Cryptography.MD5.Create().ComputeHash(bytes));

        static void Persist(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = SaveMaterial(mats[i]);
                r.sharedMaterials = mats;
                if (r is ParticleSystemRenderer pr && pr.trailMaterial != null)
                    pr.trailMaterial = SaveMaterial(pr.trailMaterial);
            }
            foreach (var puddle in root.GetComponentsInChildren<PuddleFx>(true))
                for (int i = 0; i < puddle.Variants.Length; i++)
                    puddle.Variants[i] = SaveMaterial(puddle.Variants[i]);
        }

        static Material SaveMaterial(Material m)
        {
            if (m == null || EditorUtility.IsPersistent(m)) return m;
            if (Resolved.TryGetValue(m, out var done)) return done;

            // Textures first, so the material's signature names saved files.
            string mainTex = "Plain";
            foreach (string prop in m.GetTexturePropertyNames())
            {
                if (!(m.GetTexture(prop) is Texture2D tex)) continue;
                if (!EditorUtility.IsPersistent(tex)) m.SetTexture(prop, tex = SaveTexture(tex));
                if (prop == "_BaseMap") mainTex = tex.name;
            }

            string key = Signature(m);
            if (!SavedMaterials.TryGetValue(key, out var saved))
            {
                string kind = m.name.Replace("Gen_", "").Replace("Particle", "");
                string path = AssetDatabase.GenerateUniqueAssetPath($"{MatDir}/{mainTex}_{kind}.mat");
                AssetDatabase.CreateAsset(m, path);
                saved = m;
                SavedMaterials[key] = saved;
            }
            Resolved[m] = saved;
            return saved;
        }

        static string Signature(Material m)
        {
            var sb = new StringBuilder(m.shader != null ? m.shader.name : "?");
            sb.Append('|').Append(m.renderQueue).Append('|');
            foreach (var k in m.shaderKeywords.OrderBy(k => k)) sb.Append(k).Append(',');
            foreach (var p in m.GetPropertyNames(MaterialPropertyType.Float).OrderBy(p => p))
                sb.Append(p).Append('=').Append(m.GetFloat(p).ToString("R")).Append(';');
            foreach (var p in m.GetPropertyNames(MaterialPropertyType.Vector).OrderBy(p => p))
                sb.Append(p).Append('=').Append(m.GetVector(p).ToString("R")).Append(';');
            foreach (var p in m.GetTexturePropertyNames().OrderBy(p => p))
            {
                var t = m.GetTexture(p);
                sb.Append(p).Append('=').Append(t != null ? AssetDatabase.GetAssetPath(t) : "").Append(';');
            }
            return sb.ToString();
        }

        static Texture2D SaveTexture(Texture2D tex)
        {
            var png = tex.EncodeToPNG();
            string key = Hash(png);
            if (SavedTextures.TryGetValue(key, out var saved)) return saved;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{TexDir}/{tex.name}.png");
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = tex.wrapMode;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();

            saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            SavedTextures[key] = saved;
            return saved;
        }
    }
}
