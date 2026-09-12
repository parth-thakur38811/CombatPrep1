using System.Collections.Generic;
using UnityEngine;
using CombatPrep.Core;

namespace CombatPrep.Weapons
{
    /// <summary>Which surface of the gun a part belongs to. The skin system recolours by group.</summary>
    public enum PartGroup { Receiver, Furniture, Barrel, Magazine, Optic, Accent }

    public enum OpticType { Iron, RedDot, Scope }

    /// <summary>Marks a primitive as part of the weapon, so skins can find and retint it.</summary>
    public class WeaponPart : MonoBehaviour
    {
        public PartGroup Group;
        public Renderer Renderer;
    }

    /// <summary>Proportions for one weapon class. Everything visual is driven from here.</summary>
    [System.Serializable]
    public struct WeaponShape
    {
        public float ReceiverLen, ReceiverH, ReceiverW;
        public float BarrelLen, BarrelDia;
        public float HandguardLen;
        public bool Stock; public float StockLen;
        public bool Magazine; public float MagLen; public float MagWidth;
        public OpticType Optic; public float OpticBore; public float OpticLen;
        public float SightDistance;      // camera-to-sight distance when aiming
        public bool Foregrip;
        public bool Suppressor;
        public bool PumpAction;
    }

    public class WeaponModel
    {
        public Transform Root;
        public Transform Muzzle;      // tracer / muzzle-flash origin, at the tip
        public Transform EjectPort;
        public Transform SightPoint;  // bore centre; the ADS pose is solved from this
        public float SightDistance = 0.22f;
        public readonly List<WeaponPart> Parts = new();
    }

    /// <summary>
    /// Assembles a weapon out of Unity primitives. Local space is +Z forward (down the
    /// barrel), +Y up, origin at the receiver.
    ///
    /// Optics are built as rings of segment boxes rather than solid blocks, so the bore is
    /// genuinely open and you aim *through* the sight instead of at the back of it. The
    /// reticle is an emissive object floating on the sight axis; because the ADS pose is
    /// solved to put SightPoint dead centre, it lands exactly on the screen centre.
    /// </summary>
    public static class WeaponModelBuilder
    {
        static Material _reticleMat, _glassMat;

        static Material Reticle => _reticleMat ??= Mat.Additive(new Color(1f, 0.18f, 0.12f));
        static Material Glass => _glassMat ??= Mat.Glass(new Color(0.55f, 0.72f, 0.78f, 0.13f));

