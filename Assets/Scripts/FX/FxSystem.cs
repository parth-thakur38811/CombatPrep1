using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CombatPrep.Core;

namespace CombatPrep.FX
{
    /// <summary>
    /// Tracers, muzzle flash, impact sparks and bullet holes - all from primitives and
    /// LineRenderers, no particle assets. Everything is pooled; nothing allocates after
    /// warm-up, which matters once you are dumping 600 RPM downrange.
    /// </summary>
    public class FxSystem : MonoBehaviour
    {
        public static FxSystem I { get; private set; }

        /// <summary>Spent debris lives here so bullets pass straight through it.</summary>
        public const int DebrisLayer = 9;

        [Header("Tracer")]
        public float TracerLife = 0.055f;
        public float TracerWidth = 0.022f;
        public Color TracerColor = new Color(1f, 0.82f, 0.35f);

        [Header("Impact")]
        public int MaxHoles = 120;

        Material _additive, _holeMat;
        Light _muzzleLight;
        Transform _muzzleFlash;
        float _flashUntil;

        readonly List<LineRenderer> _tracerPool = new();
        readonly List<float> _tracerExpiry = new();
        readonly Queue<Transform> _holes = new();
        readonly List<Transform> _debris = new();
        readonly List<Rigidbody> _debrisBodies = new();
        readonly List<float> _debrisExpiry = new();

        void Awake()
        {
            I = this;
            _additive = MakeAdditive(TracerColor);
            _holeMat = Mat.Get(new Color(0.03f, 0.03f, 0.04f), 0f, 0.05f);
        }

        /// <summary>URP/Unlit switched to additive blending in code - no shader asset needed.</summary>
        static Material MakeAdditive(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);                                  // transparent
            m.SetFloat("_Blend", 2f);                                    // additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public void AttachMuzzle(Transform muzzle)
        {
            var lightGo = new GameObject("MuzzleLight");
            lightGo.transform.SetParent(muzzle, false);
            _muzzleLight = lightGo.AddComponent<Light>();
            _muzzleLight.type = LightType.Point;
            _muzzleLight.color = new Color(1f, 0.78f, 0.45f);
            _muzzleLight.range = 7f;
            _muzzleLight.intensity = 0f;

            _muzzleFlash = Prim.Ball(muzzle, "MuzzleFlash", Vector3.zero, 0.085f, TracerColor, 0f, 1f);
            _muzzleFlash.GetComponent<MeshRenderer>().sharedMaterial = _additive;
            _muzzleFlash.gameObject.SetActive(false);
        }

        public void MuzzleFlash()
        {
            _flashUntil = Time.time + 0.035f;
            if (_muzzleLight != null) _muzzleLight.intensity = Random.Range(9f, 14f);
            if (_muzzleFlash != null)
            {
                _muzzleFlash.gameObject.SetActive(true);
                _muzzleFlash.localScale = Vector3.one * Random.Range(0.07f, 0.11f);
                _muzzleFlash.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            }
        }

        public void Tracer(Vector3 from, Vector3 to)
        {
            LineRenderer lr = null;
            for (int i = 0; i < _tracerPool.Count; i++)
                if (!_tracerPool[i].gameObject.activeSelf) { lr = _tracerPool[i]; break; }

            if (lr == null)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.material = _additive;
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                _tracerPool.Add(lr);
                _tracerExpiry.Add(0f);
            }

            int idx = _tracerPool.IndexOf(lr);
            lr.gameObject.SetActive(true);
            lr.startWidth = TracerWidth;
            lr.endWidth = TracerWidth * 0.35f;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            _tracerExpiry[idx] = Time.time + TracerLife;
        }

        /// <summary>
        /// Impact burst. Pass <paramref name="attachTo"/> for surfaces that move (a swinging
        /// target board) so the hole travels with them instead of hanging in world space.
        /// </summary>
        public void Impact(Vector3 point, Vector3 normal, Color surfaceTint,
                           Transform attachTo = null, int debrisCount = -1, bool spark = true)
        {
            if (spark)
            {
                var flash = Prim.Ball(transform, "ImpactFlash", point, 0.12f, TracerColor, 0f, 1f);
                flash.GetComponent<MeshRenderer>().sharedMaterial = _additive;
                Destroy(flash.gameObject, 0.05f);
            }

            // Debris: a few tiny cubes thrown off along the normal.
            int count = debrisCount >= 0 ? debrisCount : Random.Range(3, 6);
            for (int i = 0; i < count; i++)
            {
                var d = Prim.Box(transform, "Debris", point, Vector3.one * Random.Range(0.012f, 0.028f),
                                 surfaceTint, 0.2f, 0.3f, true);
                d.gameObject.layer = DebrisLayer;   // excluded from the bullet mask
                var rb = d.gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.02f;
                Vector3 dir = (normal + Random.insideUnitSphere * 0.75f).normalized;
                rb.AddForce(dir * Random.Range(1.6f, 3.4f), ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 0.05f, ForceMode.Impulse);
                Destroy(d.gameObject, 2.2f);
            }

            BulletHole(point, normal, attachTo);
        }

