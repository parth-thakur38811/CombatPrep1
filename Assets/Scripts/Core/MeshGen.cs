using UnityEngine;
using UnityEngine.Rendering;

namespace CombatPrep.Core
{
    /// <summary>
    /// The few meshes no primitive can fake. Built once, in world space, as plain Mesh data.
    /// </summary>
    public static class MeshGen
    {
        static readonly System.Collections.Generic.Dictionary<(int, int, int, int), Mesh> BoxCache = new();

        /// <summary>
        /// A box of the given size whose UVs are measured in metres / <paramref name="tileMeters"/>,
        /// so a texture keeps the same real-world scale on a 20 cm crate slat and a 20 m wall.
        /// Unity's cube stretches one copy of the texture over every face, whatever its size -
        /// fine for flat colour, very wrong for a photographed surface. Cached by size, since
        /// most boxes in the level repeat.
        /// </summary>
        public static Mesh Box(Vector3 size, float tileMeters)
        {
            var key = (Mathf.RoundToInt(size.x * 1000f), Mathf.RoundToInt(size.y * 1000f),
                       Mathf.RoundToInt(size.z * 1000f), Mathf.RoundToInt(tileMeters * 1000f));
            if (BoxCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var h = size * 0.5f;
            float t = 1f / Mathf.Max(0.01f, tileMeters);
            var verts = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            var tris = new int[36];

            // Each face: normal, and the two in-plane axes the UVs run along.
            var faces = new (Vector3 n, Vector3 u, Vector3 v)[]
            {
                (Vector3.forward, Vector3.left, Vector3.up),
                (Vector3.back, Vector3.right, Vector3.up),
                (Vector3.right, Vector3.forward, Vector3.up),
                (Vector3.left, Vector3.back, Vector3.up),
                (Vector3.up, Vector3.right, Vector3.forward),
                (Vector3.down, Vector3.right, Vector3.back),
            };

            for (int f = 0; f < 6; f++)
            {
                var (n, u, v) = faces[f];
                float du = Mathf.Abs(Vector3.Dot(u, size)) * 0.5f;
                float dv = Mathf.Abs(Vector3.Dot(v, size)) * 0.5f;
                var centre = Vector3.Scale(n, h);
                int b = f * 4;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k == 1 || k == 2) ? 1f : -1f;
                    float sv = (k >= 2) ? 1f : -1f;
                    verts[b + k] = centre + u * (su * du) + v * (sv * dv);
                    normals[b + k] = n;
                    uvs[b + k] = new Vector2((su * du + du) * t, (sv * dv + dv) * t);
                }
                // Clockwise seen from outside: 0-2-1, 0-3-2 with this corner order.
                int i = f * 6;
                tris[i] = b; tris[i + 1] = b + 2; tris[i + 2] = b + 1;
                tris[i + 3] = b; tris[i + 4] = b + 3; tris[i + 5] = b + 2;
            }

            var mesh = new Mesh { name = "WorldBox" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            BoxCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// An earth mound: a smooth elliptical hump on a grid, with UVs from world position so
        /// neighbouring mounds share one continuous ground texture. Replaces scaled spheres,
        /// which smear any real texture into streaks toward their poles.
        /// </summary>
        public static Mesh Mound(Vector3 worldCentre, float width, float height, float depth,
                                 float tileMeters, int seed, int segments = 28)
        {
            int n = segments + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 50f, oz = (float)rng.NextDouble() * 50f;
            float t = 1f / Mathf.Max(0.01f, tileMeters);

            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float fx = x / (float)segments * 2f - 1f;
                float fz = z / (float)segments * 2f - 1f;
                float r = Mathf.Sqrt(fx * fx + fz * fz);
                // Rounded top, gentle skirt, and a slight sink below grade at the rim so the
                // edge never floats.
                float shape = r < 1f ? Mathf.Pow(Mathf.Cos(r * Mathf.PI * 0.5f), 1.4f) : 0f;
                float lumps = (Mathf.PerlinNoise(fx * 1.7f + ox, fz * 1.7f + oz) - 0.5f) * 0.35f;
                float y = height * shape * (1f + lumps) - (r > 0.92f ? 0.25f : 0f);

                var local = new Vector3(fx * width * 0.5f, y, fz * depth * 0.5f);
                verts[z * n + x] = local;
                uvs[z * n + x] = new Vector2((local.x + worldCentre.x) * t, (local.z + worldCentre.z) * t);
            }

            var tris = new int[segments * segments * 6];
            int k = 0;
            for (int z = 0; z < segments; z++)
            for (int x = 0; x < segments; x++)
            {
                int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }

            var mesh = new Mesh { name = "Mound" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Concertina wire: a thin tube wound into a helix lying along the ground from
        /// <paramref name="from"/> to <paramref name="to"/>. Each loop's radius wobbles a little
        /// (<paramref name="jitter"/>) and the coil sags under its own weight, because a
        /// perfect spring reads as a spring, not as wire someone dragged out in the mud.
        /// The tube is three-sided: at the thickness of wire nobody can count the facets.
        /// </summary>
        public static Mesh HelixTube(Vector3 from, Vector3 to, float coilRadius, float pitch,
                                     float wireRadius, int stepsPerTurn = 9, float jitter = 0.1f, int seed = 0)
        {
            var axis = to - from;
            float length = axis.magnitude;
            var forward = axis / length;
            var side = Vector3.Cross(Vector3.up, forward).normalized;
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            var up = Vector3.Cross(forward, side);

            int turns = Mathf.Max(1, Mathf.RoundToInt(length / pitch));
            int steps = turns * stepsPerTurn;
            var rng = new System.Random(seed);

            var path = new Vector3[steps + 1];
            float loopRadius = coilRadius;
            for (int i = 0; i <= steps; i++)
            {
                // A new radius per loop, eased in so adjacent loops don't kink.
                if (i % stepsPerTurn == 0)
                    loopRadius = coilRadius * (1f + ((float)rng.NextDouble() * 2f - 1f) * jitter);

                float t = i / (float)steps;
                float angle = t * turns * Mathf.PI * 2f;
                path[i] = from + axis * t
                        + side * (Mathf.Cos(angle) * loopRadius)
                        + up * (Mathf.Sin(angle) * loopRadius * 0.82f + coilRadius * 0.82f);
            }

            const int sides = 3;
            var verts = new Vector3[(steps + 1) * sides];
            var normals = new Vector3[verts.Length];
            var tris = new int[steps * sides * 6];

            for (int i = 0; i <= steps; i++)
            {
                var tangent = (path[Mathf.Min(i + 1, steps)] - path[Mathf.Max(i - 1, 0)]).normalized;
                var n1 = Vector3.Cross(tangent, up);
                if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.Cross(tangent, side);
                n1.Normalize();
                var n2 = Vector3.Cross(tangent, n1);

                for (int s = 0; s < sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    var dir = n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a);
                    verts[i * sides + s] = path[i] + dir * wireRadius;
                    normals[i * sides + s] = dir;
                }
            }

            // Wound so faces point outward under Unity's clockwise-front convention.
            int k = 0;
            for (int i = 0; i < steps; i++)
            for (int s = 0; s < sides; s++)
            {
                int i0 = i * sides + s, i1 = i * sides + (s + 1) % sides;
                int j0 = (i + 1) * sides + s, j1 = (i + 1) * sides + (s + 1) % sides;
                tris[k++] = i0; tris[k++] = i1; tris[k++] = j0;
                tris[k++] = i1; tris[k++] = j1; tris[k++] = j0;
            }

            var mesh = new Mesh { name = "Concertina" };
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