        public static WeaponModel Build(Transform parent, WeaponShape s)
        {
            var model = new WeaponModel { SightDistance = s.SightDistance };
            var root = Prim.Empty(parent, "Weapon");
            model.Root = root;

            float halfRec = s.ReceiverLen * 0.5f;

            // --- receiver ---
            Add(model, PartGroup.Receiver, Prim.Box(root, "Receiver", new Vector3(0f, 0f, 0.02f),
                new Vector3(s.ReceiverW, s.ReceiverH, s.ReceiverLen), Mat.Gunmetal, 0.65f, 0.42f));
            Add(model, PartGroup.Receiver, Prim.Box(root, "UpperRail", new Vector3(0f, s.ReceiverH * 0.60f, 0.02f),
                new Vector3(s.ReceiverW * 0.55f, 0.018f, s.ReceiverLen * 1.05f), Mat.Gunmetal, 0.7f, 0.45f));
            Add(model, PartGroup.Receiver, Prim.Box(root, "EjectionPort",
                new Vector3(s.ReceiverW * 0.52f, s.ReceiverH * 0.20f, 0.07f),
                new Vector3(0.010f, s.ReceiverH * 0.36f, 0.06f), Mat.Steel, 0.8f, 0.55f));

            // --- handguard ---
            if (s.HandguardLen > 0.01f)
            {
                float hgZ = halfRec + s.HandguardLen * 0.5f - 0.01f;
                Add(model, PartGroup.Furniture, Prim.Box(root, "Handguard", new Vector3(0f, -0.002f, hgZ),
                    new Vector3(s.ReceiverW * 1.04f, s.ReceiverH * 0.72f, s.HandguardLen), Mat.Polymer, 0.05f, 0.28f));

                if (s.Foregrip)
                    Add(model, PartGroup.Furniture, Prim.Box(root, "Foregrip",
                        new Vector3(0f, -s.ReceiverH * 0.75f, hgZ + s.HandguardLen * 0.18f),
                        new Vector3(0.036f, 0.095f, 0.040f), Mat.Polymer, 0.05f, 0.3f));

                if (s.PumpAction)
                    Add(model, PartGroup.Furniture, Prim.Box(root, "Pump", new Vector3(0f, -s.ReceiverH * 0.42f, hgZ),
                        new Vector3(s.ReceiverW * 1.25f, 0.050f, s.HandguardLen * 0.55f), Mat.Wood, 0.05f, 0.35f));
            }

            // --- grip ---
            Add(model, PartGroup.Furniture, Prim.Box(root, "Grip",
                new Vector3(0f, -s.ReceiverH * 0.95f, -halfRec * 0.42f),
                new Vector3(0.036f, 0.118f, 0.048f), Mat.Polymer, 0.05f, 0.30f, false, new Vector3(20f, 0f, 0f)));

            // --- stock ---
            if (s.Stock)
            {
                float stockZ = -halfRec - s.StockLen * 0.5f;
                Add(model, PartGroup.Furniture, Prim.Box(root, "BufferTube",
                    new Vector3(0f, 0.010f, -halfRec - s.StockLen * 0.22f),
                    new Vector3(0.038f, 0.042f, s.StockLen * 0.5f), Mat.Gunmetal, 0.6f, 0.4f));
                Add(model, PartGroup.Furniture, Prim.Box(root, "Stock", new Vector3(0f, -0.004f, stockZ - 0.03f),
                    new Vector3(0.050f, 0.100f, s.StockLen * 0.55f), Mat.Polymer, 0.05f, 0.26f));
                Add(model, PartGroup.Furniture, Prim.Box(root, "CheekRest",
                    new Vector3(0f, 0.046f, stockZ - 0.02f),
                    new Vector3(0.042f, 0.022f, s.StockLen * 0.7f), Mat.Polymer, 0.05f, 0.26f));
            }

            // --- barrel ---
            float barrelZ = halfRec + s.HandguardLen + s.BarrelLen * 0.5f - 0.02f;
            Add(model, PartGroup.Barrel, Prim.Tube(root, "Barrel", new Vector3(0f, 0.004f, barrelZ),
                s.BarrelDia, s.BarrelLen, Mat.Steel, 0.85f, 0.55f));

            float tipZ = barrelZ + s.BarrelLen * 0.5f;
            if (s.Suppressor)
            {
                Add(model, PartGroup.Barrel, Prim.Tube(root, "Suppressor", new Vector3(0f, 0.004f, tipZ + 0.055f),
                    s.BarrelDia * 2.1f, 0.12f, Mat.Gunmetal, 0.6f, 0.35f));
                tipZ += 0.115f;
            }
            else
            {
                Add(model, PartGroup.Barrel, Prim.Tube(root, "MuzzleBrake", new Vector3(0f, 0.004f, tipZ + 0.026f),
                    s.BarrelDia * 1.5f, 0.055f, Mat.Gunmetal, 0.8f, 0.5f));
                tipZ += 0.055f;
            }

            // --- magazine ---
            if (s.Magazine)
            {
                Add(model, PartGroup.Magazine, Prim.Box(root, "Magazine",
                    new Vector3(0f, -s.ReceiverH * 0.5f - s.MagLen * 0.5f, 0.055f),
                    new Vector3(s.MagWidth, s.MagLen, 0.072f), Mat.Polymer, 0.05f, 0.30f,
                    false, new Vector3(-9f, 0f, 0f)));
                Add(model, PartGroup.Magazine, Prim.Box(root, "MagWell",
                    new Vector3(0f, -s.ReceiverH * 0.45f, 0.050f),
                    new Vector3(s.MagWidth * 1.35f, 0.050f, 0.086f), Mat.Gunmetal, 0.6f, 0.4f));
            }

            // --- trigger group ---
            Add(model, PartGroup.Receiver, Prim.Box(root, "TriggerGuard",
                new Vector3(0f, -s.ReceiverH * 0.66f, -0.012f),
                new Vector3(0.024f, 0.006f, 0.060f), Mat.Gunmetal, 0.6f, 0.4f));
            Add(model, PartGroup.Accent, Prim.Box(root, "Trigger",
                new Vector3(0f, -s.ReceiverH * 0.46f, -0.014f),
                new Vector3(0.010f, 0.028f, 0.010f), Mat.Steel, 0.8f, 0.6f));

            // --- sights ---
            float sightY = s.ReceiverH * 0.60f + 0.012f;
            BuildOptic(model, root, s, sightY);

            // --- reference points ---
            model.Muzzle = Prim.Empty(root, "MuzzleTip", new Vector3(0f, 0.004f, tipZ + 0.03f));
            model.EjectPort = Prim.Empty(root, "EjectOrigin",
                new Vector3(s.ReceiverW * 0.7f, s.ReceiverH * 0.2f, 0.07f));

            return model;
        }

