using UnityEngine;
using UnityEngine.Rendering;
using CombatPrep.Core;

namespace CombatPrep.FX
{
    /// <summary>
    /// How each effect is first made. Editor/FxPrefabBuilder runs these once to create the
    /// effect prefabs in Prefabs/FX - after that the prefabs are what the game uses, so they
    /// can be opened and tuned in the editor like any hand-made effect. The game only falls
    /// back to building an effect from here if its prefab is missing.
    ///
    /// Every effect is built at unit scale, facing +Z (a muzzle's barrel, an impact's surface
    /// normal), and returned as a new root object.
    /// </summary>
    public static class FxRecipes
    {
        // HDR tints, so the hot parts bloom.
        static readonly Color Muzzle = new(3.0f, 1.9f, 0.9f, 1f);
        static readonly Color MuzzleFlame = new(2.6f, 1.45f, 0.55f, 1f);
        static readonly Color Spark = new(3.2f, 2.0f, 0.8f, 1f);
        static readonly Color TracerHot = new(3.0f, 2.1f, 1.0f, 1f);
        static readonly Color Fireball = new(2.2f, 1.5f, 0.9f, 1f);

        // ------------------------------------------------------------------ muzzle flash

        /// <summary>
        /// A muzzle flash: a star of fire seen head-on, tongues of flame thrown forward, a few
        /// sparks, a puff of smoke and a flash of light. The flash itself rides the barrel;
        /// sparks and smoke are left behind in the world.
        /// </summary>
        public static GameObject MuzzleFlash()
        {
            var root = new GameObject("MuzzleFlash");
            var fx = root.AddComponent<FlashFx>();
            fx.LightIntensity = 10f;
            fx.LightFade = 0.06f;

            var core = Child(root.transform, "Core", 21u, Mat.Particle(Tex.MuzzleStar(), true, false, Muzzle), local: true);
            OneShot(core, 1, 1);
            var main = core.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.035f, 0.055f);
            main.startSpeed = 0f;
            // Kept small: through a scope the flash is magnified, and it shouldn't white out the sight.
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            NoShape(core);
            Grow(core, 0.8f, 1.25f);
            FadeOut(core);

            var flame = Child(root.transform, "Flame", 22u, Mat.Particle(Tex.Flame(), true, false, MuzzleFlame), local: true);
            OneShot(flame, 3, 5);
            main = flame.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.028f, 0.045f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
            Cone(flame, 9f, 0.004f);
            Stretch(flame, 0.018f, 1.4f);
            FadeOut(flame);

            var sparks = Child(root.transform, "Sparks", 23u, Mat.Particle(Tex.SoftDot(32, 3f), true, false, Spark));
            OneShot(sparks, 2, 6);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.012f);
            main.gravityModifier = 0.5f;
            Cone(sparks, 22f, 0.004f);
            Stretch(sparks, 0.03f, 1.2f);
            FadeOut(sparks);

