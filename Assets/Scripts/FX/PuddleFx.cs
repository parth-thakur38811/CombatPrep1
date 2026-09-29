using UnityEngine;

namespace CombatPrep.FX
{
    /// <summary>
    /// A puddle: a patch of standing water, and raindrops rippling across it. The prefab holds
    /// a few outline variants so neighbouring puddles don't match.
    /// </summary>
    public class PuddleFx : MonoBehaviour
    {
        public MeshRenderer Surface;
        public ParticleSystem Ripples;
        [Tooltip("Puddle outlines; each puddle takes one.")]
        public Material[] Variants = new Material[0];
        [Tooltip("Ripples per second on each square metre of water.")]
        public float RipplesPerSquareMetre = 5f;

        /// <param name="width">Metres across (x).</param>
        /// <param name="length">Metres along (z).</param>
        /// <param name="seed">Fixed per puddle, so the arena build draws no randomness.</param>
        public void Setup(int variant, float width, float length, uint seed)
        {
            if (Surface != null)
            {
                Surface.transform.localScale = new Vector3(width, length, 1f);
                if (Variants.Length > 0) Surface.sharedMaterial = Variants[Mathf.Abs(variant) % Variants.Length];
            }

            if (Ripples != null)
            {
                Ripples.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Ripples.randomSeed = seed;
                var shape = Ripples.shape;
                shape.scale = new Vector3(width * 0.62f, 0f, length * 0.62f);
                var emission = Ripples.emission;
                emission.rateOverTime = RipplesPerSquareMetre * width * length * 0.6f;
                Ripples.Play();
            }
        }
    }
}