        // -------------------------------------------------------------------- sights

        static void BuildOptic(WeaponModel model, Transform root, WeaponShape s, float railY)
        {
            switch (s.Optic)
            {
                case OpticType.Iron: BuildIronSights(model, root, s, railY); break;
                case OpticType.RedDot: BuildRingOptic(model, root, s, railY, false); break;
                case OpticType.Scope: BuildRingOptic(model, root, s, railY, true); break;
            }
        }

        /// <summary>
        /// Ring-housing optic. The bore is left completely open - this is the whole fix for
        /// a sight that was previously a solid block of geometry in front of the eye.
        /// </summary>
        static void BuildRingOptic(WeaponModel model, Transform root, WeaponShape s, float railY, bool scope)
        {
            float bore = s.OpticBore;
            float wall = scope ? 0.007f : 0.006f;
            float len = s.OpticLen;
            float outer = bore + wall * 2f;
            float axisY = railY + outer * 0.5f + 0.006f;
            float z = scope ? 0.03f : 0.01f;

            // Mount
            Add(model, PartGroup.Optic, Prim.Box(root, "OpticMount", new Vector3(0f, railY + 0.012f, z),
                new Vector3(0.030f, 0.026f, len * 0.45f), Mat.Gunmetal, 0.7f, 0.45f));

            // Front and rear rings, plus a body tube between them.
            var ringFront = Prim.Ring(root, "RingFront", new Vector3(0f, axisY, z + len * 0.5f),
                                      outer, wall, 0.016f, Mat.Gunmetal, 20);
            var ringRear = Prim.Ring(root, "RingRear", new Vector3(0f, axisY, z - len * 0.5f),
                                     outer, wall, 0.016f, Mat.Gunmetal, 20);
            var tube = Prim.Ring(root, "OpticTube", new Vector3(0f, axisY, z),
                                 outer * 0.97f, wall * 0.8f, len, Mat.Gunmetal, 20);
            AddAll(model, PartGroup.Optic, ringFront);
            AddAll(model, PartGroup.Optic, ringRear);
            AddAll(model, PartGroup.Optic, tube);

            if (scope)
            {
                // Turrets, so a magnified optic reads as one at a glance.
                Add(model, PartGroup.Optic, Prim.Pillar(root, "TurretTop", new Vector3(0f, axisY + outer * 0.5f + 0.012f, z),
                    0.030f, 0.026f, Mat.Gunmetal, 0.7f, 0.45f));
                Add(model, PartGroup.Optic, Prim.Pillar(root, "TurretSide", new Vector3(outer * 0.5f + 0.012f, axisY, z),
                    0.028f, 0.024f, Mat.Gunmetal, 0.7f, 0.45f, false, new Vector3(0f, 0f, 90f)));
            }

            // Objective glass, at the far end so the reticle always draws in front of it.
            // Deliberately not registered as a skinnable part - a skin must never repaint
            // the glass or the reticle, or the sight stops working.
            Prim.Quad(root, "Lens", new Vector3(0f, axisY, z + len * 0.5f + 0.004f),
                      new Vector2(bore, bore), Glass);

            // --- reticle, on the sight axis ---
            var reticle = Prim.Empty(root, "Reticle", new Vector3(0f, axisY, z + len * 0.34f));
            if (scope)
            {
                float arm = bore * 0.42f, t = bore * 0.018f;
                // Crosshair with a gap at the centre so the aiming point stays readable.
                MakeReticleBar(model, reticle, "Up", new Vector3(0f, arm * 0.62f, 0f), new Vector3(t, arm * 0.7f, t));
                MakeReticleBar(model, reticle, "Down", new Vector3(0f, -arm * 0.62f, 0f), new Vector3(t, arm * 0.7f, t));
                MakeReticleBar(model, reticle, "Left", new Vector3(-arm * 0.62f, 0f, 0f), new Vector3(arm * 0.7f, t, t));
                MakeReticleBar(model, reticle, "Right", new Vector3(arm * 0.62f, 0f, 0f), new Vector3(arm * 0.7f, t, t));
                MakeReticleDot(model, reticle, bore * 0.030f);
            }
            else
            {
                MakeReticleDot(model, reticle, bore * 0.058f);
            }

            // A magnified optic is aligned from just behind its rear ring, so the eye sits at
            // a realistic eye relief instead of inside the tube.
            float sightZ = scope ? z - len * 0.5f - 0.012f : z;
            model.SightPoint = Prim.Empty(root, "SightPoint", new Vector3(0f, axisY, sightZ));
        }

