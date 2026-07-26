using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Generates the victory fanfare as a WAV.
    ///
    /// Synthesised rather than downloaded, unlike the two music tracks. Those are real recordings
    /// because a tune has to sound like a tune, and the earlier attempt to synthesise creature voices
    /// proved how badly that goes — see CreatureVoiceBuilder. A fanfare is different: it is three
    /// notes and a cymbal, it is over in a second and a half, and it needs to sit under a roar
    /// without competing. That is well inside what a few oscillators can do, and it means no licence,
    /// no download, and no fourth-party file in the repository.
    ///
    /// Menu: Dino Battle > 6. Generate Victory Fanfare
    /// </summary>
    public static class FanfareBuilder
    {
        private const int SampleRate = 44100;
        private const string OutputPath = "Assets/Audio/SFX/sfx_victory.wav";

        /// <summary>
        /// A major triad climbing to the octave. The most unambiguous "you won" in Western music, and
        /// the reason every game since the 1980s has used some version of it.
        /// </summary>
        private static readonly (float Semitone, float Start, float Length)[] Notes =
        {
            (0f,  0.00f, 0.16f),   // root
            (4f,  0.13f, 0.16f),   // major third
            (7f,  0.26f, 0.16f),   // fifth
            (12f, 0.39f, 0.85f),   // octave, held
        };

        private const float Root = 392f;     // G4 — high enough to cut through a roar
        private const float Length = 1.5f;

        [MenuItem("Dino Battle/6. Generate Victory Fanfare", priority = 131)]
        public static void Generate()
        {
            int count = Mathf.RoundToInt(SampleRate * Length);
            var samples = new float[count];

            foreach (var (semitone, start, length) in Notes) AddNote(samples, semitone, start, length);
            AddCymbal(samples);

            Normalise(samples, 0.85f);
            WriteWav(OutputPath, samples);

            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[FanfareBuilder] Wrote {OutputPath} ({Length:0.00}s).");
        }

        /// <summary>
        /// One note: a sawtooth softened by its own harmonics, with a fast attack and a long tail.
        ///
        /// Three harmonics rather than a pure sine. A sine reads as a test tone; the harmonics are
        /// what make it sound like an instrument playing a note.
        /// </summary>
        private static void AddNote(float[] samples, float semitone, float start, float length)
        {
            float frequency = Root * Mathf.Pow(2f, semitone / 12f);
            int from = Mathf.RoundToInt(start * SampleRate);
            int span = Mathf.RoundToInt(length * SampleRate);

            for (int i = 0; i < span; i++)
            {
                int index = from + i;
                if (index < 0 || index >= samples.Length) continue;

                float t = i / (float)SampleRate;
                float progress = i / (float)span;

                // 4ms attack, then an exponential fall. Anything slower on the attack and the
                // arpeggio smears into one chord.
                float attack = Mathf.Min(1f, t / 0.004f);
                float decay = Mathf.Exp(-3.2f * progress);
                float envelope = attack * decay;

                float phase = 2f * Mathf.PI * frequency * t;
                float tone = Mathf.Sin(phase)
                           + 0.45f * Mathf.Sin(phase * 2f)
                           + 0.22f * Mathf.Sin(phase * 3f);

                samples[index] += tone * envelope * 0.30f;
            }
        }

        /// <summary>
        /// A cymbal on the final note: filtered noise with a sharp attack.
        ///
        /// Marks the landing. Without it the arpeggio just stops, which sounds like the sound failing
        /// rather than finishing.
        /// </summary>
        private static void AddCymbal(float[] samples)
        {
            var random = new System.Random(20260726);
            int from = Mathf.RoundToInt(0.39f * SampleRate);
            int span = Mathf.RoundToInt(0.7f * SampleRate);

            // One-pole high-pass, so it hisses rather than rumbles under the notes.
            float previous = 0f;
            float previousFiltered = 0f;

            for (int i = 0; i < span; i++)
            {
                int index = from + i;
                if (index < 0 || index >= samples.Length) continue;

                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                float filtered = 0.92f * (previousFiltered + noise - previous);
                previous = noise;
                previousFiltered = filtered;

                float progress = i / (float)span;
                float envelope = Mathf.Min(1f, i / (SampleRate * 0.002f)) * Mathf.Exp(-4.5f * progress);

                samples[index] += filtered * envelope * 0.18f;
            }
        }

        private static void Normalise(float[] samples, float peak)
        {
            float loudest = 0f;
            foreach (float sample in samples) loudest = Mathf.Max(loudest, Mathf.Abs(sample));
            if (loudest < 1e-5f) return;

            float gain = peak / loudest;
            for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
        }

        /// <summary>16-bit mono PCM WAV, the same format CreatureVoiceBuilder writes.</summary>
        private static void WriteWav(string path, float[] samples)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets/Audio/SFX");

            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);

            int dataBytes = samples.Length * 2;

            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);

            foreach (float sample in samples)
            {
                writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }
        }
    }
}
