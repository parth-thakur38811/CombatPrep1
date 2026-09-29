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

        /// <summary>
        /// A seamless rain loop, three layers deep:
        ///   hiss   - band-passed noise, the sheet of rain in the distance
        ///   patter - hundreds of tiny droplet transients a second, the rain near you
        ///   wash   - low rumble underneath, the weight of a downpour
        /// Slow gusting swells the hiss. The tail is crossfaded into the head so the clip
        /// loops without a seam. The same generator makes rain-on-a-roof by trading hiss for
        /// fewer, heavier, lower drops (<paramref name="patterHz"/> sets their ring).
        /// </summary>
        public static AudioClip RainLoop(string name, int seed, float seconds = 5f, float patterRate = 540f,
                                         float patterHz = 3200f, float hiss = 0.55f, float patter = 0.6f,
                                         float wash = 0.35f, float gain = 0.5f)
        {
            var rng = new System.Random(seed);
            int n = Mathf.CeilToInt(SampleRate * seconds);
            int xf = Mathf.CeilToInt(SampleRate * 0.6f);
            var raw = new float[n + xf];

            float hpState = 0f, bpState = 0f, washA = 0f, washB = 0f;
            float aHp = Coeff(480f), aLp = Coeff(6800f), aWash = Coeff(240f);
            float dropEnv = 0f, dropDecay = 0f, dropFreq = 0f, dropPhase = 0f;
            float g1 = (float)rng.NextDouble() * 6.28f, g2 = (float)rng.NextDouble() * 6.28f;

            for (int i = 0; i < raw.Length; i++)
            {
                float t = (float)i / SampleRate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);

                // hiss: high-pass then low-pass
                hpState += aHp * (white - hpState);
                bpState += aLp * ((white - hpState) - bpState);
                float gust = 0.78f + 0.22f * Mathf.Sin(t * 0.83f + g1) * Mathf.Sin(t * 0.31f + g2);

                // wash: twice-filtered noise, near brown
                washA += aWash * (white - washA);
                washB += aWash * (washA - washB);

                // patter: a droplet fires at random; each is a ringing blip in a burst of noise
                if (rng.NextDouble() < patterRate / SampleRate)
                {
                    dropEnv = Mathf.Pow((float)rng.NextDouble(), 2.2f) * 0.9f + 0.06f;
                    dropDecay = Mathf.Lerp(900f, 2600f, (float)rng.NextDouble());
                    dropFreq = patterHz * Mathf.Lerp(0.6f, 1.6f, (float)rng.NextDouble());
                    dropPhase = 0f;
                }
                dropEnv *= 1f - dropDecay / SampleRate;
                dropPhase += 2f * Mathf.PI * dropFreq / SampleRate;
                float drop = (Mathf.Sin(dropPhase) * 0.6f + white * 0.4f) * dropEnv;

                float s = bpState * hiss * gust + drop * patter + washB * wash * 5f;
                raw[i] = SoftClip(s * 1.2f) * gain;
            }

            // Fold the overrun back over the start: equal-power crossfade, seamless loop.
            var data = new float[n];
            System.Array.Copy(raw, data, n);
            for (int k = 0; k < xf; k++)
            {
                float a = (float)k / xf;
                data[k] = data[k] * Mathf.Sqrt(a) + raw[n + k] * Mathf.Sqrt(1f - a);
            }
            return Make(name, data);
        }

        /// <summary>
        /// Thunder. A rolling rumble is heavily low-passed noise shaped by several overlapping
        /// swells - each a different stretch of the bolt's path arriving at a different time,
        /// which is why real thunder rolls rather than bangs. A close strike adds the tearing
        /// crack at the front; a far one keeps only the low roll.
        /// </summary>
        public static AudioClip Thunder(string name, int seed, bool close, float seconds = 7f, float gain = 0.9f)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();

            int n = Mathf.CeilToInt(SampleRate * seconds);
            var data = new float[n];

            int rolls = close ? 7 : 5;
            var at = new float[rolls];
            var amp = new float[rolls];
            var rise = new float[rolls];
            var fall = new float[rolls];
            for (int k = 0; k < rolls; k++)
            {
                at[k] = k == 0 ? (close ? 0.02f : R(0.1f, 0.5f)) : R(close ? 0.08f : 0.3f, seconds * 0.55f);
                amp[k] = k == 0 && close ? 1f : R(0.35f, 0.95f);
                rise[k] = R(0.04f, 0.28f);
                fall[k] = R(0.5f, 1.6f);
            }

            float lp1 = 0f, lp2 = 0f, lp3 = 0f, mid = 0f, crackLp = 0f, subPhase = 0f;
            float aLow = Coeff(close ? 170f : 105f), aMid = Coeff(900f), aCrack = Coeff(2400f);

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);

                lp1 += aLow * (white - lp1);
                lp2 += aLow * (lp1 - lp2);
                lp3 += aLow * (lp2 - lp3);

                float env = 0.22f * Mathf.Exp(-t / (seconds * 0.35f));
                for (int k = 0; k < rolls; k++)
                {
                    float dt = t - at[k];
                    if (dt <= 0f) continue;
                    env += amp[k] * (dt < rise[k] ? dt / rise[k] : Mathf.Exp(-(dt - rise[k]) / fall[k]));
                }

                // Rattle: bursts of mid-band grit riding the roll.
                mid += aMid * (white - mid);
                float rattle = (white - mid) * env * (close ? 0.10f : 0.04f) * (rng.NextDouble() < 0.03 ? 3f : 0.4f);

                subPhase += 2f * Mathf.PI * (36f + 6f * Mathf.Sin(t * 0.7f)) / SampleRate;
                float sub = Mathf.Sin(subPhase) * env * 0.05f;

                float crack = 0f;
                if (close && t < 0.7f)
                {
                    crackLp += aCrack * (white - crackLp);
                    float rip = rng.NextDouble() < 0.5 ? 1f : 0.3f;
                    crack = (white - crackLp) * Mathf.Exp(-t * 7.5f) * 0.55f * rip;
                }

                data[i] = lp3 * env * 9f + sub + rattle + crack;
            }

            // Normalise, then saturate gently so the peaks round off instead of clipping.
            float peak = 0.0001f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float scale = 0.95f / peak;
            for (int i = 0; i < n; i++) data[i] = SoftClip(data[i] * scale * 1.3f) * gain;

            Fade(data, 0.002f, 0.9f);
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
