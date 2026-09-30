using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CombatPrep.Core;
using CombatPrep.Weapons;

namespace CombatPrep.Player
{
    /// <summary>
    /// Your own arms, holding the gun. They're the soldier model's arms - the rest of the body
    /// cut out of the mesh - reaching every frame for the grip and the fore-end of whatever gun
    /// is in hand, so they follow its kick, sway, sprint and reload without animations of their
    /// own. Each arm is a two-joint reach (shoulder to elbow to wrist) with the elbow pushed out
    /// and down; the hand is then turned to wrap the gun and its fingers curled round it.
    ///
    /// Needs the art library's soldier. Without it the gun floats as it always did.
    /// </summary>
    public class FirstPersonArms : MonoBehaviour
    {
        /// <summary>
        /// Where the shoulders sit, relative to the eye: low and well back, so the forearms come
        /// up into view from the bottom of the screen and the elbows stay out of it.
        /// </summary>
        static readonly Vector3 ShoulderMid = new(0f, -0.3f, 0f);
        /// <summary>A shade over life size: viewmodel arms read small otherwise.</summary>
        const float Scale = 1.05f;

        /// <summary>
        /// Joints whose skin is kept: the arms and hands. The shoulders sit under the eye, so the
        /// upper arms stay below the edge of the screen - but kept, they close off the sleeves.
        /// </summary>
        static readonly HashSet<string> ArmBones = new()
        {
            "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand",
        };

        WeaponLoadout _loadout;
        Transform _cam;
        Arm _right, _left;
        Renderer _renderer;
        readonly List<(Transform bone, Quaternion rest)> _rest = new();

        sealed class Arm
        {
            public Transform Upper, Lower, Hand;
            public Transform[][] Fingers;       // thumb, index, middle - three joints each
            public float UpperLength, LowerLength;
            public Vector3 FingerDirRest, PalmRest;   // in the hand's own frame, from the bind pose
            public Vector3 PalmCentre;                // from the wrist joint, in the hand's own frame
            public float Side;                        // +1 right, -1 left
        }

        public static FirstPersonArms Build(Transform cam, WeaponLoadout loadout)
        {
            var art = ArtLibrary.I;
            if (art == null || art.Soldier == null || art.Soldier.Prefab == null) return null;

            var body = Instantiate(art.Soldier.Prefab, cam, false);
            body.name = "Arms";
            foreach (var a in body.GetComponentsInChildren<Animator>(true)) Destroy(a);

            var smr = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable)
            {
                Destroy(body);
                return null;
            }

            var arms = body.AddComponent<FirstPersonArms>();
            arms._loadout = loadout;
            arms._cam = cam;
            if (!arms.Setup(body.transform, smr))
            {
                Destroy(body);
                return null;
            }
            return arms;
        }

        bool Setup(Transform root, SkinnedMeshRenderer smr)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;

            _right = MakeArm(bones, "Right", 1f);
            _left = MakeArm(bones, "Left", -1f);
            if (_right == null || _left == null) return false;