        void BulletHole(Vector3 point, Vector3 normal, Transform attachTo)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Hole";
            q.transform.SetParent(attachTo != null ? attachTo : transform, true);
            // Lift off the surface to avoid z-fighting, and face outward.
            q.transform.position = point + normal * 0.004f;
            q.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            q.transform.localScale = Vector3.one * Random.Range(0.022f, 0.034f);
            q.GetComponent<MeshRenderer>().sharedMaterial = _holeMat;

            // Holes parented to a target die with that target's board on respawn.
            if (attachTo != null) return;

            _holes.Enqueue(q.transform);
            while (_holes.Count > MaxHoles)
            {
                var old = _holes.Dequeue();
                if (old != null) Destroy(old.gameObject);
            }
        }

        // ------------------------------------------------------------------- explosion

        Material _fireMat;
        Material FireMat => _fireMat ??= MakeAdditive(new Color(1f, 0.55f, 0.18f));

        /// <summary>
        /// Grenade blast: an expanding fireball, a bright light flash, flung debris and a
        /// scorch mark. All primitives and one point light, animated by a coroutine and
        /// torn down after - no particle system, in keeping with the rest of the project.
        /// </summary>
        public void Explosion(Vector3 centre, float radius)
        {
            // Fireball.
            var ball = Prim.Ball(transform, "Blast", centre, 0.6f, new Color(1f, 0.6f, 0.2f), 0f, 1f);
            ball.GetComponent<MeshRenderer>().sharedMaterial = FireMat;

            // Light flash.
            var lightGo = new GameObject("BlastLight");
            lightGo.transform.position = centre;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.42f);
            light.range = radius * 3.2f;
            light.intensity = 22f;

            // Ground scorch.
            var scorch = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(scorch.GetComponent<Collider>());
            scorch.name = "Scorch";
            scorch.transform.SetParent(transform, true);
            scorch.transform.position = centre + Vector3.up * 0.03f;
            scorch.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            scorch.transform.localScale = Vector3.one * radius * 1.1f;
            scorch.GetComponent<MeshRenderer>().sharedMaterial =
                Mat.Get(new Color(0.04f, 0.03f, 0.03f), 0f, 0.1f);
            Destroy(scorch, 9f);

            // Debris chunks flung out.
            for (int i = 0; i < 14; i++)
            {
                var d = Prim.Box(transform, "Frag", centre, Vector3.one * Random.Range(0.05f, 0.13f),
                                 new Color(0.18f, 0.16f, 0.14f), 0.3f, 0.3f, true);
                d.gameObject.layer = DebrisLayer;
                var rb = d.gameObject.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                Vector3 dir = (Vector3.up * 0.6f + Random.insideUnitSphere).normalized;
                rb.AddForce(dir * Random.Range(6f, 13f), ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 0.4f, ForceMode.Impulse);
                Destroy(d.gameObject, Random.Range(2.5f, 4f));
            }

            StartCoroutine(AnimateBlast(ball, light, radius));
        }

        System.Collections.IEnumerator AnimateBlast(Transform ball, Light light, float radius)
        {
            float t = 0f;
            const float dur = 0.5f;
            var mr = ball.GetComponent<MeshRenderer>();
            var mat = new Material(mr.sharedMaterial);   // per-blast instance so alpha fades independently
            mr.sharedMaterial = mat;

            while (t < dur)
            {
                t += Time.deltaTime;
                float u = t / dur;

                // Fireball punches out fast then holds; colour cools white -> orange -> dark.
                float scale = Mathf.SmoothStep(0.6f, radius * 1.15f, Mathf.Sqrt(u));
                ball.localScale = Vector3.one * scale;

                Color c = Color.Lerp(new Color(1f, 0.95f, 0.7f), new Color(0.7f, 0.22f, 0.06f), u);
                c.a = 1f - u;
                mat.SetColor("_BaseColor", c);

                if (light != null)
                    light.intensity = Mathf.Lerp(22f, 0f, u * u);

                yield return null;
            }

            if (light != null) Destroy(light.gameObject);
            Destroy(ball.gameObject);
            Destroy(mat);
        }

        void Update()
        {
            float now = Time.time;

            for (int i = 0; i < _tracerPool.Count; i++)
                if (_tracerPool[i].gameObject.activeSelf && now > _tracerExpiry[i])
                    _tracerPool[i].gameObject.SetActive(false);

            if (_muzzleLight != null && now > _flashUntil)
            {
                _muzzleLight.intensity = Mathf.Lerp(_muzzleLight.intensity, 0f, 1f - Mathf.Exp(-30f * Time.deltaTime));
                if (_muzzleFlash != null && _muzzleFlash.gameObject.activeSelf)
                    _muzzleFlash.gameObject.SetActive(false);
            }
        }
    }
}