        static void BuildIronSights(WeaponModel model, Transform root, WeaponShape s, float railY)
        {
            float axisY = railY + 0.020f;
            float front = s.ReceiverLen * 0.5f + s.HandguardLen * 0.85f;

            // Front post inside a protective hood.
            Add(model, PartGroup.Accent, Prim.Box(root, "FrontPost", new Vector3(0f, axisY, front),
                new Vector3(0.0035f, 0.026f, 0.0035f), Mat.Gunmetal, 0.7f, 0.45f));
            Add(model, PartGroup.Optic, Prim.Box(root, "FrontHoodL", new Vector3(-0.013f, axisY, front),
                new Vector3(0.004f, 0.030f, 0.010f), Mat.Gunmetal, 0.7f, 0.45f));
            Add(model, PartGroup.Optic, Prim.Box(root, "FrontHoodR", new Vector3(0.013f, axisY, front),
                new Vector3(0.004f, 0.030f, 0.010f), Mat.Gunmetal, 0.7f, 0.45f));

            // Rear notch: two uprights with a gap you look through.
            float rear = -s.ReceiverLen * 0.35f;
            Add(model, PartGroup.Optic, Prim.Box(root, "RearL", new Vector3(-0.011f, axisY, rear),
                new Vector3(0.008f, 0.024f, 0.010f), Mat.Gunmetal, 0.7f, 0.45f));
            Add(model, PartGroup.Optic, Prim.Box(root, "RearR", new Vector3(0.011f, axisY, rear),
                new Vector3(0.008f, 0.024f, 0.010f), Mat.Gunmetal, 0.7f, 0.45f));

            // A dim emissive bead on the front post - irons are otherwise near-invisible
            // against a dark target at range.
            var bead = Prim.Ball(root, "Bead", new Vector3(0f, axisY + 0.012f, front), 0.005f, Color.white);
            Prim.SetMaterial(bead, Reticle);

            model.SightPoint = Prim.Empty(root, "SightPoint", new Vector3(0f, axisY + 0.012f, rear));
        }

        // Reticle geometry is intentionally left out of model.Parts so no skin can repaint it.

        static void MakeReticleBar(WeaponModel model, Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var t = Prim.Box(parent, name, pos, size, Color.white);
            Prim.SetMaterial(t, Reticle);
        }

        static void MakeReticleDot(WeaponModel model, Transform parent, float diameter)
        {
            var dot = Prim.Ball(parent, "Dot", Vector3.zero, diameter, Color.white);
            Prim.SetMaterial(dot, Reticle);
        }

        // --------------------------------------------------------------------- helpers

        static void Add(WeaponModel model, PartGroup group, Transform t)
        {
            var r = t.GetComponent<Renderer>();
            if (r == null) return;
            var part = t.gameObject.AddComponent<WeaponPart>();
            part.Group = group;
            part.Renderer = r;
            model.Parts.Add(part);
        }

        /// <summary>Tags every renderer under a composite (a Ring) as one group.</summary>
        static void AddAll(WeaponModel model, PartGroup group, Transform composite)
        {
            foreach (var r in composite.GetComponentsInChildren<Renderer>())
                Add(model, group, r.transform);
        }

        public static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }
    }
}
