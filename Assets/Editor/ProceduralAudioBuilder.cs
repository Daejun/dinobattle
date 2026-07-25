using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Synthesises the creature sound effects and writes them out as WAV assets.
    ///
    /// Generated rather than downloaded, for the same reason the scenes and prefabs are: the whole
    /// project reproduces its content from code, and a fresh clone should not depend on someone
    /// having fetched an asset pack by hand. It also sidesteps the licensing bookkeeping — nothing
    /// here needs an entry in ATTRIBUTIONS.md.
    ///
    /// These are placeholders in the same sense the capsule creatures were: good enough to hear a
    /// fight, meant to be replaced by recorded audio later. Docs/assets.md lists CC0 sources.
    ///
    /// Menu: Dino Battle > 5. Generate Creature Audio
    /// </summary>
    public static class ProceduralAudioBuilder
    {
        private const int SampleRate = 44100;
        private const string AudioFolder = "Assets/Audio/SFX";

        [MenuItem("Dino Battle/5. Generate Creature Audio", priority = 130)]
        public static void Generate()
        {
            SampleContentBuilder.EnsureFolder("Assets/Audio");
            SampleContentBuilder.EnsureFolder(AudioFolder);

            // Seeded so regenerating produces byte-identical files instead of churning the repo.
            Write("sfx_bite_small", Bite(seed: 11, basePitch: 320f, length: 0.22f));
            Write("sfx_bite_large", Bite(seed: 12, basePitch: 140f, length: 0.34f));
            Write("sfx_roar_small", Roar(seed: 21, basePitch: 180f, length: 0.9f));
            Write("sfx_roar_large", Roar(seed: 22, basePitch: 72f, length: 1.6f));
            Write("sfx_death_small", Death(seed: 31, basePitch: 200f, length: 1.0f));
            Write("sfx_death_large", Death(seed: 32, basePitch: 85f, length: 1.5f));

            AssetDatabase.Refresh();
            Debug.Log($"[ProceduralAudioBuilder] Wrote 6 clips to {AudioFolder}.");
        }

        // ---------------------------------------------------------------- synthesis

        /// <summary>
        /// Short wet snap: a noise burst shaped by a fast attack and steep decay, with a low thump
        /// underneath so it carries weight rather than sounding like static.
        /// </summary>
        private static float[] Bite(int seed, float basePitch, float length)
        {
            var random = new System.Random(seed);
            int count = (int)(SampleRate * length);
            var samples = new float[count];

            float lowpass = 0f;

            // A body around the impact. Dry noise plus a sine reads as a click on a speaker; two
            // resonances give the snap somewhere to happen, so it lands as flesh and bone rather
            // than as a burst of static.
            float scale = Mathf.Clamp(basePitch / 220f, 0.5f, 1.5f);
            var body = new Resonator(420f * scale, 3.5f);
            var crack = new Resonator(1650f * scale, 6f);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = i / (float)count;

                // Very fast attack, exponential decay — the shape of a jaw snapping shut.
                float envelope = Mathf.Min(1f, progress / 0.02f) * Mathf.Exp(-progress * 9f);

                float noise = (float)(random.NextDouble() * 2.0 - 1.0);

                // One-pole lowpass, opening as the bite lands so it starts dull and turns crisp.
                float cutoff = Mathf.Lerp(0.05f, 0.4f, progress);
                lowpass += (noise - lowpass) * cutoff;

                float thump = Mathf.Sin(2f * Mathf.PI * basePitch * t * Mathf.Exp(-progress * 2.5f));

                // The crack decays much faster than the body: teeth meeting is over long before the
                // weight behind them has finished arriving.
                float resonant = body.Process(lowpass) * 0.9f
                               + crack.Process(noise) * 0.5f * Mathf.Exp(-progress * 26f);

                samples[i] = (lowpass * 0.35f + resonant + thump * 0.45f) * envelope;
            }

            return Normalise(samples, 0.85f);
        }

        /// <summary>
        /// Layered growl: a detuned sawtooth stack with slow vibrato, plus breath noise. Real
        /// dinosaur calls are unknowable, so this follows the usual trick of stacking low animal-like
        /// tones — see the note in Docs/assets.md.
        /// </summary>
        private static float[] Roar(int seed, float basePitch, float length)
        {
            var random = new System.Random(seed);
            int count = (int)(SampleRate * length);
            var samples = new float[count];

            // Formants scale with the animal, not with the note it is producing. Tying them to the
            // base pitch is what makes the small and large variants sound like different-sized
            // throats rather than the same throat transposed.
            float scale = Mathf.Clamp(basePitch / 120f, 0.55f, 1.6f);
            var f1 = new Resonator(320f * scale, 6f);
            var f2 = new Resonator(830f * scale, 8f);
            var f3 = new Resonator(2400f * scale, 11f);

            float breath = 0f;
            float jitter = 0f;
            float phase = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = i / (float)count;

                // Fast onset, long fall. A symmetric swell reads as a machine winding up; animals
                // start a call abruptly and run out of air slowly.
                float envelope = Mathf.Min(1f, progress / 0.08f) * Mathf.Pow(1f - progress, 0.7f);

                // Jitter — small random pitch wander. Perfectly steady pitch is the single clearest
                // giveaway of a synthesised voice; real vocal folds never hold one.
                jitter += ((float)(random.NextDouble() * 2.0 - 1.0) - jitter) * 0.004f;

                float drift = Mathf.Lerp(1.1f, 0.82f, progress);
                float pitch = basePitch * drift * (1f + jitter * 0.06f);

                phase += pitch / SampleRate;
                phase -= Mathf.Floor(phase);

                // Sawtooth glottal source plus a half-rate component. Period doubling is the physical
                // origin of the rough, torn quality in a big animal's roar, and it is why this reads
                // as a growl rather than a hum.
                float source = 2f * phase - 1f;
                source += (2f * (phase * 0.5f - Mathf.Floor(phase * 0.5f)) - 1f) * 0.45f;

                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                breath += (noise - breath) * 0.35f;
                source += breath * 0.5f;

                // Through the throat.
                float voiced = f1.Process(source) * 1.0f
                             + f2.Process(source) * 0.55f
                             + f3.Process(source) * 0.2f;

                // Roughness: amplitude modulation well above vibrato rate, which is heard as texture
                // rather than as wobble.
                float roughness = 1f - 0.25f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 38f * t));

                samples[i] = voiced * roughness * envelope;
            }

            return Normalise(samples, 0.9f);
        }

        /// <summary>A roar that loses pitch and power as it goes, ending in a rattle.</summary>
        private static float[] Death(int seed, float basePitch, float length)
        {
            var random = new System.Random(seed);
            int count = (int)(SampleRate * length);
            var samples = new float[count];

            float scale = Mathf.Clamp(basePitch / 120f, 0.55f, 1.6f);
            var f1 = new Resonator(300f * scale, 5f);
            var f2 = new Resonator(760f * scale, 7f);

            float rattle = 0f;
            float phase = 0f;

            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)count;

                float envelope = Mathf.Min(1f, progress / 0.05f) * Mathf.Exp(-progress * 2.2f);

                // Falls a long way — the pitch drop is what reads as "dying" rather than "shouting".
                float pitch = basePitch * Mathf.Lerp(1f, 0.45f, progress);

                phase += pitch / SampleRate;
                phase -= Mathf.Floor(phase);

                float source = 2f * phase - 1f;

                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                rattle += (noise - rattle) * 0.25f;

                // Breath takes over from voice as the call collapses — the voiced part drops away
                // while the rattle rises, which is the shape of a last exhalation.
                float voiced = (f1.Process(source) + f2.Process(source) * 0.6f)
                             * Mathf.Lerp(1f, 0.25f, progress);

                samples[i] = (voiced + rattle * Mathf.Lerp(0.15f, 0.7f, progress)) * envelope;
            }

            return Normalise(samples, 0.85f);
        }

        private static float Saw(float frequency, float t)
        {
            float phase = frequency * t;
            return 2f * (phase - Mathf.Floor(phase + 0.5f));
        }

        /// <summary>
        /// A resonant band-pass, run one sample at a time.
        ///
        /// This is the piece that was missing. Almost everything that makes a vocalisation sound like
        /// it came from an animal rather than an oscillator is formants — fixed resonances of the
        /// throat and mouth that emphasise particular bands regardless of the pitch being sung. A
        /// harmonic stack without them is a synth pad; the same stack through two or three of these
        /// is recognisably a voice, and moving the resonances down is what makes the same source read
        /// as a much larger animal.
        ///
        /// Standard RBJ band-pass biquad, constant skirt gain.
        /// </summary>
        private sealed class Resonator
        {
            private readonly float b0, b1, b2, a1, a2;
            private float x1, x2, y1, y2;

            public Resonator(float frequency, float q)
            {
                float omega = 2f * Mathf.PI * frequency / SampleRate;
                float sin = Mathf.Sin(omega);
                float cos = Mathf.Cos(omega);
                float alpha = sin / (2f * q);

                float a0 = 1f + alpha;
                b0 = (sin * 0.5f) / a0;
                b1 = 0f;
                b2 = -(sin * 0.5f) / a0;
                a1 = (-2f * cos) / a0;
                a2 = (1f - alpha) / a0;
            }

            public float Process(float input)
            {
                float output = b0 * input + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;

                x2 = x1;
                x1 = input;
                y2 = y1;
                y1 = output;

                return output;
            }
        }

        /// <summary>Scale to a target peak so every clip sits at a comparable loudness in game.</summary>
        private static float[] Normalise(float[] samples, float peak)
        {
            float max = 0f;
            foreach (float s in samples) max = Mathf.Max(max, Mathf.Abs(s));
            if (max < 0.0001f) return samples;

            float gain = peak / max;
            for (int i = 0; i < samples.Length; i++) samples[i] *= gain;

            return samples;
        }

        // ---------------------------------------------------------------- WAV output

        /// <summary>
        /// Write mono 16-bit PCM. Unity can build an AudioClip in memory but cannot serialise one as
        /// a usable asset, so the clips go to disk as WAV and are imported normally.
        /// </summary>
        private static void Write(string name, float[] samples)
        {
            string path = $"{AudioFolder}/{name}.wav";
            string full = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, path);

            using var stream = new FileStream(full, FileMode.Create);
            using var writer = new BinaryWriter(stream);

            int dataBytes = samples.Length * 2;

            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });

            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);                    // PCM chunk size
            writer.Write((short)1);              // format: PCM
            writer.Write((short)1);              // channels: mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);        // byte rate
            writer.Write((short)2);              // block align
            writer.Write((short)16);             // bits per sample

            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);

            foreach (float sample in samples)
            {
                writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }
        }
    }
}
