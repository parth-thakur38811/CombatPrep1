using UnityEngine;
using UnityEngine.UI;
using CombatPrep.Core;
using CombatPrep.Skins;
using CombatPrep.Targets;
using CombatPrep.Weapons;

namespace CombatPrep.Net
{
    /// <summary>
    /// What NetPlayer drives on a remote player's body, whichever body it is: smoothed aim
    /// pitch, crouch stance, shots, death and a camera-facing nameplate.
    /// </summary>
    public class AvatarView : MonoBehaviour
    {
        public Transform Nameplate;
        public Text NameText;
        public WeaponModel Weapon;

        /// <summary>Look pitch in degrees, eased - positive is looking down.</summary>
        protected float Pitch;
        /// <summary>Standing (0) to crouched (1), eased at the owner's own stance speed.</summary>
        protected float Stance;

        float _targetPitch, _stanceTarget;

        /// <summary>Pitch arrives in network steps; it is eased so the arms don't stutter.</summary>
        public void SetPitch(float degrees) => _targetPitch = degrees;

        /// <summary>Crouch arrives as a flag; the pose eases in rather than snapping.</summary>
        public void SetCrouch(bool crouched) => _stanceTarget = crouched ? 1f : 0f;

        public void SetName(string n)
        {
            if (NameText != null) NameText.text = n;
        }

        /// <summary>
        /// Hides a dead player: renderers *and* colliders, so a corpse can't be seen, shot or
        /// walked into during the respawn wait.
        /// </summary>
        public virtual void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
            SetColliders(visible);
            if (Nameplate != null) Nameplate.gameObject.SetActive(visible);
        }

        protected void SetColliders(bool on)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        /// <summary>This player fired - everyone else's copy of them shows it.</summary>
        public virtual void OnShot() { }

        public virtual void OnDied() => SetVisible(false);

        public virtual void OnRespawned() => SetVisible(true);

        /// <summary>Where this player's shots appear to leave from, for others' tracers and flash.</summary>
        public Vector3 MuzzlePosition => Weapon != null && Weapon.Muzzle != null
            ? Weapon.Muzzle.position
            : transform.position + Vector3.up * 1.4f;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Pitch = Mathf.Lerp(Pitch, _targetPitch, 1f - Mathf.Exp(-18f * dt));
            Stance = Mathf.Lerp(Stance, _stanceTarget, 1f - Mathf.Exp(-10f * dt));
            if (Mathf.Abs(Stance - _stanceTarget) < 0.002f) Stance = _stanceTarget;

            Pose();

