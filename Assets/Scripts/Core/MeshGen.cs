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
        public static Mesh Box(Vector3 size, float tileMeters) => BevelBox(size, AutoBevel(size), tileMeters);

        static readonly System.Collections.Generic.Dictionary<(int, int, int), Mesh> SackCache = new();

        /// <summary>
        /// A filled sandbag: a stuffed pillow rather than a capsule - squarish along its length,
        /// rounded across it, flattened by the weight on top and pinched in at the tied ends.
        /// Laid out like Unity's capsule (length along Y, UVs wrapping round it) so it drops in
        /// where one was, with the same rotation and the same burlap tiling.
        /// </summary>
        /// <param name="halfThick">Half its height once lying down (local X).</param>
        /// <param name="halfDepth">Half its depth (local Z).</param>
        /// <param name="halfLength">Half its length (local Y).</param>
        public static Mesh Sack(float halfThick, float halfDepth, float halfLength)
        {
            var key = (Mathf.RoundToInt(halfThick * 1000f), Mathf.RoundToInt(halfDepth * 1000f),
                       Mathf.RoundToInt(halfLength * 1000f));
            if (SackCache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int rings = 14, segments = 22;
            // Superellipsoid exponents: squarish along the bag, rounder across it.
            const float alongPower = 0.35f, acrossPower = 0.8f;
            static float Pow(float w, float e) => Mathf.Sign(w) * Mathf.Pow(Mathf.Abs(w), e);

            var verts = new Vector3[(rings + 1) * (segments + 1)];
            var uvs = new Vector2[verts.Length];
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, r / (float)rings);
                float along = Pow(Mathf.Sin(phi), alongPower);
                float across = Pow(Mathf.Cos(phi), alongPower);
                // Gathered in at the tied ends.
                float pinch = 1f - 0.3f * Mathf.Pow(Mathf.Abs(Mathf.Sin(phi)), 8f);
                for (int s = 0; s <= segments; s++)
                {
                    float theta = Mathf.Lerp(-Mathf.PI, Mathf.PI, s / (float)segments);
                    float x = halfThick * across * Pow(Mathf.Cos(theta), acrossPower) * pinch;
                    float z = halfDepth * across * Pow(Mathf.Sin(theta), acrossPower) * pinch;
                    int i = r * (segments + 1) + s;
                    verts[i] = new Vector3(x, halfLength * along, z);
                    uvs[i] = new Vector2(s / (float)segments, r / (float)rings);
                }
            }

            var tris = new int[rings * segments * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }

            // Face outward: check one quad on the bag's side against the way out.
            int probe = (rings / 2) * (segments + 1) + segments / 4;
            var n = Vector3.Cross(verts[probe + segments + 1] - verts[probe], verts[probe + 1] - verts[probe]);
            if (Vector3.Dot(n, verts[probe]) < 0f)
                for (int i = 0; i < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);

            var mesh = new Mesh { name = "Sack" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();

            // Weld the shading where the grid wraps round and closes at the ends.
            var normals = mesh.normals;
            for (int r = 0; r <= rings; r++)
            {
                int first = r * (segments + 1), last = first + segments;
                var avg = (normals[first] + normals[last]).normalized;
                normals[first] = normals[last] = avg;
            }
            foreach (int r in new[] { 0, rings })
            {
                var sum = Vector3.zero;
                for (int s = 0; s <= segments; s++) sum += normals[r * (segments + 1) + s];
                for (int s = 0; s <= segments; s++) normals[r * (segments + 1) + s] = sum.normalized;
            }
            mesh.normals = normals;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            SackCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// How much to take off a box's edges: a few millimetres on a gun part, a few centimetres
        /// on a wall - enough to catch the light, never enough to look rounded off.
        /// </summary>
        public static float AutoBevel(Vector3 size)
            => Mathf.Min(0.035f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.08f);

        // Each face: normal, and the two in-plane axes its UVs run along.
        static readonly (Vector3 n, Vector3 u, Vector3 v)[] BoxFaces =
        {
            (Vector3.forward, Vector3.left, Vector3.up),
            (Vector3.back, Vector3.right, Vector3.up),
            (Vector3.right, Vector3.forward, Vector3.up),
            (Vector3.left, Vector3.back, Vector3.up),
            (Vector3.up, Vector3.right, Vector3.forward),
            (Vector3.down, Vector3.right, Vector3.back),
        };

        /// <summary>
        /// A box with every edge chamfered: a narrow 45-degree face along each edge and a small
        /// triangle at each corner. The chamfers carry the normals of the faces either side, so
        /// an edge shades like a rounded one and catches a line of light - most of what tells a
        /// cast or machined thing from a default cube.
        /// </summary>
        /// <param name="tileMeters">UVs in metres over this, as <see cref="Box"/>; 0 for 0-1 across each face.</param>
        public static Mesh BevelBox(Vector3 size, float bevel, float tileMeters)
        {
            var key = (Mathf.RoundToInt(size.x * 1000f), Mathf.RoundToInt(size.y * 1000f),
                       Mathf.RoundToInt(size.z * 1000f), Mathf.RoundToInt(tileMeters * 1000f) * 4096
                                                          + Mathf.RoundToInt(bevel * 10000f));
            if (BoxCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var h = size * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            float t = 1f / Mathf.Max(0.01f, tileMeters);

            var verts = new System.Collections.Generic.List<Vector3>(96);
            var normals = new System.Collections.Generic.List<Vector3>(96);
            var uvs = new System.Collections.Generic.List<Vector2>(96);
            var tris = new System.Collections.Generic.List<int>(132);

            int Add(Vector3 p, Vector3 n)
            {
                // UVs from the face this normal belongs to, so a chamfer carries on its texture.
                int f = 0;
                for (int i = 0; i < 6; i++) if (Vector3.Dot(BoxFaces[i].n, n) > 0.5f) { f = i; break; }
                var (_, u, v) = BoxFaces[f];
                float du = Mathf.Abs(Vector3.Dot(u, size)) * 0.5f, dv = Mathf.Abs(Vector3.Dot(v, size)) * 0.5f;
                float x = Vector3.Dot(p, u) + du, y = Vector3.Dot(p, v) + dv;
                uvs.Add(tileMeters > 0f ? new Vector2(x * t, y * t)
                                        : new Vector2(x / Mathf.Max(1e-5f, du * 2f), y / Mathf.Max(1e-5f, dv * 2f)));
                verts.Add(p);
                normals.Add(n);
                return verts.Count - 1;
            }

            // Wound so the face shows from outside: its cross product points the way it faces.
            void Tri(int i0, int i1, int i2, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]), outward) < 0f)
                    (i1, i2) = (i2, i1);
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
            }

            void Quad(int i0, int i1, int i2, int i3, Vector3 outward)
            {
                Tri(i0, i1, i2, outward);
                Tri(i0, i2, i3, outward);
            }

            Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

            // Faces, inset by the bevel.
            for (int i = 0; i < 3; i++)
            for (int s = -1; s <= 1; s += 2)
            {
                int j = (i + 1) % 3, k = (i + 2) % 3;
                Vector3 n = Axis(i) * s, c = n * h[i];
                Vector3 ej = Axis(j) * (h[j] - b), ek = Axis(k) * (h[k] - b);
                Quad(Add(c - ej - ek, n), Add(c + ej - ek, n), Add(c + ej + ek, n), Add(c - ej + ek, n), n);
            }

            if (b > 1e-5f)
            {
                // Edge chamfers: face i's inset edge to face j's, along the third axis.
                for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                for (int si = -1; si <= 1; si += 2)
                for (int sj = -1; sj <= 1; sj += 2)
                {
                    int k = 3 - i - j;
                    Vector3 ni = Axis(i) * si, nj = Axis(j) * sj, ek = Axis(k) * (h[k] - b);
                    Vector3 onI = ni * h[i] + nj * (h[j] - b), onJ = ni * (h[i] - b) + nj * h[j];
                    Quad(Add(onI - ek, ni), Add(onI + ek, ni), Add(onJ + ek, nj), Add(onJ - ek, nj), ni + nj);
                }

                // Corner triangles, one vertex on each of the three faces meeting there.
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var p = new Vector3(sx * (h.x - b), sy * (h.y - b), sz * (h.z - b));
                    Tri(Add(new Vector3(sx * h.x, p.y, p.z), Vector3.right * sx),
                        Add(new Vector3(p.x, sy * h.y, p.z), Vector3.up * sy),
                        Add(new Vector3(p.x, p.y, sz * h.z), Vector3.forward * sz),
                        new Vector3(sx, sy, sz));
                }
            }

            var mesh = new Mesh { name = b > 0f ? "BevelBox" : "WorldBox" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
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