            var smoke = Child(root.transform, "Smoke", 24u, ParticleKit.Smoke);
            OneShot(smoke, 1, 2);
            main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.62f, 0.62f, 0.64f, 1f);
            main.gravityModifier = -0.03f;
            Cone(smoke, 18f, 0.01f);
            Grow(smoke, 0.6f, 3f);
            Puff(smoke, 0.22f);
            Drag(smoke, 1.6f);

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.74f, 0.45f);
            light.range = 7f;
            light.shadows = LightShadows.None;
            light.intensity = 0f;
            return root;
        }

        // ------------------------------------------------------------------------ tracer

        /// <summary>A glowing streak: white-hot at the head, fading to nothing at the tail.</summary>
        public static GameObject Tracer()
        {
            var root = new GameObject("Tracer");
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = Mat.Particle(Tex.TracerGlow(), true, false, TracerHot);
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.010f), new Keyframe(1f, 0.028f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.6f, 0.3f), 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(1f, 1f) });
            line.colorGradient = g;
            var tracer = root.AddComponent<TracerFx>();
            tracer.Speed = 520f;
            tracer.Length = 7f;
            return root;
        }

        // ------------------------------------------------------------------------ impact

        /// <summary>
        /// A bullet hitting something: a puff of dust and a spray of chips in the colour of what
        /// was hit, and sparks off anything hard.
        /// </summary>
        public static GameObject Impact()
        {
            var root = new GameObject("Impact");
            var fx = root.AddComponent<FlashFx>();
            fx.LightIntensity = 0f;

            var dust = Child(root.transform, "Dust", 31u, ParticleKit.Smoke);
            OneShot(dust, 3, 5);
            var main = dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = -0.01f;
            Cone(dust, 32f, 0.02f);
            Grow(dust, 0.5f, 2.4f);
            Puff(dust, 0.6f);
            Drag(dust, 1.4f);

            var chips = Child(root.transform, "Chips", 32u, Mat.Particle(Tex.SoftDot(16, 12f), false, false));
            OneShot(chips, 3, 6);
            main = chips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.010f, 0.022f);
            main.startColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            main.gravityModifier = 1.4f;
            Cone(chips, 50f, 0.01f);
            FadeOut(chips, 0.7f);

            var sparks = Child(root.transform, "Sparks", 33u, Mat.Particle(Tex.SoftDot(32, 3f), true, false, Spark));
            OneShot(sparks, 3, 8);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.07f, 0.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.005f, 0.011f);
            main.gravityModifier = 0.9f;
            Cone(sparks, 45f, 0.005f);
            Stretch(sparks, 0.03f, 1.1f);
            FadeOut(sparks);

            fx.Tinted = new[] { dust, chips };
            fx.Sparks = new[] { sparks };
            return root;
        }

        /// <summary>A bullet hole decal: lit, so it darkens and glints with the surface it's on.</summary>
        public static GameObject BulletHole()
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "BulletHole";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mat.Decal(Tex.BulletHole(), Color.white, 0.12f);
            r.shadowCastingMode = ShadowCastingMode.Off;
            return q;
        }

        // --------------------------------------------------------------------- explosion

        /// <summary>
        /// A grenade blast, built for a 4 m radius (the caller scales it): a fireball, a thick
        /// column of smoke, sparks and debris thrown out, a flash of light and a scorch mark.
        /// </summary>
        public static GameObject Explosion()
        {
            var root = new GameObject("Explosion");
            var fx = root.AddComponent<FlashFx>();
            fx.LightIntensity = 24f;
            fx.LightFade = 0.55f;
            fx.DestroyAfter = 10f;

            var fire = Child(root.transform, "Fireball", 41u, Mat.Particle(Tex.Flame(), true, true, Fireball));
            OneShot(fire, 14, 18);
            var main = fire.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Sphere(fire, 0.35f, hemisphere: false);
            Grow(fire, 0.5f, 1.7f);
            var col = fire.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(
                new[] { (0f, new Color(1f, 0.95f, 0.8f)), (0.4f, new Color(1f, 0.55f, 0.18f)), (1f, new Color(0.45f, 0.12f, 0.05f)) },
                new[] { (0f, 1f), (0.6f, 0.7f), (1f, 0f) });

            var smoke = Child(root.transform, "Smoke", 42u, ParticleKit.Smoke);
            OneShot(smoke, 16, 22);
            main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.18f, 0.17f, 0.16f, 1f);
            main.gravityModifier = -0.04f;
            Sphere(smoke, 0.5f, hemisphere: true);
            Grow(smoke, 0.7f, 2.6f);
            Puff(smoke, 0.8f);
            Drag(smoke, 0.9f);
            Wind(smoke, 0.8f);

            var sparks = Child(root.transform, "Sparks", 43u, Mat.Particle(Tex.SoftDot(32, 3f), true, false, Spark));
            OneShot(sparks, 30, 45);
            main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 20f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.gravityModifier = 1.1f;
            Sphere(sparks, 0.2f, hemisphere: true);
            Stretch(sparks, 0.02f, 1.2f);
            FadeOut(sparks);

            var debris = Child(root.transform, "Debris", 44u, Mat.Particle(Tex.SoftDot(16, 12f), false, false));
            OneShot(debris, 12, 18);
            main = debris.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.12f, 0.11f, 0.10f, 1f);
            main.gravityModifier = 1.5f;
            Sphere(debris, 0.2f, hemisphere: true);
            var spin = debris.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            FadeOut(debris, 0.8f);

            var scorch = GameObject.CreatePrimitive(PrimitiveType.Quad);
            scorch.name = "Scorch";
            Object.DestroyImmediate(scorch.GetComponent<Collider>());
            scorch.transform.SetParent(root.transform, false);
            scorch.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            scorch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            scorch.transform.localScale = Vector3.one * 4.4f;
            var sr = scorch.GetComponent<MeshRenderer>();
            sr.sharedMaterial = Mat.Decal(Tex.Scorch(256, 4), Color.white, 0.3f);
            sr.shadowCastingMode = ShadowCastingMode.Off;

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 1f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.42f);
            light.range = 14f;
            light.shadows = LightShadows.None;
            light.intensity = 0f;
            return root;
        }

        // -------------------------------------------------------------------------- rain

        /// <summary>
        /// Rain: streaks falling from a box above the camera, slanting with the wind, dying on
        /// whatever they hit - and sometimes throwing a splash or leaving a ripple there.
        /// </summary>
        public static GameObject Rain(float dropsPerSecond = 3800f, float area = 46f)
        {
            var rain = ParticleKit.New(null, "Rain", Vector3.zero, 11u,
                                       Mat.Particle(Tex.RainStreak(), additive: false, soft: false));
            var main = rain.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.25f, 1.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.03f);
            main.startColor = new Color(0.70f, 0.76f, 0.84f, 0.26f);
            main.maxParticles = 7000;

            var em = rain.emission;
            em.rateOverTime = dropsPerSecond;

            var sh = rain.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(area, 0.2f, area);

            // Fall speed and slant come from velocity, not start speed, so the wind is in world
            // space no matter how the emitter is oriented.
            var vel = rain.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Weather.Wind.x * 0.85f, Weather.Wind.x * 1.15f);
            vel.y = new ParticleSystem.MinMaxCurve(-19f, -15.5f);
            vel.z = new ParticleSystem.MinMaxCurve(Weather.Wind.z * 0.85f, Weather.Wind.z * 1.15f);

            var col = rain.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Medium;   // Low misses thin roofs
            col.collidesWith = Weather.WorldMask;
            col.lifetimeLoss = 1f;
            col.bounce = 0f;
            col.dampen = 1f;
            col.radiusScale = 0.5f;
            col.enableDynamicColliders = false;
            col.maxCollisionShapes = 128;

            var r = rain.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 2.2f;
            r.cameraVelocityScale = 0f;

            var subs = rain.subEmitters;
            subs.enabled = true;
            subs.AddSubEmitter(Splash(rain.transform), ParticleSystemSubEmitterType.Collision,
                               ParticleSystemSubEmitterProperties.InheritNothing, 0.35f);
            subs.AddSubEmitter(Ripple(rain.transform), ParticleSystemSubEmitterType.Collision,
                               ParticleSystemSubEmitterProperties.InheritNothing, 0.22f);
            return rain.gameObject;
        }

        /// <summary>A couple of droplets kicked up where a drop lands.</summary>
        static ParticleSystem Splash(Transform parent)
        {
            var ps = ParticleKit.New(parent, "Splash", Vector3.zero, 12u,
                                     Mat.Particle(Tex.SoftDot(32, 3f), additive: false, soft: false));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
            main.startColor = new Color(0.75f, 0.80f, 0.86f, 0.45f);
            main.gravityModifier = 1.6f;
            main.maxParticles = 1500;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 3) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 32f;
            sh.radius = 0.01f;
            sh.rotation = new Vector3(-90f, 0f, 0f);

            FadeOut(ps);
            return ps;
        }

        /// <summary>
        /// A ring spreading on the surface. Lifted a couple of centimetres so it can't z-fight
        /// the ground, and not a soft particle: soft particles fade where they meet geometry,
        /// and a ripple is nothing but meeting geometry.
        /// </summary>
        static ParticleSystem Ripple(Transform parent)
        {
            var ps = ParticleKit.New(parent, "Ripple", Vector3.zero, 13u,
                                     Mat.Particle(Tex.Ring(), additive: false, soft: false));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.36f);
            main.startColor = new Color(0.80f, 0.85f, 0.90f, 0.30f);
            main.maxParticles = 900;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.0001f;
            sh.position = new Vector3(0f, 0.02f, 0f);

            Grow(ps, 0.15f, 1f);
            FadeOut(ps);
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return ps;
        }

        // -------------------------------------------------------------------------- fire

        /// <summary>
        /// A fire at unit scale - flame tongues, rising embers, smoke and a flickering light.
        /// FireFx.Create scales it; the systems scale with it.
        /// </summary>
        public static GameObject Fire() => BuildFire("Fire", column: false);

        /// <summary>
        /// A burning wreck: the same fire under a towering smoke column that leans with the wind
        /// - the town burning on the horizon. The column keeps its own size whatever the fire's.
        /// </summary>
        public static GameObject BurningWreck() => BuildFire("BurningWreck", column: true);

        static GameObject BuildFire(string name, bool column)
        {
            var root = new GameObject(name);
            var fire = root.AddComponent<FireFx>();

            var flames = Child(root.transform, "Flames", 51u, ParticleKit.Flame, looping: true);
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            main.gravityModifier = -0.08f;
            main.maxParticles = 90;
            var em = flames.emission;
            em.rateOverTime = 30f;
            Cone(flames, 10f, 0.18f, up: true);
            var col = flames.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(
                new[] { (0f, new Color(1f, 0.86f, 0.58f)), (0.35f, new Color(1f, 0.52f, 0.16f)),
                        (0.75f, new Color(0.62f, 0.16f, 0.04f)), (1f, new Color(0.2f, 0.05f, 0.02f)) },
                new[] { (0f, 0f), (0.12f, 0.95f), (0.6f, 0.55f), (1f, 0f) });
            var size = flames.sizeOverLifetime;
            size.enabled = true;
            size.size = ParticleKit.Curve((0f, 0.7f), (0.3f, 1f), (1f, 0.25f));
            Noise(flames, 0.35f, 1.4f, 0.8f);

            var embers = Child(root.transform, "Embers", 52u, ParticleKit.Ember, looping: true);
            embers.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new Color(1f, 0.62f, 0.25f);
            main.gravityModifier = -0.12f;
            main.maxParticles = 60;
            em = embers.emission;
            em.rateOverTime = 6f;
            Cone(embers, 25f, 0.2f, up: true);
            col = embers.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(
                new[] { (0f, new Color(1f, 0.8f, 0.4f)), (1f, new Color(0.8f, 0.18f, 0.05f)) },
                new[] { (0f, 1f), (0.7f, 0.8f), (1f, 0f) });
            Noise(embers, 0.8f, 0.6f, 0f);

            var smoke = Child(root.transform, column ? "SmokeColumn" : "Smoke", 53u, ParticleKit.Smoke, looping: true);
            smoke.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            main = smoke.main;
            main.startLifetime = column ? new ParticleSystem.MinMaxCurve(16f, 22f) : new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = column ? new ParticleSystem.MinMaxCurve(2.6f, 3.6f) : new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.startSize = column ? new ParticleSystem.MinMaxCurve(5f, 8f) : new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = column ? new Color(0.07f, 0.07f, 0.075f, 0.62f) : new Color(0.10f, 0.10f, 0.10f, 0.45f);
            main.maxParticles = column ? 240 : 60;
            // The column is sized for the skyline, not for the fire under it.
            if (column) main.scalingMode = ParticleSystemScalingMode.Local;
            em = smoke.emission;
            em.rateOverTime = column ? 4.5f : 4f;
            Cone(smoke, column ? 6f : 12f, column ? 2.2f : 0.25f, up: true);
            Wind(smoke, column ? 1.4f : 0.6f);
            col = smoke.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(
                new[] { (0f, Color.white), (1f, new Color(0.75f, 0.75f, 0.78f)) },
                new[] { (0f, 0f), (0.1f, 1f), (0.65f, 0.7f), (1f, 0f) });
            size = smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = column ? ParticleKit.Curve((0f, 0.6f), (1f, 4f)) : ParticleKit.Curve((0f, 0.5f), (1f, 3f));
            var rot = smoke.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            var light = new GameObject("FireLight").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.22f);
            light.range = 6.5f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;
            fire.Light = light;
            return root;
        }

        // ------------------------------------------------------------------------ puddle

        /// <summary>Standing water with raindrops rippling across it, in four outline variants.</summary>
        public static GameObject Puddle()
        {
            var root = new GameObject("Puddle");
            var puddle = root.AddComponent<PuddleFx>();

            // Near-black and mirror-smooth: water reads by what it reflects, not its colour.
            puddle.Variants = new Material[4];
            for (int i = 0; i < puddle.Variants.Length; i++)
                puddle.Variants[i] = Mat.Decal(Tex.Puddle(256, i + 7), new Color(0.045f, 0.05f, 0.055f), 0.96f);

            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Water";
            Object.DestroyImmediate(surface.GetComponent<Collider>());
            surface.transform.SetParent(root.transform, false);
            surface.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var r = surface.GetComponent<MeshRenderer>();
            r.sharedMaterial = puddle.Variants[0];
            r.shadowCastingMode = ShadowCastingMode.Off;
            puddle.Surface = r;

            var ripples = Child(root.transform, "Ripples", 61u, Mat.Particle(Tex.Ring(), additive: false, soft: false),
                                 local: true, looping: true);
            ripples.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            var main = ripples.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.24f);
            main.startColor = new Color(0.85f, 0.9f, 0.95f, 0.35f);
            main.maxParticles = 200;
            var em = ripples.emission;
            em.rateOverTime = 10f;
            var sh = ripples.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(1f, 0f, 1f);
            Grow(ripples, 0.15f, 1f);
            FadeOut(ripples);
            ripples.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            puddle.Ripples = ripples;
            return root;
        }

        // ----------------------------------------------------------------------- helpers

        /// <summary>A child system. Bursts and effects scale with their parent unless told otherwise.</summary>
        static ParticleSystem Child(Transform parent, string name, uint seed, Material material,
                                     bool local = false, bool looping = true)
        {
            var ps = ParticleKit.New(parent, name, Vector3.zero, seed, material);
            var main = ps.main;
            main.loop = looping;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            if (local) main.simulationSpace = ParticleSystemSimulationSpace.Local;
            return ps;
        }

        /// <summary>Fired by FlashFx, never on its own: one burst as the setting, emission off.</summary>
        static void OneShot(ParticleSystem ps, short min, short max)
        {
            var main = ps.main;
            main.loop = true;
            main.maxParticles = Mathf.Max(64, max * 12);
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, min, max) });
            em.enabled = false;
        }

        static void NoShape(ParticleSystem ps)
        {
            var sh = ps.shape;
            sh.enabled = false;
        }

        static void Cone(ParticleSystem ps, float angle, float radius, bool up = false)
        {
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = angle;
            sh.radius = radius;
            if (up) sh.rotation = new Vector3(-90f, 0f, 0f);   // cone axis +Z turned to point up
        }

        static void Sphere(ParticleSystem ps, float radius, bool hemisphere)
        {
            var sh = ps.shape;
            sh.shapeType = hemisphere ? ParticleSystemShapeType.Hemisphere : ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            if (hemisphere) sh.rotation = new Vector3(-90f, 0f, 0f);   // dome facing up
        }

        static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velocityScale;
            r.lengthScale = lengthScale;
        }

        static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = ParticleKit.Curve((0f, from), (1f, to));
        }

        static void FadeOut(ParticleSystem ps, float holdUntil = 0f)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = holdUntil > 0f
                ? ParticleKit.Gradient(new[] { (0f, Color.white), (1f, Color.white) },
                                       new[] { (0f, 1f), (Mathf.Clamp(holdUntil, 0.01f, 0.99f), 1f), (1f, 0f) })
                : ParticleKit.Gradient(new[] { (0f, Color.white), (1f, Color.white) },
                                       new[] { (0f, 1f), (1f, 0f) });
        }

        /// <summary>Fades in fast to <paramref name="peak"/> alpha, then away - a soft puff.</summary>
        static void Puff(ParticleSystem ps, float peak)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(new[] { (0f, Color.white), (1f, Color.white) },
                                             new[] { (0f, 0f), (0.08f, peak), (1f, 0f) });
        }

        /// <summary>Air resistance: the puff flies out and slows to a drift rather than a stop.</summary>
        static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.dampen = 0f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
        }

        static void Wind(ParticleSystem ps, float drift)
        {
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Weather.Wind.x * drift * 0.7f, Weather.Wind.x * drift * 1.3f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0.2f);
            vel.z = new ParticleSystem.MinMaxCurve(Weather.Wind.z * drift * 0.7f, Weather.Wind.z * drift * 1.3f);
        }

        static void Noise(ParticleSystem ps, float strength, float frequency, float scroll)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = scroll;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }
    }
}
