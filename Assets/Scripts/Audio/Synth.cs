using UnityEngine;

namespace CombatPrep.Audio
{
    /// <summary>
    /// Every sound in the game is synthesised into an AudioClip at startup - no audio files.
    ///
    /// A gunshot is three layered voices, which is roughly how the real thing decomposes:
    ///   crack - high-passed noise, very fast decay (the supersonic snap)
    ///   body  - a sine sweeping downward, medium decay (the low thump you feel)
    ///   tail  - low-passed noise, slow decay (the room)
    /// Sum them, soft-clip so the transient saturates instead of digitally clipping, then
    /// fade the first and last couple of milliseconds so there is no edge click.
    /// </summary>
    public static class Synth
    {
        const int SampleRate = 44100;

        public static AudioClip GunShot(string name, float gain, float decay, float bodyHz, float crack, float tail)
        {
            float length = 0.55f;
            int n = Mathf.CeilToInt(SampleRate * length);
            var data = new float[n];

            float lp = 0f, hpPrev = 0f, hpOut = 0f, phase = 0f;
            float lpA = Coeff(900f);     // tail darkness
            float hpA = Coeff(2600f);    // crack brightness

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Random.value * 2f - 1f;

                // crack: one-pole high-pass, sharp envelope
                lp += hpA * (noise - lp);
                hpOut = noise - lp;
                float crackSig = hpOut * Mathf.Exp(-t * decay * 2.4f) * crack;

                // body: sine sweeping from bright to low as pressure drops
                float f = bodyHz * Mathf.Lerp(2.4f, 0.55f, Mathf.Clamp01(t * 26f));
                phase += 2f * Mathf.PI * f / SampleRate;
                float bodySig = Mathf.Sin(phase) * Mathf.Exp(-t * decay * 0.75f) * 0.9f;

                // tail: low-passed noise, slow decay
                hpPrev += lpA * (noise - hpPrev);
                float tailSig = hpPrev * Mathf.Exp(-t * 7.5f) * tail;

                float s = crackSig + bodySig + tailSig;
                data[i] = SoftClip(s * 1.6f) * gain;
            }

            Fade(data, 0.001f, 0.04f);
            return Make(name, data);
        }

        public static AudioClip Impact(string name, float pitch, float gain = 0.5f)
        {
            int n = Mathf.CeilToInt(SampleRate * 0.18f);
            var data = new float[n];
            float lp = 0f;
            float a = Coeff(1400f * pitch);

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Random.value * 2f - 1f;
                lp += a * (noise - lp);
                data[i] = SoftClip(lp * Mathf.Exp(-t * 42f) * 2.2f) * gain;
            }

            Fade(data, 0.0008f, 0.02f);
            return Make(name, data);
        }

        /// <summary>Short tonal blip. Two tones stacked gives the headshot/kill variants.</summary>
        public static AudioClip Blip(string name, float hz, float decayRate, float gain = 0.35f, float secondHz = 0f)
        {
            int n = Mathf.CeilToInt(SampleRate * 0.16f);
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float env = Mathf.Exp(-t * decayRate);
                float s = Mathf.Sin(2f * Mathf.PI * hz * t) * env;
                if (secondHz > 0f) s += Mathf.Sin(2f * Mathf.PI * secondHz * t) * env * 0.6f;
                data[i] = s * gain;
            }

            Fade(data, 0.002f, 0.02f);
            return Make(name, data);
        }

        /// <summary>Mechanical click - a filtered noise transient. Mag out, mag in, bolt release.</summary>
        public static AudioClip Click(string name, float brightness, float gain = 0.45f)
        {
            int n = Mathf.CeilToInt(SampleRate * 0.09f);
            var data = new float[n];
            float lp = 0f;
            float a = Coeff(1800f * brightness);

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Random.value * 2f - 1f;
                lp += a * (noise - lp);
                float hp = noise - lp;
                data[i] = SoftClip((hp * 0.8f + lp * 0.3f) * Mathf.Exp(-t * 150f) * 3f) * gain;
            }

            Fade(data, 0.0005f, 0.01f);
            return Make(name, data);
        }


        /// <summary>
        /// Grenade blast: a deep sine sweeping down for the pressure wave, a broadband
        /// crack for the detonation itself, and a long low-passed rumble for the tail.
        /// Much slower decay than a gunshot - the tail is what sells the size.
        /// </summary>
        public static AudioClip Explosion(string name, float gain = 1f)
        {
            float length = 1.9f;
            int n = Mathf.CeilToInt(SampleRate * length);
            var data = new float[n];

            float lowState = 0f, rumbleState = 0f, phase = 0f;
            float lowA = Coeff(220f);
            float rumbleA = Coeff(90f);

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Random.value * 2f - 1f;

                // Pressure wave: sweeps from ~110 Hz down to ~26 Hz.
                float f = Mathf.Lerp(110f, 26f, Mathf.Clamp01(t * 2.2f));
                phase += 2f * Mathf.PI * f / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * 2.4f) * 1.1f;

                // Detonation crack, gone almost immediately.
                lowState += lowA * (noise - lowState);
                float crack = (noise - lowState) * Mathf.Exp(-t * 34f) * 0.85f;

                // Rumble tail.
                rumbleState += rumbleA * (noise - rumbleState);
                float tail = rumbleState * Mathf.Exp(-t * 1.7f) * 0.9f;

                data[i] = SoftClip((body + crack + tail) * 1.7f) * gain;
            }

            Fade(data, 0.0006f, 0.25f);
            return Make(name, data);
        }

        // --- helpers ---

        static float Coeff(float cutoffHz) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / SampleRate);

        static float SoftClip(float x) => (float)System.Math.Tanh(x);

        static void Fade(float[] data, float inSeconds, float outSeconds)
        {
            int fi = Mathf.Max(1, Mathf.CeilToInt(SampleRate * inSeconds));
            int fo = Mathf.Max(1, Mathf.CeilToInt(SampleRate * outSeconds));
            for (int i = 0; i < fi && i < data.Length; i++) data[i] *= (float)i / fi;
            for (int i = 0; i < fo && i < data.Length; i++)
                data[data.Length - 1 - i] *= (float)i / fo;
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
