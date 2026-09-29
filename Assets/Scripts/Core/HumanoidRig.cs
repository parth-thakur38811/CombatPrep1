using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CombatPrep.Core
{
    /// <summary>
    /// Builds a Humanoid avatar for a Mixamo-rigged model from its joint names, so any Mixamo
    /// animation retargets onto it.
    ///
    /// It is built while the game runs rather than kept as an asset. An avatar saved from an
    /// editor script can quietly drop out of a player build - it did, and every soldier in the
    /// build fell back to the primitive one - whereas AvatarBuilder behaves the same in the
    /// editor and in a build. It takes a few milliseconds, once.
    /// </summary>
    public static class HumanoidRig
    {
        /// <summary>Unity human bone, Mixamo joint.</summary>
        static readonly (string human, string joint)[] MixamoMap =
        {
            ("Hips", "Hips"), ("Spine", "Spine"), ("Chest", "Spine1"), ("UpperChest", "Spine2"),
            ("Neck", "Neck"), ("Head", "Head"),
            ("LeftShoulder", "LeftShoulder"), ("LeftUpperArm", "LeftArm"),
            ("LeftLowerArm", "LeftForeArm"), ("LeftHand", "LeftHand"),
            ("RightShoulder", "RightShoulder"), ("RightUpperArm", "RightArm"),
            ("RightLowerArm", "RightForeArm"), ("RightHand", "RightHand"),
            ("LeftUpperLeg", "LeftUpLeg"), ("LeftLowerLeg", "LeftLeg"),
            ("LeftFoot", "LeftFoot"), ("LeftToes", "LeftToeBase"),
            ("RightUpperLeg", "RightUpLeg"), ("RightLowerLeg", "RightLeg"),
            ("RightFoot", "RightFoot"), ("RightToes", "RightToeBase"),
            ("Left Thumb Proximal", "LeftHandThumb1"), ("Left Thumb Intermediate", "LeftHandThumb2"),
            ("Left Thumb Distal", "LeftHandThumb3"),
            ("Left Index Proximal", "LeftHandIndex1"), ("Left Index Intermediate", "LeftHandIndex2"),
            ("Left Index Distal", "LeftHandIndex3"),
            ("Left Middle Proximal", "LeftHandMiddle1"), ("Left Middle Intermediate", "LeftHandMiddle2"),
            ("Left Middle Distal", "LeftHandMiddle3"),
            ("Right Thumb Proximal", "RightHandThumb1"), ("Right Thumb Intermediate", "RightHandThumb2"),
            ("Right Thumb Distal", "RightHandThumb3"),
            ("Right Index Proximal", "RightHandIndex1"), ("Right Index Intermediate", "RightHandIndex2"),
            ("Right Index Distal", "RightHandIndex3"),
            ("Right Middle Proximal", "RightHandMiddle1"), ("Right Middle Intermediate", "RightHandMiddle2"),
            ("Right Middle Distal", "RightHandMiddle3"),
        };

        /// <summary>
        /// The avatar for a model in its bind pose, with no parent and at the origin - the
        /// hierarchy AvatarBuilder expects. The reference pose is that bind pose with the arms
        /// straightened out level: Unity measures every animation against a T-pose, and this
        /// soldier's arms droop about six degrees. Returns null if the joints don't make a human.
        /// </summary>
        public static Avatar Build(GameObject root)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in all) byName[t.name] = t;

            var known = new HashSet<string>(HumanTrait.BoneName);
            var human = new List<HumanBone>();
            foreach (var (humanName, joint) in MixamoMap)
            {
                if (!known.Contains(humanName) || !byName.ContainsKey(joint)) continue;
                human.Add(new HumanBone
                {
                    humanName = humanName,
                    boneName = joint,
                    limit = new HumanLimit { useDefaultValues = true }
                });
            }

            var saved = all.Select(t => t.localRotation).ToArray();
            var side = root.transform.right;
            Straighten(byName, "LeftArm", "LeftForeArm", -side);
            Straighten(byName, "LeftForeArm", "LeftHand", -side);
            Straighten(byName, "RightArm", "RightForeArm", side);
            Straighten(byName, "RightForeArm", "RightHand", side);

            var skeleton = all.Select(t => new SkeletonBone
            {
                name = t.name,
                position = t.localPosition,
                rotation = t.localRotation,
                scale = t.localScale
            }).ToArray();

            for (int i = 0; i < all.Length; i++) all[i].localRotation = saved[i];

            var avatar = UnityEngine.AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            });

            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                if (avatar != null)
                {
                    if (Application.isPlaying) Object.Destroy(avatar);
                    else Object.DestroyImmediate(avatar);
                }
                return null;
            }
            avatar.name = root.name + "_Avatar";
            return avatar;
        }

        static void Straighten(Dictionary<string, Transform> bones, string bone, string child, Vector3 direction)
        {
            if (!bones.TryGetValue(bone, out var b) || !bones.TryGetValue(child, out var c)) return;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, direction) * b.rotation;
        }
    }
}
