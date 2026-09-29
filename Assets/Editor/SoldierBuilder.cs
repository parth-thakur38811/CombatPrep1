using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using CombatPrep.Core;
using CombatPrep.Player;

namespace CombatPrep.EditorTools
{
    /// <summary>
    /// Builds the online soldier from Assets/Art/Characters (run by ArtBuilder):
    ///
    /// 1. Soldier/russian_soldier.glb, a rigged and skinned glTF model, becomes a mesh, URP
    ///    materials and a prefab in Art/Generated/Soldier. Unity doesn't read glTF, and this one
    ///    file is small and fixed, so it's read here directly rather than through a package.
    ///    Its Humanoid avatar is built by the game at runtime (Core/HumanoidRig).
    /// 2. Every FBX under Mixamo/ is imported as a Humanoid clip, sorted by its file name into
    ///    idle / walk / run / sprint / crouch / fire / death, and wired into an Animator
    ///    Controller: a blend tree on the soldier's velocity and crouch, an upper-body firing
    ///    layer and a death state. Mixamo's files may not be redistributed, so that folder - and
    ///    the controller made from it - stay out of the repository; without them the online
    ///    avatars fall back to the primitive soldier.
    /// </summary>
    public static class SoldierBuilder
    {
        public const string SourcePath = ArtBuilder.Root + "/Characters/Soldier/russian_soldier.glb";
        public const string MixamoDir = ArtBuilder.Root + "/Characters/Mixamo";
        const string OutDir = ArtBuilder.Root + "/Generated/Soldier";
        const string MaterialsDir = ArtBuilder.Root + "/Materials";
        const string ControllerPath = MixamoDir + "/Generated/SoldierAnimator.controller";

        /// <summary>Smoothness added to the model's own: everyone out here is soaked.</summary>
        const float Wet = 0.12f;

        public static bool IsMixamo(string path) =>
            path.StartsWith(MixamoDir + "/") && !path.Contains("/Generated/");

        public static ArtLibrary.Character Build()
        {
            if (!File.Exists(SourcePath))
            {
                Debug.LogWarning($"[Art] soldier: {SourcePath} missing - online players stay primitive.");
                return null;
            }

            GameObject prefab;
            try
            {
                prefab = BuildPrefab();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Art] soldier: couldn't build from {SourcePath}: {e.Message}\n{e.StackTrace}");
                return null;
            }

            var character = new ArtLibrary.Character { Prefab = prefab };
            BuildAnimator(character);
            return character;
        }

        // =================================================================== model

        static GameObject BuildPrefab()
        {
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(MaterialsDir);

            var (g, bin) = ReadGlb(SourcePath);
            string folder = Path.GetDirectoryName(SourcePath).Replace('\\', '/');

            var materials = g.materials.Select(m => BuildMaterial(g, m, folder)).ToArray();

            // Built in a preview scene, so making and discarding it never touches the open scene.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var made = new Transform[g.nodes.Length];
                int top = g.scenes[Mathf.Clamp(g.scene, 0, g.scenes.Length - 1)].nodes[0];
                var root = CreateNode(g, top, null, made);
                SceneManager.MoveGameObjectToScene(root.gameObject, scene);

                int meshNode = Array.FindIndex(g.nodes, n => n.mesh >= 0 && n.skin >= 0);
                if (meshNode < 0) throw new InvalidDataException("no skinned mesh in the file");
                var node = g.nodes[meshNode];
                var skin = g.skins[node.skin];
                var gm = g.meshes[node.mesh];

                var mesh = SaveMesh(BuildMesh(g, bin, gm, skin), OutDir + "/Soldier_Mesh.asset");
                var bones = skin.joints.Select(j => made[j]).ToArray();
                var hips = bones.FirstOrDefault(b => b.name == "Hips") ?? bones[0];

                var smr = made[meshNode].gameObject.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = hips;
                smr.sharedMaterials = gm.primitives.Select(p => p.material >= 0 ? materials[p.material] : null).ToArray();
                smr.quality = SkinQuality.Bone4;
                smr.updateWhenOffscreen = false;
                // Bounds around the hips, big enough for any pose - lying dead or reaching up.
                smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 2.6f);

