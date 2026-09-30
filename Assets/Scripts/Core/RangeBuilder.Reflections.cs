using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CombatPrep.FX;

namespace CombatPrep.Core
{
    /// <summary>
    /// What the wet ground, the puddles and the steel actually mirror. Left to Unity, every
    /// reflection was a clear blue sky (see EnvironmentCapture), so puddles looked like ice and
    /// wet earth like frost. Instead the scene itself is captured: once from the middle of the
    /// arena, which becomes the reflection everywhere, and into box-projected probes - the whole
    /// arena, so a reflection sits where the thing it shows is, and each ruin and the canopy,
    /// so a covered floor doesn't mirror a sky it can't see.
    ///
    /// Captured once the arena is up and burning, and again whenever the online cover comes or
    /// goes - not every frame, because nothing they show moves.
    /// </summary>
    public static partial class RangeBuilder
    {
        static readonly List<ReflectionProbe> Probes = new();
        static readonly Dictionary<ReflectionProbe, RenderTexture> Captures = new();

        /// <summary>
        /// What a probe sees: the world and its fires, never players, spent debris, the invisible
        /// walls or the rain - which follows the camera, and would hang in every reflection.
        /// </summary>
        public static int ReflectionMask => ~((1 << PlayerRigBuilder.PlayerLayer) | (1 << PlayerRigBuilder.RemotePlayerLayer)
                                            | (1 << FxSystem.DebrisLayer) | (1 << BoundaryLayer)
                                            | (1 << Weather.CameraOnlyLayer) | (1 << 5));   // UI

        static void Reflections(Transform root)
        {
            Probes.Clear();
            Captures.Clear();
            var t = Prim.Empty(root, "Reflections");

            Probe(t, "Arena", new Vector3(0f, 6f, ArenaCentre.z), new Vector3(110f, 40f, 150f), 256, 0, 4f);
            Probe(t, "Canopy", new Vector3(0f, 1.8f, -1.5f), new Vector3(26f, 3.6f, 8f), 128, 1, 0.6f);
            foreach (var site in ArenaRuinSites)
                foreach (int side in new[] { -1, 1 })
                    Probe(t, "Ruin", new Vector3(site.centre.x * side, 1.6f, site.centre.y),
                          new Vector3(site.size.x - RuinWall * 2f, 3.2f, site.size.y - RuinWall * 2f), 128, 1, 0.5f);
        }

        static void Probe(Transform parent, string name, Vector3 centre, Vector3 size, int resolution,
                          int importance, float blend)
        {
            var go = new GameObject(name + "Probe");
            go.transform.SetParent(parent, false);
            go.transform.position = centre;

            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Custom;      // filled by RenderReflections
            p.size = size;
            p.boxProjection = true;
            p.resolution = resolution;
            p.hdr = true;
            p.importance = importance;
            p.blendDistance = blend;
            p.clearFlags = ReflectionProbeClearFlags.Skybox;
            p.nearClipPlane = 0.1f;
            p.farClipPlane = 400f;
            p.shadowDistance = 80f;
            p.cullingMask = ReflectionMask;
            Probes.Add(p);
        }

        /// <summary>Captures every probe afresh; the first, the whole arena, is also the reflection everywhere else.</summary>
        public static void RenderReflections()
        {
            foreach (var p in Probes)
            {
                if (p == null) continue;
                Captures.TryGetValue(p, out var rt);
                rt = EnvironmentCapture.Capture(p.transform.position, p.resolution, ReflectionMask, rt);
                Captures[p] = rt;
                p.customBakedTexture = rt;
            }

            if (Probes.Count > 0 && Captures.TryGetValue(Probes[0], out var arena))
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = arena;
            }
        }
    }
}