            var cam = Camera.main;
            if (Nameplate != null && cam != null)
                Nameplate.rotation = Quaternion.LookRotation(Nameplate.position - cam.transform.position);
        }

        /// <summary>Applies pitch and stance to the body, once a frame, after any animation.</summary>
        protected virtual void Pose() { }
    }

    /// <summary>
    /// The primitive soldier's pose: no animation, just joints set from pitch and stance.
    /// </summary>
    public class BlockyAvatarView : AvatarView
    {
        /// <summary>Height of the hip joints when standing - where the legs meet the upper body.</summary>
        public const float HipHeight = 0.80f;

        /// <summary>
        /// How far the head drops when crouched: exactly what the owner's own camera drops
        /// (PlayerMotor: 1.8 m stance to 1.15 m, eye 1.62 m to 0.97 m). The body others can see
        /// and shoot behind cover is then the body its owner is actually hiding.
        /// </summary>
        const float CrouchDrop = 0.65f;

        public Transform AimPivot;
        public Transform Upper;
        public Transform HipL, HipR, KneeL, KneeR;

        float _posedStance = -1f;

        protected override void Pose()
        {
            if (AimPivot != null)
                AimPivot.localRotation = Quaternion.Euler(Mathf.Clamp(Pitch, -70f, 70f), 0f, 0f);

            if (Stance != _posedStance)
            {
                ApplyStance(Stance);
                _posedStance = Stance;
            }
        }

        /// <summary>
        /// Standing (0) to crouched (1). The crouch is a tactical kneel - left foot planted,
        /// right knee down - with the upper body sunk and leaning a touch forward. The hitboxes
        /// ride on these same joints, so they crouch too.
        /// </summary>
        void ApplyStance(float c)
        {
            if (Upper == null) return;

            Upper.localPosition = new Vector3(0f, HipHeight - CrouchDrop * c, 0.03f * c);
            Upper.localRotation = Quaternion.Euler(6f * c, 0f, 0f);

            float hipY = HipHeight - 0.33f * c;
            HipL.localPosition = new Vector3(-0.11f, hipY, 0f);
            HipR.localPosition = new Vector3(0.11f, hipY, 0f);

            // Left thigh swings forward, shin drops straight to the planted foot.
            HipL.localRotation = Quaternion.Euler(-80f * c, 0f, 0f);
            KneeL.localRotation = Quaternion.Euler(80f * c, 0f, 0f);
            // Right thigh points down to a knee on the ground, shin lies back along it.
            HipR.localRotation = Quaternion.Euler(10f * c, 0f, 0f);
            KneeR.localRotation = Quaternion.Euler(80f * c, 0f, 0f);
        }
    }

    /// <summary>
    /// Builds the third-person body other players see, holding their chosen weapon in their
    /// chosen finish. The player's slot colour survives only where it helps: an armband and
    /// the nameplate (plus a helmet band on the primitive soldier), so you can tell who's who
    /// without anyone glowing like a toy in the gloom.
    ///
    /// With the art library's soldier and its Mixamo animations present, that body is the
    /// rigged soldier (SoldierView); otherwise it is a blocky primitive soldier - muted
    /// uniform, plate carrier, helmet, balaclava - so a fresh clone still plays. Either way,
    /// every body part carries a collider on the remote-player layer, which both stops you
    /// walking through other players and is what your shots hit.
    /// </summary>
    public static class AvatarBuilder
    {
        /// <summary>One distinct identity colour per player slot: armband, helmet band, nameplate.</summary>
        public static readonly Color[] Palette =
        {
            new Color(0.95f, 0.45f, 0.12f),   // orange
            new Color(0.15f, 0.72f, 0.92f),   // cyan
            new Color(0.55f, 0.85f, 0.20f),   // lime
            new Color(0.88f, 0.28f, 0.72f),   // magenta
        };

        /// <summary>Uniform per slot - different enough to tell apart, all of them drab.</summary>
        static readonly Color[] Uniforms =
        {
            new Color(0.30f, 0.31f, 0.24f),   // olive
            new Color(0.36f, 0.33f, 0.26f),   // coyote
            new Color(0.25f, 0.27f, 0.29f),   // urban grey
            new Color(0.21f, 0.23f, 0.19f),   // dark green
        };

        /// <summary>
        /// The same four, as tints over the soldier's camouflage print: as issued, sun-bleached
        /// toward coyote, washed out to grey, and a darker woodland.
        /// </summary>
        static readonly Color[] CamoTints =
        {
            new Color(1.00f, 1.00f, 1.00f),
            new Color(1.12f, 1.00f, 0.78f),
            new Color(0.86f, 0.90f, 0.96f),
            new Color(0.78f, 0.86f, 0.74f),
        };

        static readonly Color Webbing = new(0.12f, 0.13f, 0.11f);
        static readonly Color Balaclava = new(0.10f, 0.10f, 0.10f);

        /// <param name="owner">What every hitzone reports damage to - the player's RemoteHitProxy.</param>
        public static AvatarView Build(Transform parent, IDamageable owner, int slot, string displayName,
                                       WeaponShape shape, SkinDefinition skin)
        {
            _owner = owner;
            var art = ArtLibrary.I;
            if (art != null && ArtLibrary.Has(art.Soldier))
            {
                var soldier = BuildSoldier(parent, slot, displayName, shape, skin, art.Soldier);
                if (soldier != null) return soldier;
            }
            return BuildBlocky(parent, slot, displayName, shape, skin);
        }

        // Set for the duration of one Build call, so Part() needn't thread it through.
        static IDamageable _owner;

        // ------------------------------------------------------------------ rigged soldier

        static AvatarView BuildSoldier(Transform parent, int slot, string displayName, WeaponShape shape,
                                       SkinDefinition skin, ArtLibrary.Character art)
        {
            var root = Prim.Empty(parent, "Avatar");
            var body = Object.Instantiate(art.Prefab, root, false);
            body.name = "Soldier";
            var animator = body.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[Avatar] Soldier prefab has no Humanoid avatar - using the primitive soldier.");
                Object.Destroy(root.gameObject);
                return null;
            }

            animator.runtimeAnimatorController = art.Controller;
            animator.applyRootMotion = false;                             // the network moves the body
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;     // hitboxes ride the bones

            // Hitboxes and the armband are sized from the model's rest pose, which is what the
            // bones still hold now - the Animator hasn't evaluated yet.
            AddHitboxes(animator, root);

            Color id = Palette[Mathf.Abs(slot) % Palette.Length];
            TintUniform(body, CamoTints[Mathf.Abs(slot) % CamoTints.Length]);
            AddArmband(animator, id);

            var view = root.gameObject.AddComponent<SoldierView>();
            var mount = Prim.Empty(root, "GunMount");
            var model = WeaponModelBuilder.Build(mount, shape);
            SkinApplier.Apply(model, skin);
            view.Init(animator, model, mount, art.HasDeath);

            BuildNameplate(root, 2.1f, view, displayName, id);

            WeaponModelBuilder.SetLayerRecursive(root, PlayerRigBuilder.RemotePlayerLayer);
            return view;
        }

        /// <summary>
        /// Hit zones as boxes riding the bones: head (with the helmet), chest and belly, and each
        /// limb segment. They follow the animation, so a crouched or leaning player is hit
        /// exactly where they appear.
        /// </summary>
        static void AddHitboxes(Animator a, Transform root)
        {
            Vector3 up = root.up;
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);

            var head = B(HumanBodyBones.Head);
            var hips = B(HumanBodyBones.Hips);
            var chest = B(HumanBodyBones.Chest) != null ? B(HumanBodyBones.Chest) : B(HumanBodyBones.Spine);
            var neck = B(HumanBodyBones.Neck) != null ? B(HumanBodyBones.Neck) : head;

            if (head != null)
                HitBox(head, head.position - up * 0.04f, head.position + up * 0.27f, 0.25f, 0.29f, Zone.Head, root);
            if (chest != null && neck != null)
                HitBox(chest, chest.position, neck.position + up * 0.02f, 0.44f, 0.32f, Zone.Body, root);
            if (hips != null && chest != null)
                HitBox(hips, hips.position - up * 0.12f, chest.position, 0.38f, 0.28f, Zone.Body, root);

            Limb(a, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.13f, root);
            Limb(a, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.11f, root);
            Limb(a, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.13f, root);
            Limb(a, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.11f, root);
            Limb(a, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.18f, root);
            Limb(a, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.15f, root);
            Limb(a, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.18f, root);
            Limb(a, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.15f, root);
        }

        static void Limb(Animator a, HumanBodyBones from, HumanBodyBones to, float thickness, Transform root)
        {
            var f = a.GetBoneTransform(from);
            var t = a.GetBoneTransform(to);
            if (f != null && t != null) HitBox(f, f.position, t.position, thickness, thickness, Zone.Limb, root);
        }

        /// <summary>A box from <paramref name="start"/> to <paramref name="end"/>, facing the way the body faces.</summary>
        static void HitBox(Transform bone, Vector3 start, Vector3 end, float width, float depth, Zone zone, Transform root)
        {
            Vector3 along = end - start;
            float length = along.magnitude;
            if (length < 0.01f) return;

            // Local Y down the bone, local Z as close to the body's forward as that allows.
            Vector3 dir = along / length;
            Vector3 fwd = Vector3.ProjectOnPlane(root.forward, dir);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(root.up, dir);

            var go = new GameObject("Hit" + zone);
            var t = go.transform;
            t.SetParent(bone, false);
            t.SetPositionAndRotation((start + end) * 0.5f, Quaternion.LookRotation(fwd, dir));
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(width, length, depth);

            var hz = go.AddComponent<HitZone>();
            hz.Owner = _owner;
            hz.Zone = zone;
        }

        /// <summary>Per-slot camo tint on everything but skin, via a property block so the shared materials stay shared.</summary>
        static void TintUniform(GameObject body, Color tint)
        {
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name.Contains("skin")) continue;
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_BaseColor", tint);
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        /// <summary>A band of the player's colour round the left upper arm.</summary>
        static void AddArmband(Animator a, Color id)
        {
            var arm = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var elbow = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            if (arm == null || elbow == null) return;

            Vector3 along = elbow.position - arm.position;
            var band = Prim.Pillar(arm, "Armband", Vector3.zero, 0.135f, 0.055f, id, 0f, 0.4f);
            band.SetPositionAndRotation(arm.position + along * 0.32f,
                                        Quaternion.FromToRotation(Vector3.up, along.normalized));
        }

        // ------------------------------------------------------------------ primitive soldier

        static AvatarView BuildBlocky(Transform parent, int slot, string displayName, WeaponShape shape,
                                      SkinDefinition skin)
        {
            Color id = Palette[Mathf.Abs(slot) % Palette.Length];
            Color uniform = Uniforms[Mathf.Abs(slot) % Uniforms.Length];
            Color trousers = uniform * 0.82f; trousers.a = 1f;

            var root = Prim.Empty(parent, "Avatar");
            var view = root.gameObject.AddComponent<BlockyAvatarView>();

            // Hitzones are the Part() boxes below and are unchanged by the kit; everything
            // added with a bare Prim.Box is visual only, with no collider to catch a bullet.

            // --- legs: hip and knee joints, so a crouch folds them rather than squashing them.
            // Everything turns with the root's synced yaw. ---
            view.HipL = Leg(root, "L", -0.11f, trousers, out view.KneeL);
            view.HipR = Leg(root, "R", 0.11f, trousers, out view.KneeR);

            // --- upper body: everything from the hips up, lowered as one when crouching ---
            var upper = Prim.Empty(root, "Upper", new Vector3(0f, BlockyAvatarView.HipHeight, 0f));
            view.Upper = upper;
            Part(upper, "Torso", new Vector3(0f, 0.31f, 0f), new Vector3(0.50f, 0.62f, 0.28f), uniform, Zone.Body);

            // Plate carrier and magazine pouches.
            Prim.Box(upper, "Vest", new Vector3(0f, 0.36f, 0f), new Vector3(0.54f, 0.44f, 0.33f), Webbing, 0f, 0.3f);
            for (int i = -1; i <= 1; i++)
                Prim.Box(upper, "Pouch", new Vector3(i * 0.13f, 0.24f, 0.18f), new Vector3(0.11f, 0.15f, 0.06f),
                         Webbing * 1.3f, 0f, 0.3f);

            // --- shoulders up: tilts with look pitch ---
            var aim = Prim.Empty(upper, "AimPivot", new Vector3(0f, 0.60f, 0f));
            view.AimPivot = aim;

            Part(aim, "Head", new Vector3(0f, 0.24f, 0f), new Vector3(0.30f, 0.30f, 0.30f), Balaclava, Zone.Head);
            // Goggles, so which way the head faces reads at a glance.
            Prim.Box(aim, "Goggles", new Vector3(0f, 0.27f, 0.152f), new Vector3(0.24f, 0.07f, 0.01f),
                     new Color(0.06f, 0.07f, 0.08f), 0.6f, 0.85f);
            Prim.Box(aim, "Helmet", new Vector3(0f, 0.40f, -0.01f), new Vector3(0.36f, 0.14f, 0.38f),
                     uniform * 0.75f, 0f, 0.35f);
            Prim.Box(aim, "HelmetBand", new Vector3(0f, 0.36f, -0.01f), new Vector3(0.365f, 0.035f, 0.385f), id, 0f, 0.4f);

            Part(aim, "ArmR", new Vector3(0.21f, -0.06f, 0.20f), new Vector3(0.13f, 0.13f, 0.46f), uniform, Zone.Limb);
            Part(aim, "ArmL", new Vector3(-0.10f, -0.10f, 0.33f), new Vector3(0.12f, 0.12f, 0.44f), uniform, Zone.Limb,
                 new Vector3(0f, 26f, 0f));
            Prim.Box(aim, "Armband", new Vector3(0.21f, -0.06f, 0.06f), new Vector3(0.14f, 0.14f, 0.07f), id, 0f, 0.4f);

            // --- their actual gun, in their actual finish ---
            var gunMount = Prim.Empty(aim, "GunMount", new Vector3(0.10f, -0.08f, 0.42f));
            var model = WeaponModelBuilder.Build(gunMount, shape);
            SkinApplier.Apply(model, skin);
            view.Weapon = model;

            BuildNameplate(upper, 2.15f - BlockyAvatarView.HipHeight, view, displayName, id);

            WeaponModelBuilder.SetLayerRecursive(root, PlayerRigBuilder.RemotePlayerLayer);
            return view;
        }

        /// <summary>One leg: hip joint, thigh, knee joint, shin and boot. Both segments are hitboxes.</summary>
        static Transform Leg(Transform root, string side, float x, Color trousers, out Transform knee)
        {
            var hip = Prim.Empty(root, "Hip" + side, new Vector3(x, BlockyAvatarView.HipHeight, 0f));
            Part(hip, "Thigh" + side, new Vector3(0f, -0.21f, 0f), new Vector3(0.18f, 0.42f, 0.20f), trousers, Zone.Limb);
            knee = Prim.Empty(hip, "Knee" + side, new Vector3(0f, -0.42f, 0f));
            Part(knee, "Shin" + side, new Vector3(0f, -0.19f, 0f), new Vector3(0.17f, 0.38f, 0.19f), trousers, Zone.Limb);
            Prim.Box(knee, "Boot" + side, new Vector3(0f, -0.32f, 0.03f), new Vector3(0.2f, 0.12f, 0.28f), Webbing, 0f, 0.45f);
            return hip;
        }

        /// <summary>A body part: solid collider plus a hitzone reporting to this player.</summary>
        static Transform Part(Transform parent, string name, Vector3 pos, Vector3 size, Color c,
                              Zone zone, Vector3 euler = default)
        {
            var t = Prim.Box(parent, name, pos, size, c, 0.05f, 0.35f, collider: true, euler: euler);
            var hz = t.gameObject.AddComponent<HitZone>();
            hz.Owner = _owner;
            hz.Zone = zone;
            return t;
        }

        static void BuildNameplate(Transform parent, float height, AvatarView view, string displayName, Color accent)
        {
            var go = new GameObject("Nameplate", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localScale = Vector3.one * 0.01f;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 50f);

            var textGo = new GameObject("Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            textGo.transform.SetParent(go.transform, false);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 34;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.Lerp(accent, Color.white, 0.35f);
            text.text = displayName;

            var outline = textGo.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            view.Nameplate = go.transform;
            view.NameText = text;
        }
    }
}