            // Only the arms: every triangle skinned mostly to an arm or hand joint.
            smr.sharedMesh = ArmsOnly(smr);
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r != smr) r.enabled = false;
            _renderer = smr;
            PlayerRigBuilder.MarkViewmodel(root);

            // Stand the body so its shoulders are where a shooter's are, relative to the eye.
            root.localScale = Vector3.one * Scale;
            root.localRotation = Quaternion.identity;
            root.localPosition = Vector3.zero;
            var mid = (_right.Upper.position + _left.Upper.position) * 0.5f;
            root.localPosition = ShoulderMid - root.parent.InverseTransformPoint(mid);

            foreach (var t in root.GetComponentsInChildren<Transform>(true)) _rest.Add((t, t.localRotation));
            return true;
        }

        static Arm MakeArm(Dictionary<string, Transform> bones, string side, float sign)
        {
            if (!bones.TryGetValue(side + "Arm", out var upper) || !bones.TryGetValue(side + "ForeArm", out var lower)
                || !bones.TryGetValue(side + "Hand", out var hand))
                return null;

            var arm = new Arm { Upper = upper, Lower = lower, Hand = hand, Side = sign };
            arm.UpperLength = Vector3.Distance(upper.position, lower.position);
            arm.LowerLength = Vector3.Distance(lower.position, hand.position);

            string[] names = { "Thumb", "Index", "Middle" };
            arm.Fingers = new Transform[names.Length][];
            for (int f = 0; f < names.Length; f++)
            {
                arm.Fingers[f] = new Transform[3];
                for (int j = 0; j < 3; j++)
                    bones.TryGetValue($"{side}Hand{names[f]}{j + 1}", out arm.Fingers[f][j]);
            }

            // The hand's own frame, measured from the bind pose: which way its fingers point
            // and which way its palm faces (away from the thumb's side for the back of the hand).
            var middle = arm.Fingers[2][0] != null ? arm.Fingers[2][0].position : hand.position + hand.up * 0.08f;
            var thumb = arm.Fingers[0][0] != null ? arm.Fingers[0][0].position : hand.position + hand.right * 0.03f;
            var fingerDir = (middle - hand.position).normalized;
            var toThumb = Vector3.ProjectOnPlane(thumb - hand.position, fingerDir).normalized;
            var palm = Vector3.Cross(fingerDir, toThumb).normalized * sign;
            arm.FingerDirRest = Quaternion.Inverse(hand.rotation) * fingerDir;
            arm.PalmRest = Quaternion.Inverse(hand.rotation) * palm;
            // The middle of the palm: halfway to the knuckles, just proud of the palm's face.
            arm.PalmCentre = Quaternion.Inverse(hand.rotation) * ((middle - hand.position) * 0.5f + palm * 0.012f);
            return arm;
        }

        /// <summary>A copy of the mesh keeping only the triangles whose corners belong to an arm joint.</summary>
        static Mesh ArmsOnly(SkinnedMeshRenderer smr)
        {
            var src = smr.sharedMesh;
            var armIndex = new bool[smr.bones.Length];
            for (int i = 0; i < smr.bones.Length; i++)
            {
                var b = smr.bones[i];
                armIndex[i] = b != null && (ArmBones.Contains(b.name) || b.name.Contains("Hand"));
            }

            var weights = src.boneWeights;
            bool OnArm(int v)
            {
                var w = weights[v];
                int top = w.boneIndex0;
                float best = w.weight0;
                if (w.weight1 > best) { best = w.weight1; top = w.boneIndex1; }
                if (w.weight2 > best) { best = w.weight2; top = w.boneIndex2; }
                if (w.weight3 > best) { top = w.boneIndex3; }
                return top < armIndex.Length && armIndex[top];
            }

            var mesh = Instantiate(src);
            mesh.name = "Arms";
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var kept = new List<int>(tris.Length / 4);
                for (int i = 0; i < tris.Length; i += 3)
                    if (OnArm(tris[i]) && OnArm(tris[i + 1]) && OnArm(tris[i + 2]))
                    {
                        kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
                    }
                mesh.SetTriangles(kept, s);
            }
            return mesh;
        }

        void LateUpdate()
        {
            var weapon = _loadout != null ? _loadout.Current : null;
            var model = weapon != null && weapon.isActiveAndEnabled ? weapon.Model : null;
            bool holding = model != null && model.Grip != null && model.OffHand != null;
            _renderer.enabled = holding;
            if (!holding) return;

            foreach (var (bone, rest) in _rest) bone.localRotation = rest;

            // Hand frames on the gun: the right hand round the grip, palm against its right side,
            // fingers wrapping forward and down; the left under the fore-end, palm up. Each palm
            // is placed where it holds, and the wrist follows from the hand's frame.
            var gun = model.Root;
            var rightFingers = gun.TransformDirection(new Vector3(-0.25f, -0.75f, 0.6f)).normalized;
            var rightPalm = gun.TransformDirection(Vector3.left);
            Reach(_right, model.Grip.position - rightPalm * 0.03f + gun.TransformDirection(Vector3.down) * 0.03f,
                  rightFingers, rightPalm, _cam.TransformDirection(new Vector3(0.7f, -1f, -0.4f)));

            // Fingers run across the fore-end, so closing them wraps it; the thumb lies along it.
            var leftFingers = gun.TransformDirection(new Vector3(0.9f, 0f, 0.42f)).normalized;
            var leftPalm = gun.TransformDirection(Vector3.up);
            Reach(_left, model.OffHand.position - leftPalm * 0.01f, leftFingers, leftPalm,
                  _cam.TransformDirection(new Vector3(-0.8f, -1f, -0.2f)));
        }

        /// <summary>
        /// Puts the palm on <paramref name="palmTarget"/>, the hand turned to its frame on the gun:
        /// shoulder to elbow to wrist, the elbow bending toward <paramref name="pole"/>, then the
        /// fingers closed round the gun.
        /// </summary>
        void Reach(Arm arm, Vector3 palmTarget, Vector3 fingerDir, Vector3 palm, Vector3 pole)
        {
            // The hand's turn on the gun is known up front, so is where its wrist must be.
            var hand = Quaternion.LookRotation(fingerDir, palm)
                       * Quaternion.Inverse(Quaternion.LookRotation(arm.FingerDirRest, arm.PalmRest));
            Vector3 target = palmTarget - hand * arm.PalmCentre * Scale;

            float a = arm.UpperLength * Scale, b = arm.LowerLength * Scale;
            Vector3 shoulder = arm.Upper.position;
            Vector3 toTarget = target - shoulder;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 0.01f, a + b - 0.001f);
            Vector3 dir = toTarget.normalized;

            // Law of cosines: how far along the shoulder-wrist line the elbow sits, and how far out.
            float along = (a * a - b * b + d * d) / (2f * d);
            float up = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 bendDir = Vector3.ProjectOnPlane(pole, dir).normalized;
            Vector3 elbow = shoulder + dir * along + bendDir * up;
            Vector3 wrist = shoulder + dir * d;

            arm.Upper.rotation = Quaternion.FromToRotation(arm.Lower.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
            arm.Lower.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Lower.position, wrist - arm.Lower.position)
                                 * arm.Lower.rotation;

            arm.Hand.rotation = hand;

            // Close the fingers round the gun: bend each joint toward the palm.
            Vector3 flexAxis = Vector3.Cross(fingerDir, palm).normalized;
            float[][] curl =
            {
                new[] { 12f, 18f, 14f },   // thumb
                new[] { 42f, 55f, 35f },   // index
                new[] { 50f, 62f, 40f },   // middle (the ring and little fingers ride on it)
            };
            for (int f = 0; f < arm.Fingers.Length; f++)
                for (int j = 0; j < 3; j++)
                {
                    var joint = arm.Fingers[f][j];
                    if (joint == null) continue;
                    // A positive turn about fingers x palm swings the fingers toward the palm.
                    joint.rotation = Quaternion.AngleAxis(curl[f][j], flexAxis) * joint.rotation;
                }
        }
    }
}
