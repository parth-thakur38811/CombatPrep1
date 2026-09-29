using UnityEngine;
using UnityEngine.UI;
using CombatPrep.Core;
using CombatPrep.Skins;
using CombatPrep.Targets;
using CombatPrep.Weapons;

namespace CombatPrep.Net
{
    /// <summary>Drives a remote player's avatar: smoothed aim pitch and a camera-facing nameplate.</summary>
    public class AvatarView : MonoBehaviour
    {
        public Transform AimPivot;
        public Transform Nameplate;
        public Text NameText;
        public WeaponModel Weapon;

        float _pitch, _targetPitch;

        /// <summary>Pitch arrives in network steps; it is eased so the arms don't stutter.</summary>
        public void SetPitch(float degrees) => _targetPitch = degrees;

        public void SetName(string n)
        {
            if (NameText != null) NameText.text = n;
        }

        /// <summary>
        /// Hides a dead player: renderers *and* colliders, so a corpse can't be seen, shot or
        /// walked into during the respawn wait.
        /// </summary>
        public void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = visible;
            if (Nameplate != null) Nameplate.gameObject.SetActive(visible);
        }

        /// <summary>Where this player's shots appear to leave from, for others' tracers and flash.</summary>
        public Vector3 MuzzlePosition => Weapon != null && Weapon.Muzzle != null
            ? Weapon.Muzzle.position
            : transform.position + Vector3.up * 1.4f;

        void LateUpdate()
        {
            _pitch = Mathf.Lerp(_pitch, _targetPitch, 1f - Mathf.Exp(-18f * Time.deltaTime));
            if (AimPivot != null)
                AimPivot.localRotation = Quaternion.Euler(Mathf.Clamp(_pitch, -70f, 70f), 0f, 0f);

            var cam = Camera.main;
            if (Nameplate != null && cam != null)
                Nameplate.rotation = Quaternion.LookRotation(Nameplate.position - cam.transform.position);
        }
    }

    /// <summary>
    /// Builds the third-person body other players see: a blocky primitive soldier - muted
    /// uniform, plate carrier, helmet, balaclava - holding their chosen weapon in their chosen
    /// finish. The player's slot colour survives only where it helps: an armband, a band on
    /// the helmet and the nameplate, so you can tell who's who without anyone glowing like a
    /// toy in the gloom.
    ///
    /// Arms, head and gun hang off an aim pivot at the shoulders, so the whole upper body
    /// tilts with the owner's synced look pitch - you can see where someone is aiming.
    /// Every body part carries a collider on the remote-player layer, which both stops you
    /// walking through other players and is what your shots will hit.
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

        static readonly Color Webbing = new(0.12f, 0.13f, 0.11f);
        static readonly Color Balaclava = new(0.10f, 0.10f, 0.10f);

        /// <param name="owner">What every hitzone reports damage to - the player's RemoteHitProxy.</param>
        public static AvatarView Build(Transform parent, IDamageable owner, int slot, string displayName,
                                       WeaponShape shape, SkinDefinition skin)
        {
            _owner = owner;
            Color id = Palette[Mathf.Abs(slot) % Palette.Length];
            Color uniform = Uniforms[Mathf.Abs(slot) % Uniforms.Length];
            Color trousers = uniform * 0.82f; trousers.a = 1f;

            var root = Prim.Empty(parent, "Avatar");
            var view = root.gameObject.AddComponent<AvatarView>();

            // Hitzones are the Part() boxes below and are unchanged by the kit; everything
            // added with a bare Prim.Box is visual only, with no collider to catch a bullet.

            // --- lower body: stays upright, turns with the root's synced yaw ---
            Part(root, "LegL", new Vector3(-0.11f, 0.40f, 0f), new Vector3(0.18f, 0.80f, 0.20f), trousers, Zone.Limb);
            Part(root, "LegR", new Vector3(0.11f, 0.40f, 0f), new Vector3(0.18f, 0.80f, 0.20f), trousers, Zone.Limb);
            Prim.Box(root, "BootL", new Vector3(-0.11f, 0.06f, 0.03f), new Vector3(0.2f, 0.12f, 0.28f), Webbing, 0f, 0.45f);
            Prim.Box(root, "BootR", new Vector3(0.11f, 0.06f, 0.03f), new Vector3(0.2f, 0.12f, 0.28f), Webbing, 0f, 0.45f);
            Part(root, "Torso", new Vector3(0f, 1.11f, 0f), new Vector3(0.50f, 0.62f, 0.28f), uniform, Zone.Body);

            // Plate carrier and magazine pouches.
            Prim.Box(root, "Vest", new Vector3(0f, 1.16f, 0f), new Vector3(0.54f, 0.44f, 0.33f), Webbing, 0f, 0.3f);
            for (int i = -1; i <= 1; i++)
                Prim.Box(root, "Pouch", new Vector3(i * 0.13f, 1.04f, 0.18f), new Vector3(0.11f, 0.15f, 0.06f),
                         Webbing * 1.3f, 0f, 0.3f);

            // --- upper body: tilts with look pitch ---
            var aim = Prim.Empty(root, "AimPivot", new Vector3(0f, 1.40f, 0f));
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

            BuildNameplate(root, view, displayName, id);

            WeaponModelBuilder.SetLayerRecursive(root, PlayerRigBuilder.RemotePlayerLayer);
            return view;
        }

        // Set for the duration of one Build call, so Part() needn't thread it through.
        static IDamageable _owner;

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

        static void BuildNameplate(Transform root, AvatarView view, string displayName, Color accent)
        {
            var go = new GameObject("Nameplate", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 2.15f, 0f);
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