                // The Humanoid avatar is built by the game itself (HumanoidRig): an avatar saved
                // as an asset from here dropped out of the player build. Building one now just
                // proves the joints make a human, so a bad model fails here and not in a match.
                var check = HumanoidRig.Build(root.gameObject);
                if (check == null) throw new InvalidDataException("the joints don't make a Humanoid avatar");
                UnityEngine.Object.DestroyImmediate(check);
                AssetDatabase.DeleteAsset(OutDir + "/Soldier_Avatar.asset");   // from earlier builds

                var animator = root.gameObject.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                return PrefabUtility.SaveAsPrefabAsset(root.gameObject, OutDir + "/Soldier.prefab");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Transform CreateNode(Gltf g, int index, Transform parent, Transform[] made)
        {
            var n = g.nodes[index];
            var t = new GameObject(string.IsNullOrEmpty(n.name) ? $"Node{index}" : n.name).transform;
            if (parent != null) t.SetParent(parent, false);

            // glTF is right-handed and Unity left-handed: mirror X throughout.
            if (n.matrix != null && n.matrix.Length == 16)
            {
                var m = Mirror(ColumnMajor(n.matrix, 0));
                t.localPosition = m.GetPosition();
                t.localRotation = m.rotation;
                t.localScale = m.lossyScale;
            }
            else
            {
                if (n.translation != null && n.translation.Length == 3)
                    t.localPosition = new Vector3(-n.translation[0], n.translation[1], n.translation[2]);
                if (n.rotation != null && n.rotation.Length == 4)
                    t.localRotation = new Quaternion(n.rotation[0], -n.rotation[1], -n.rotation[2], n.rotation[3]);
                if (n.scale != null && n.scale.Length == 3)
                    t.localScale = new Vector3(n.scale[0], n.scale[1], n.scale[2]);
            }

            made[index] = t;
            if (n.children != null)
                foreach (int c in n.children) CreateNode(g, c, t, made);
            return t;
        }

        /// <summary>One mesh, one submesh per glTF primitive (one per material).</summary>
        static Mesh BuildMesh(Gltf g, byte[] bin, GMesh gm, GSkin skin)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var submeshes = new List<int[]>();
            bool hasTangents = gm.primitives.All(p => p.attributes.TANGENT >= 0);

            foreach (var p in gm.primitives)
            {
                if (p.mode != 4) throw new InvalidDataException("only triangle lists are supported");
                int first = positions.Count;

                var pos = Floats(g, bin, p.attributes.POSITION);
                int count = pos.Length / 3;
                for (int i = 0; i < count; i++)
                    positions.Add(new Vector3(-pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]));

