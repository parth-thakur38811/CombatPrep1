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
        /// <summary>
        /// A box of this size with its edges bevelled (MeshGen.BevelBox) rather than Unity's
        /// razor-edged cube, so it catches the light along its edges. The transform stays at unit
        /// scale; the mesh is the size.
        /// </summary>
        public static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size,
                                    Color color, float metallic = 0f, float smoothness = 0.3f,
                                    bool collider = false, Vector3 euler = default)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localEulerAngles = euler;
            go.AddComponent<MeshFilter>().sharedMesh = MeshGen.BevelBox(size, MeshGen.AutoBevel(size), 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat.Get(color, metallic, smoothness);
            if (collider) go.AddComponent<BoxCollider>();   // sizes itself to the mesh
            return t;
        }

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

        /// <summary>
        /// A sandbag (MeshGen.Sack): sized, laid and UV'd like the capsule it replaced, so the
        /// same rotation lays it along a wall and the same burlap tiling fits it.
        /// </summary>
        public static Transform Sack(Transform parent, string name, Vector3 pos, float diameter, float length,
                                     Color color, Vector3 euler = default)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localEulerAngles = euler;
            go.AddComponent<MeshFilter>().sharedMesh = MeshGen.Sack(diameter * 0.4f, diameter * 0.5f, length * 0.5f);
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat.Get(color, 0f, 0.3f);
            return t;
        }

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

        // ------------------------------------------------------------ imported art

        /// <summary>
        /// A box wearing an imported PBR surface at true scale (see MeshGen.Box). Built at
        /// unit scale around its own mesh, so its collider is the mesh bounds. With no art
        /// library it falls back to a plain Box in <paramref name="fallback"/>, so every caller
        /// works on a bare checkout.
        /// </summary>
        public static Transform Surface(Transform parent, string name, Vector3 pos, Vector3 size,
                                        ArtLibrary.Surface surface, Material fallback,
                                        bool collider = false, Vector3 euler = default)
        {
            if (!ArtLibrary.Has(surface))
            {
                var b = Box(parent, name, pos, size, Color.white, 0f, 0.3f, collider, euler);
                SetMaterial(b, fallback);
                return b;
            }

            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localEulerAngles = euler;
            go.AddComponent<MeshFilter>().sharedMesh = MeshGen.Box(size, surface.TileMeters);
            go.AddComponent<MeshRenderer>().sharedMaterial = surface.Material;
            if (collider) go.AddComponent<BoxCollider>();   // sizes itself to the mesh
            return t;
        }

        /// <summary>Surface with a flat-colour fallback, for callers that never had a texture.</summary>
        public static Transform Surface(Transform parent, string name, Vector3 pos, Vector3 size,
                                        ArtLibrary.Surface surface, Color fallback, float metallic, float smoothness,
                                        bool collider = false, Vector3 euler = default)
            => Surface(parent, name, pos, size, surface, Mat.Get(fallback, metallic, smoothness), collider, euler);

        public enum Pivot { Base, Centre }

        /// <summary>
        /// An imported prop, scaled to its real-world height, wearing its material, with one
        /// box collider around the whole thing so bullets and players both stop at it.
        ///
        /// Models come with whatever origin their author left, so each one is wrapped in a
        /// pivot object: its origin is the model's base centre (sits on the ground wherever it
        /// is placed) or its middle (for things that get turned over, like a tyre laid flat).
        /// </summary>
        public static Transform Prop(Transform parent, string name, ArtLibrary.Prop prop, Vector3 pos,
                                     Quaternion rotation, bool collider = true, float scale = 1f,
                                     Pivot pivot = Pivot.Base)
        {
            // Measured at the world origin, unrotated, before it joins the hierarchy - a world
            // AABB taken under a rotated parent would come out too big.
            // The model keeps its own root rotation and scale - that is how its importer left it
            // standing upright - and only moves to the origin.
            var model = Object.Instantiate(prop.Model);
            model.name = "Model";
            var m = model.transform;
            m.position = Vector3.zero;

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) r.sharedMaterial = prop.Material;

            // Guard against an importer unit mismatch (cm vs m): trust the published size.
            var bounds = WorldBounds(renderers);
            if (prop.Size.y > 0f && bounds.size.y > 0.001f)
            {
                float fix = prop.Size.y / bounds.size.y;
                if (fix < 0.5f || fix > 2f)
                {
                    m.localScale *= fix;
                    bounds = WorldBounds(renderers);
                }
            }

            var root = new GameObject(name).transform;
            var anchor = pivot == Pivot.Base
                ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)
                : bounds.center;
            m.SetParent(root, true);          // root sits at the origin, so world == local here
            m.localPosition = -anchor;

            if (collider)
            {
                var box = root.gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center - anchor;
                box.size = bounds.size;
            }

            root.localScale = Vector3.one * scale;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = rotation;
            return root;
        }

        /// <summary>World-space renderer bounds of a model sitting unrotated at the origin.</summary>
        static Bounds WorldBounds(Renderer[] renderers)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any ? b : new Bounds(Vector3.zero, Vector3.one);
        }
    }
}
