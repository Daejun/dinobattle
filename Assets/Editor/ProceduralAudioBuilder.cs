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

                samples[i] = (lowpass * 0.75f + thump * 0.45f) * envelope;
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

            float breath = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = i / (float)count;

                // Swell in, hold, fall away.
                float envelope = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
                envelope *= envelope;

                // Vibrato plus a slow downward drift, which is what makes a held tone sound alive.
                float vibrato = 1f + Mathf.Sin(2f * Mathf.PI * 5.5f * t) * 0.04f;
                float drift = Mathf.Lerp(1.08f, 0.86f, progress);
                float pitch = basePitch * vibrato * drift;

                float tone = 0f;
                for (int harmonic = 1; harmonic <= 5; harmonic++)
                {
                    // Slight detune per harmonic keeps the stack from sounding like a synth pad.
                    float detune = 1f + (harmonic - 1) * 0.004f;
                    tone += Saw(pitch * harmonic * detune, t) / harmonic;
                }
                tone /= 2.2f;

                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                breath += (noise - breath) * 0.08f;

                samples[i] = (tone * 0.8f + breath * 0.35f) * envelope;
            }

            return Normalise(samples, 0.9f);
        }

        /// <summary>A roar that loses pitch and power as it goes, ending in a rattle.</summary>
        private static float[] Death(int seed, float basePitch, float length)
        {
            var random = new System.Random(seed);
            int count = (int)(SampleRate * length);
            var samples = new float[count];

            float rattle = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = i / (float)count;

                float envelope = Mathf.Min(1f, progress / 0.05f) * Mathf.Exp(-progress * 2.2f);

                // Falls a long way — the pitch drop is what reads as "dying" rather than "shouting".
                float pitch = basePitch * Mathf.Lerp(1f, 0.45f, progress);

                float tone = Saw(pitch, t) * 0.6f + Saw(pitch * 2.01f, t) * 0.25f;

                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                rattle += (noise - rattle) * 0.25f;

                samples[i] = (tone + rattle * Mathf.Lerp(0.15f, 0.6f, progress)) * envelope;
            }

            return Normalise(samples, 0.85f);
        }

        private static float Saw(float frequency, float t)
        {
            float phase = frequency * t;
            return 2f * (phase - Mathf.Floor(phase + 0.5f));
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