                var nor = Floats(g, bin, p.attributes.NORMAL);
                for (int i = 0; i < count; i++)
                    normals.Add(new Vector3(-nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2]));

                if (hasTangents)
                {
                    // Mirroring flips the tangent frame's handedness, so w flips with x.
                    var tan = Floats(g, bin, p.attributes.TANGENT);
                    for (int i = 0; i < count; i++)
                        tangents.Add(new Vector4(-tan[i * 4], tan[i * 4 + 1], tan[i * 4 + 2], -tan[i * 4 + 3]));
                }

                // glTF's texture origin is top-left, Unity's bottom-left.
                var uv = Floats(g, bin, p.attributes.TEXCOORD_0);
                for (int i = 0; i < count; i++)
                    uvs.Add(new Vector2(uv[i * 2], 1f - uv[i * 2 + 1]));

                var joints = Ints(g, bin, p.attributes.JOINTS_0);
                var jw = Floats(g, bin, p.attributes.WEIGHTS_0);
                var influences = new (int joint, float weight)[4];
                for (int i = 0; i < count; i++)
                {
                    for (int k = 0; k < 4; k++) influences[k] = (joints[i * 4 + k], jw[i * 4 + k]);
                    Array.Sort(influences, (a, b) => b.weight.CompareTo(a.weight));
                    float sum = Mathf.Max(1e-6f, influences.Sum(x => x.weight));
                    weights.Add(new BoneWeight
                    {
                        boneIndex0 = influences[0].joint, weight0 = influences[0].weight / sum,
                        boneIndex1 = influences[1].joint, weight1 = influences[1].weight / sum,
                        boneIndex2 = influences[2].joint, weight2 = influences[2].weight / sum,
                        boneIndex3 = influences[3].joint, weight3 = influences[3].weight / sum,
                    });
                }

                // Mirroring also reverses winding, so every triangle is flipped back.
                var idx = Ints(g, bin, p.indices);
                var tris = new int[idx.Length];
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    tris[i] = first + idx[i];
                    tris[i + 1] = first + idx[i + 2];
                    tris[i + 2] = first + idx[i + 1];
                }
                submeshes.Add(tris);
            }

            var mesh = new Mesh { name = "Soldier_Mesh" };
            mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            if (hasTangents) mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = weights.ToArray();
            mesh.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s, false);

            // Bind poses are glTF's inverse bind matrices, mirrored the same way as everything else.
            var ibm = Floats(g, bin, skin.inverseBindMatrices);
            var bindposes = new Matrix4x4[skin.joints.Length];
            for (int j = 0; j < bindposes.Length; j++) bindposes[j] = Mirror(ColumnMajor(ibm, j * 16));
            mesh.bindposes = bindposes;

            mesh.RecalculateBounds();
            if (!hasTangents) mesh.RecalculateTangents();
            return mesh;
        }

        static Material BuildMaterial(Gltf g, GMaterial m, string folder)
        {
            string path = $"{MaterialsDir}/{m.name}.mat";
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

            var pbr = m.pbrMetallicRoughness;
            mat.shaderKeywords = Array.Empty<string>();
            mat.SetFloat("_WorkflowMode", 1f);      // metallic
            mat.SetFloat("_Surface", 0f);           // opaque, clipped where the file says MASK
            mat.SetTexture("_BaseMap", Texture(g, pbr.baseColorTexture, folder));
            mat.SetColor("_BaseColor", pbr.baseColorFactor != null && pbr.baseColorFactor.Length == 4
                ? new Color(pbr.baseColorFactor[0], pbr.baseColorFactor[1], pbr.baseColorFactor[2], pbr.baseColorFactor[3])
                : Color.white);
            mat.SetFloat("_Metallic", pbr.metallicFactor);
            mat.SetFloat("_Smoothness", Mathf.Clamp01(1f - pbr.roughnessFactor + Wet));

            var normal = Texture(g, m.normalTexture, folder);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            if (normal != null) mat.EnableKeyword("_NORMALMAP");

            bool cutout = m.alphaMode == "MASK";
            mat.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            mat.SetFloat("_Cutoff", m.alphaCutoff);
            if (cutout)
            {
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = (int)RenderQueue.Geometry;
            }

            // Straps, lashes and the edges of the kit are single sheets: draw both sides.
            mat.SetFloat("_Cull", m.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D Texture(Gltf g, GTexRef r, string folder)
        {
            if (r == null || r.index < 0 || g.textures == null || r.index >= g.textures.Length) return null;
            int src = g.textures[r.index].source;
            if (src < 0 || g.images == null || src >= g.images.Length || string.IsNullOrEmpty(g.images[src].uri)) return null;
            string path = $"{folder}/{Uri.UnescapeDataString(g.images[src].uri)}";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null && File.Exists(path))
            {
                // On disk but not imported yet - just copied in, most likely.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            if (tex == null) Debug.LogWarning($"[Art] soldier: texture {path} missing.");
            return tex;
        }

        // ------------------------------------------------------------------ saving

        /// <summary>Updates the mesh asset in place, so its GUID - and the prefab's reference - stay put.</summary>
        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // =================================================================== animations

        enum Gait { None, Idle, Walk, Run, Sprint, CrouchIdle, CrouchWalk, Fire, Death }

        sealed class Clip
        {
            public AnimationClip Anim;
            public Gait Gait;
            public Vector2 Dir;
            public string File;
        }

        public static void Configure(ModelImporter mi)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.addCollider = false;
            mi.isReadable = false;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        /// <summary>
        /// Clip settings for a Mixamo file, from its name. Rotation and height are baked into the
        /// pose (so a crouch stays low and a strafe keeps facing forward); the forward travel is
        /// left as root motion, which the soldier's Animator discards - the network moves the body.
        /// </summary>
        public static void ConfigureClips(ModelImporter mi)
        {
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            string file = Path.GetFileNameWithoutExtension(mi.assetPath);
            var (gait, _) = Classify(file);
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                c.name = clips.Length == 1 ? file : $"{file} {i + 1}";
                c.loopTime = gait != Gait.Death && gait != Gait.None;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips;
        }

        /// <summary>Sorts a clip by the words in its file name - Mixamo's own names or the pack's.</summary>
        static (Gait gait, Vector2 dir) Classify(string fileName)
        {
            string n = " " + Regex.Replace(fileName.ToLowerInvariant(), "[^a-z]+", " ") + " ";
            bool Has(params string[] words) => words.Any(n.Contains);

            if (Has("death", "dying", " die ", " dead")) return (Gait.Death, Vector2.zero);
            if (Has("reload", "jump", "turn", "throw", "grenade", "hit ", "react", "prone", "melee", "punch"))
                return (Gait.None, Vector2.zero);
            if (Has("firing", " fire", "shoot")) return (Gait.Fire, Vector2.zero);

            bool moving = Has("walk", "run", "jog", "sprint", "straf", "move", "forward", "backward",
                              " back ", " left", " right");
            if (Has("crouch", "kneel")) return moving ? (Gait.CrouchWalk, Direction(n)) : (Gait.CrouchIdle, Vector2.zero);
            if (Has("sprint")) return (Gait.Sprint, Direction(n));
            if (Has("run", "jog")) return (Gait.Run, Direction(n));
            if (moving) return (Gait.Walk, Direction(n));
            if (Has("idle", "aim", "stand")) return (Gait.Idle, Vector2.zero);
            return (Gait.None, Vector2.zero);
        }

        static Vector2 Direction(string n)
        {
            float x = 0f, z = 0f;
            if (n.Contains("forward") || n.Contains(" fwd")) z += 1f;
            if (n.Contains("backward") || n.Contains(" back ") || n.Contains(" bwd")) z -= 1f;
            if (n.Contains(" left")) x -= 1f;
            if (n.Contains(" right")) x += 1f;
            return x == 0f && z == 0f ? Vector2.up : new Vector2(x, z).normalized;
        }

        static List<Clip> LoadClips()
        {
            var list = new List<Clip>();
            if (!Directory.Exists(MixamoDir)) return list;

            var files = Directory.GetFiles(MixamoDir, "*.fbx", SearchOption.AllDirectories)
                                 .Select(f => f.Replace('\\', '/')).Where(IsMixamo).OrderBy(f => f, StringComparer.Ordinal);
            foreach (var path in files)
            {
                string file = Path.GetFileNameWithoutExtension(path);
                var (gait, dir) = Classify(file);
                if (gait == Gait.None) continue;

                var anim = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                                        .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (anim == null) continue;
                if (!anim.isHumanMotion)
                {
                    Debug.LogWarning($"[Art] Mixamo: {file} didn't import as a Humanoid clip - skipped.");
                    continue;
                }
                list.Add(new Clip { Anim = anim, Gait = gait, Dir = dir, File = file });
            }
            return list;
        }

        static void BuildAnimator(ArtLibrary.Character character)
        {
            var clips = LoadClips();
            Clip Pick(Gait gait) =>
                clips.Where(c => c.Gait == gait)
                     .OrderByDescending(c => c.File.ToLowerInvariant().Contains("aim"))   // alert over relaxed
                     .ThenByDescending(c => c.File.ToLowerInvariant().Contains("rifle"))
                     .FirstOrDefault();
            List<Clip> Dirs(Gait gait) =>
                clips.Where(c => c.Gait == gait).GroupBy(c => c.Dir).Select(grp => grp.First()).ToList();

            var idle = Pick(Gait.Idle);
            var walk = Dirs(Gait.Walk);
            var run = Dirs(Gait.Run);
            var sprint = Dirs(Gait.Sprint).Where(c => c.Dir.y > 0.1f).ToList();
            var crouchIdle = Pick(Gait.CrouchIdle);
            var crouchWalk = Dirs(Gait.CrouchWalk);
            var fire = clips.Where(c => c.Gait == Gait.Fire)
                            .OrderBy(c => c.File.ToLowerInvariant().Contains("crouch")).FirstOrDefault();
            var death = clips.Where(c => c.Gait == Gait.Death)
                             .OrderBy(c => c.File.ToLowerInvariant().Contains("crouch"))
                             .ThenByDescending(c => c.File.ToLowerInvariant().Contains("front"))
                             .FirstOrDefault();

            if (idle == null && walk.Count == 0 && run.Count == 0)
            {
                character.Controller = null;
                Debug.Log(clips.Count == 0
                    ? $"[Art] soldier: no Mixamo animations in {MixamoDir} yet - online players stay primitive until there are."
                    : $"[Art] soldier: found {clips.Count} Mixamo clips but no idle, walk or run - online players stay primitive.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Float);
            controller.AddParameter("Firing", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            // --- base layer: locomotion, blended between standing and crouched ---
            var sm = controller.layers[0].stateMachine;
            // Without sprint clips, the forward run stretches to sprint speed instead.
            if (sprint.Count == 0)
                sprint = run.Where(c => c.Dir == Vector2.up).ToList();
            var stand = LocomotionTree(controller, "Stand", idle, new[]
            {
                // Clips are timed to the speeds players actually move at: walking pace is the
                // slow, aimed walk; the default move speed is a run.
                (walk, run.Count > 0 ? PlayerMotor.DefaultAdsSpeed : PlayerMotor.DefaultWalkSpeed),
                (run, PlayerMotor.DefaultWalkSpeed),
                (sprint, PlayerMotor.DefaultSprintSpeed),
            });
            var crouched = crouchIdle != null || crouchWalk.Count > 0
                ? LocomotionTree(controller, "Crouch", crouchIdle ?? idle,
                                 new[] { (crouchWalk, PlayerMotor.DefaultCrouchSpeed) })
                : stand;

            var locomotion = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Crouch",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(locomotion, controller);
            locomotion.AddChild(stand, 0f);
            locomotion.AddChild(crouched, 1f);

            var move = sm.AddState("Locomotion");
            move.motion = locomotion;
            sm.defaultState = move;

            if (death != null)
            {
                var die = sm.AddState("Death");
                die.motion = death.Anim;
                var fall = sm.AddAnyStateTransition(die);
                fall.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
                fall.hasExitTime = false;
                fall.duration = 0.1f;
                fall.canTransitionToSelf = false;
                var rise = die.AddTransition(move);
                rise.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
                rise.hasExitTime = false;
                rise.duration = 0f;
            }

            // --- upper-body layer: the firing pose over whatever the legs are doing ---
            if (fire != null)
            {
                var mask = UpperBodyMask();
                AssetDatabase.AddObjectToAsset(mask, controller);
                controller.AddLayer("Fire");
                var layers = controller.layers;
                var layer = layers[layers.Length - 1];
                layer.defaultWeight = 1f;
                layer.avatarMask = mask;
                layer.blendingMode = AnimatorLayerBlendingMode.Override;

                var fsm = layer.stateMachine;
                var ready = fsm.AddState("Ready");        // no motion: the base layer shows through
                var shoot = fsm.AddState("Fire");
                shoot.motion = fire.Anim;
                fsm.defaultState = ready;

                var raise = ready.AddTransition(shoot);
                raise.AddCondition(AnimatorConditionMode.If, 0f, "Firing");
                raise.hasExitTime = false;
                raise.duration = 0.08f;
                var lower = shoot.AddTransition(ready);
                lower.AddCondition(AnimatorConditionMode.IfNot, 0f, "Firing");
                lower.hasExitTime = false;
                lower.duration = 0.25f;

                controller.layers = layers;
            }

            EditorUtility.SetDirty(controller);
            character.Controller = controller;
            character.HasFire = fire != null;
            character.HasDeath = death != null;

            Debug.Log($"<b>CombatPrep</b>: soldier animations built from {clips.Count} Mixamo clips - " +
                      $"idle {(idle != null ? "yes" : "no")}, walk {walk.Count}, run {run.Count}, " +
                      $"sprint {sprint.Count}, crouch {(crouchIdle != null ? 1 : 0) + crouchWalk.Count}, " +
                      $"fire {(fire != null ? "yes" : "no")}, death {(death != null ? "yes" : "no")}.");

            SelfTest(character);
        }

        /// <summary>
        /// Poses a copy of the soldier the way the game does - runtime avatar, this controller -
        /// and logs where the body ends up standing and crouched, so a broken rig or retarget
        /// shows in the Console now rather than in a match.
        /// </summary>
        static void SelfTest(ArtLibrary.Character character)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            Avatar avatar = null;
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(character.Prefab, scene);
                avatar = HumanoidRig.Build(go);
                var a = go.GetComponent<Animator>();
                if (avatar == null || a == null)
                {
                    Debug.LogWarning("[Art] soldier self-test: no Humanoid avatar.");
                    return;
                }
                a.avatar = avatar;
                a.runtimeAnimatorController = character.Controller;
                a.Rebind();

                string Pose(string label)
                {
                    Vector3 P(HumanBodyBones b)
                    {
                        var t = a.GetBoneTransform(b);
                        return t != null ? go.transform.InverseTransformPoint(t.position) : Vector3.zero;
                    }
                    Vector3 r = P(HumanBodyBones.RightHand), l = P(HumanBodyBones.LeftHand);
                    return $"{label}: hips {P(HumanBodyBones.Hips).y:F2} m, head {P(HumanBodyBones.Head).y:F2} m, " +
                           $"right hand {r.x:F2},{r.y:F2},{r.z:F2}, left hand {l.x:F2},{l.y:F2},{l.z:F2}, " +
                           $"hands {Vector3.Distance(r, l):F2} m apart";
                }

                a.SetFloat("Crouch", 0f);
                a.Update(0f);
                a.Update(0.5f);
                string standing = Pose("standing");

                a.SetFloat("Crouch", 1f);
                a.Update(0.5f);
                a.Update(0.5f);
                string crouched = Pose("crouched");

                Debug.Log($"<b>CombatPrep</b>: soldier self-test - {standing}; {crouched}.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Art] soldier self-test failed: {e.Message}");
            }
            finally
            {
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// A 2D freeform blend on the soldier's velocity (MoveX right, MoveZ forward, m/s). Each
        /// clip sits at its direction times the speed it stands for, and plays fast or slow
        /// enough that its feet cover that ground. A missing backward clip is the forward one
        /// played in reverse.
        /// </summary>
        static BlendTree LocomotionTree(AnimatorController controller, string name, Clip idle,
                                        (List<Clip> clips, float speed)[] gaits)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveZ",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            var scales = new List<float>();
            void Add(AnimationClip clip, Vector2 dir, float speed, float direction = 1f)
            {
                tree.AddChild(clip, dir * speed);
                float natural = new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude;
                float scale = natural > 0.3f && speed > 0f ? Mathf.Clamp(speed / natural, 0.6f, 1.8f) : 1f;
                scales.Add(scale * direction);
            }

            if (idle != null) Add(idle.Anim, Vector2.zero, 0f);
            foreach (var (clips, speed) in gaits)
            {
                foreach (var c in clips) Add(c.Anim, c.Dir, speed);
                var forward = clips.FirstOrDefault(c => c.Dir == Vector2.up);
                if (forward != null && !clips.Any(c => c.Dir.y < -0.5f))
                    Add(forward.Anim, Vector2.down, speed, -1f);
            }

            var children = tree.children;
            for (int i = 0; i < children.Length; i++) children[i].timeScale = scales[i];
            tree.children = children;
            return tree;
        }

        /// <summary>
        /// Arms, hands and head only. The torso is left to the legs' layer on purpose: the
        /// humanoid "Body" part carries the hips too, and a standing firing clip would then lift
        /// a crouched soldier's hips back up. The spine still leans with the look pitch.
        /// </summary>
        static AvatarMask UpperBodyMask()
        {
            var mask = new AvatarMask { name = "UpperBody" };
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, false);
            foreach (var part in new[]
                     {
                         AvatarMaskBodyPart.Head,
                         AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                         AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
                         AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK
                     })
                mask.SetHumanoidBodyPartActive(part, true);
            return mask;
        }

        // =================================================================== glTF reading

        // Just enough of glTF 2.0 for one skinned model, in the shape JsonUtility can fill.
        // Absent indices read as -1 through the field initialisers.
#pragma warning disable 0649
        [Serializable] sealed class Gltf
        {
            public int scene;
            public GScene[] scenes;
            public GNode[] nodes;
            public GMesh[] meshes;
            public GSkin[] skins;
            public GAccessor[] accessors;
            public GView[] bufferViews;
            public GMaterial[] materials;
            public GTexture[] textures;
            public GImage[] images;
        }
        [Serializable] sealed class GScene { public int[] nodes; }
        [Serializable] sealed class GNode
        {
            public string name;
            public int[] children;
            public int mesh = -1, skin = -1;
            public float[] translation, rotation, scale, matrix;
        }
        [Serializable] sealed class GMesh { public string name; public GPrimitive[] primitives; }
        [Serializable] sealed class GPrimitive
        {
            public GAttributes attributes;
            public int indices = -1, material = -1, mode = 4;
        }
        [Serializable] sealed class GAttributes
        {
            public int POSITION = -1, NORMAL = -1, TANGENT = -1, TEXCOORD_0 = -1, JOINTS_0 = -1, WEIGHTS_0 = -1;
        }
        [Serializable] sealed class GSkin { public int[] joints; public int inverseBindMatrices = -1, skeleton = -1; }
        [Serializable] sealed class GAccessor
        {
            public int bufferView = -1, byteOffset, componentType, count;
            public string type;
            public bool normalized;
        }
        [Serializable] sealed class GView { public int buffer, byteOffset, byteLength, byteStride; }
        [Serializable] sealed class GMaterial
        {
            public string name;
            public GPbr pbrMetallicRoughness;
            public GTexRef normalTexture;
            public string alphaMode = "OPAQUE";
            public float alphaCutoff = 0.5f;
            public bool doubleSided;
        }
        [Serializable] sealed class GPbr
        {
            public float[] baseColorFactor;
            public GTexRef baseColorTexture;
            public float metallicFactor = 1f, roughnessFactor = 1f;
        }
        [Serializable] sealed class GTexRef { public int index = -1; }
        [Serializable] sealed class GTexture { public int source = -1; }
        [Serializable] sealed class GImage { public string uri, mimeType, name; public int bufferView = -1; }
#pragma warning restore 0649

        static (Gltf, byte[]) ReadGlb(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67)
                throw new InvalidDataException("not a binary glTF (.glb) file");

            int jsonLength = BitConverter.ToInt32(bytes, 12);
            var gltf = JsonUtility.FromJson<Gltf>(Encoding.UTF8.GetString(bytes, 20, jsonLength));

            int binAt = 20 + jsonLength;
            var bin = Array.Empty<byte>();
            if (binAt + 8 <= bytes.Length)
            {
                int binLength = BitConverter.ToInt32(bytes, binAt);
                bin = new byte[binLength];
                Buffer.BlockCopy(bytes, binAt + 8, bin, 0, binLength);
            }
            return (gltf, bin);
        }

        static int Components(string type) => type switch
        {
            "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16,
            _ => throw new InvalidDataException($"unsupported accessor type {type}")
        };

        static int ComponentSize(int componentType) => componentType switch
        {
            5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
            _ => throw new InvalidDataException($"unsupported component type {componentType}")
        };

        static float[] Floats(Gltf g, byte[] bin, int accessor)
        {
            if (accessor < 0) throw new InvalidDataException("a required attribute is missing");
            var a = g.accessors[accessor];
            var v = g.bufferViews[a.bufferView];
            int comps = Components(a.type), size = ComponentSize(a.componentType);
            int stride = v.byteStride > 0 ? v.byteStride : comps * size;
            int start = v.byteOffset + a.byteOffset;
            var result = new float[a.count * comps];
            for (int i = 0; i < a.count; i++)
            for (int c = 0; c < comps; c++)
            {
                int o = start + i * stride + c * size;
                result[i * comps + c] = a.componentType switch
                {
                    5126 => BitConverter.ToSingle(bin, o),
                    5123 => a.normalized ? BitConverter.ToUInt16(bin, o) / 65535f : BitConverter.ToUInt16(bin, o),
                    5121 => a.normalized ? bin[o] / 255f : bin[o],
                    5125 => BitConverter.ToUInt32(bin, o),
                    5122 => a.normalized ? Mathf.Max(BitConverter.ToInt16(bin, o) / 32767f, -1f) : BitConverter.ToInt16(bin, o),
                    _ => a.normalized ? Mathf.Max((sbyte)bin[o] / 127f, -1f) : (sbyte)bin[o],
                };
            }
            return result;
        }

        static int[] Ints(Gltf g, byte[] bin, int accessor)
        {
            if (accessor < 0) throw new InvalidDataException("a required attribute is missing");
            var a = g.accessors[accessor];
            var v = g.bufferViews[a.bufferView];
            int comps = Components(a.type), size = ComponentSize(a.componentType);
            int stride = v.byteStride > 0 ? v.byteStride : comps * size;
            int start = v.byteOffset + a.byteOffset;
            var result = new int[a.count * comps];
            for (int i = 0; i < a.count; i++)
            for (int c = 0; c < comps; c++)
            {
                int o = start + i * stride + c * size;
                result[i * comps + c] = a.componentType switch
                {
                    5125 => (int)BitConverter.ToUInt32(bin, o),
                    5123 => BitConverter.ToUInt16(bin, o),
                    5121 => bin[o],
                    _ => throw new InvalidDataException($"component type {a.componentType} isn't an index type")
                };
            }
            return result;
        }

        /// <summary>A glTF matrix (column-major floats) as a Unity one.</summary>
        static Matrix4x4 ColumnMajor(float[] m, int at)
        {
            var r = new Matrix4x4();
            for (int c = 0; c < 4; c++)
            for (int row = 0; row < 4; row++)
                r[row, c] = m[at + c * 4 + row];
            return r;
        }

        /// <summary>Right-handed to left-handed: conjugate by a mirror in X.</summary>
        static Matrix4x4 Mirror(Matrix4x4 m)
        {
            var s = new Vector4(-1f, 1f, 1f, 1f);
            var r = new Matrix4x4();
            for (int row = 0; row < 4; row++)
            for (int c = 0; c < 4; c++)
                r[row, c] = m[row, c] * s[row] * s[c];
            return r;
        }
    }
}
