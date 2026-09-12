using UnityEngine;

namespace CombatPrep.Core
{
    /// <summary>
    /// Thin wrapper over Unity's built-in primitives. Every piece of geometry in the
    /// game - guns, targets, the range itself - is assembled from these, which is what
    /// lets the project ship with zero imported meshes.
    /// </summary>
    public static class Prim
    {
        public static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size,
                                    Color color, float metallic = 0f, float smoothness = 0.3f,
                                    bool collider = false, Vector3 euler = default)
            => Make(PrimitiveType.Cube, parent, name, pos, size, euler, color, metallic, smoothness, collider);

        /// <summary>Cylinder along local Z (Unity's cylinder is Y-up and 2 units tall, so we correct both).</summary>
        public static Transform Tube(Transform parent, string name, Vector3 pos, float diameter, float length,
                                     Color color, float metallic = 0.3f, float smoothness = 0.5f,
                                     bool collider = false, Vector3 euler = default)
            => Make(PrimitiveType.Cylinder, parent, name, pos,
                    new Vector3(diameter, length * 0.5f, diameter),
                    euler + new Vector3(90f, 0f, 0f), color, metallic, smoothness, collider);

        /// <summary>Cylinder standing on its base along local Y - barrels, posts, drums.</summary>
        public static Transform Pillar(Transform parent, string name, Vector3 pos, float diameter, float height,
                                       Color color, float metallic = 0f, float smoothness = 0.3f,
                                       bool collider = false, Vector3 euler = default)
            => Make(PrimitiveType.Cylinder, parent, name, pos,
                    new Vector3(diameter, height * 0.5f, diameter), euler, color, metallic, smoothness, collider);

        public static Transform Ball(Transform parent, string name, Vector3 pos, float diameter,
                                     Color color, float metallic = 0f, float smoothness = 0.3f, bool collider = false)
            => Make(PrimitiveType.Sphere, parent, name, pos, Vector3.one * diameter, default, color, metallic, smoothness, collider);

        public static Transform Capsule(Transform parent, string name, Vector3 pos, float diameter, float height,
                                        Color color, bool collider = false, Vector3 euler = default)
            => Make(PrimitiveType.Capsule, parent, name, pos,
                    new Vector3(diameter, height * 0.5f, diameter), euler, color, 0f, 0.3f, collider);

        /// <summary>Flat quad facing local -Z, for anything that just displays a texture.</summary>
        public static Transform Quad(Transform parent, string name, Vector3 pos, Vector2 size,
                                     Material material, Vector3 euler = default, bool collider = false)
        {
            var t = Make(PrimitiveType.Quad, parent, name, pos, new Vector3(size.x, size.y, 1f),
                         euler, Color.white, 0f, 0.3f, collider);
            t.GetComponent<MeshRenderer>().sharedMaterial = material;
            return t;
        }

        /// <summary>
        /// A ring built from segment boxes around the local Z axis. Unity has no torus
        /// primitive and we cannot import one, so this is how the optic housing gets a
        /// clear bore instead of being a solid block in front of the camera.
        /// </summary>
        public static Transform Ring(Transform parent, string name, Vector3 pos, float outerDiameter,
                                     float thickness, float depth, Color color,
                                     int segments = 16, float metallic = 0.7f, float smoothness = 0.45f)
        {
            var root = Empty(parent, name, pos);
            float radius = (outerDiameter - thickness) * 0.5f;
            // Chord length of one segment, widened slightly so neighbours overlap and the
            // ring reads as solid rather than dashed.
            float segWidth = 2f * Mathf.PI * radius / segments * 1.25f;

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var seg = Box(root, $"Seg{i}",
                              new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f),
                              new Vector3(segWidth, thickness, depth),
                              color, metallic, smoothness);
                seg.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
            }
            return root;
        }

        public static void SetMaterial(Transform t, Material m)
        {
            var r = t.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = m;
        }

        static Transform Make(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale,
                              Vector3 euler, Color color, float metallic, float smoothness, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (!collider && col != null) Object.Destroy(col);

            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localEulerAngles = euler;
            t.localScale = scale;

            go.GetComponent<MeshRenderer>().sharedMaterial = Mat.Get(color, metallic, smoothness);
            return t;
        }

        public static Transform Empty(Transform parent, string name, Vector3 pos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go.transform;
        }
    }
}
