using System.IO;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Builds the creature voices the way films build them: real animal recordings, slowed down.
    ///
    /// Two earlier attempts missed. Synthesis gave something with the right spectrum and the wrong
    /// character, and picking ready-made "creature" SFX out of a CC0 pack gave monsters rather than
    /// animals — those libraries are full of designed sci-fi growls, which read as a sound effect
    /// however good the measurements look.
    ///
    /// What actually reads as an enormous animal is a real animal played slower. Dropping the
    /// sample rate lowers pitch and stretches time together, exactly as tape does, and it carries the
    /// breath, the irregularity and the throat rattle of the original recording with it — the parts
    /// that are extremely hard to synthesise and immediately obvious when missing. A dog growl at
    /// 0.4x is a large predator, and it is a large predator because a real throat made it.
    ///
    /// Sources are the CC0 animal recordings staged in Assets/Audio/SFX/_src (see ATTRIBUTIONS.md).
    /// </summary>
    public static class CreatureVoiceBuilder
    {
        /// <summary>
        /// Under Editor/ on purpose. Unity excludes anything in an Editor folder from builds, so the
        /// raw recordings stay available to regenerate the voices without adding a megabyte and a
        /// half of unused source audio to the APK.
        /// </summary>
        private const string SourceFolder = "Assets/Editor/AudioSources";
        private const string OutputFolder = "Assets/Audio/SFX";
        private const int SampleRate = 44100;

        private readonly struct Recipe
        {
            public readonly string Output;
            public readonly string Source;

            /// <summary>Playback rate. Below 1 lowers pitch and lengthens the clip together.</summary>
            public readonly float Pitch;

            /// <summary>Seconds of the source to keep.</summary>
            public readonly float SourceSeconds;

            public readonly float Peak;

            /// <summary>
            /// Cut at an attack and impose a decay, instead of taking the loudest sustained stretch.
            ///
            /// A bite is an event, not a sound an animal holds. Selecting by energy always landed in
            /// the middle of the snarl — measured, the finished bite peaked 91% of the way through a
            /// 0.4s clip and took 355ms to get there, which is the envelope of a growl. What makes a
            /// snap read as a snap is that it is loudest immediately and then gone.
            /// </summary>
            public readonly bool Percussive;

            public Recipe(string output, string source, float pitch, float sourceSeconds,
                          float peak = 0.92f, bool percussive = false)
            {
                Output = output;
                Source = source;
                Pitch = pitch;
                SourceSeconds = sourceSeconds;
                Peak = peak;
                Percussive = percussive;
            }
        }

        private static readonly Recipe[] Recipes =
        {
            // Heavy creatures. 0.38 is about a musical fifth and a half down — far enough that a dog
            // becomes something the size of a bus, close enough that it still articulates.
            new("sfx_roar_large",  "growl1",    0.38f, 0.85f),
            new("sfx_death_large", "voice3",    0.42f, 0.70f),
            new("sfx_bite_large",  "angerdog2", 0.40f, 0.22f, 0.85f, percussive: true),

            // Light creatures. Less shift, so they stay quick and sharp rather than turning into
            // small versions of the same bellow.
            new("sfx_roar_small",  "growl2",    0.68f, 0.55f),
            new("sfx_death_small", "voice1",    0.72f, 0.45f),
            // Same source as the large bite, only faster.
            //
            // Using a different recording for each size let the two drift out of order — the small
            // bite measured lower than the large one, because the loudest slice of one take happened
            // to be darker than the other's. Sharing a source makes the size relationship a property
            // of the pitch alone, and therefore guaranteed rather than lucky.
            new("sfx_bite_small",  "angerdog2", 0.80f, 0.34f, 0.85f, percussive: true),
        };

        [MenuItem("Dino Battle/5. Generate Creature Audio", priority = 130)]
        public static void Build()
        {
            if (!Directory.Exists(SourceFolder))
            {
                Debug.LogError($"[CreatureVoiceBuilder] No sources at {SourceFolder}. " +
                               "Fetch them first — see ATTRIBUTIONS.md for the exact files.");
                return;
            }

            int built = 0;

            foreach (var recipe in Recipes)
            {
                var source = LoadSource(recipe.Source);
                if (source == null)
                {
                    Debug.LogWarning($"[CreatureVoiceBuilder] Source '{recipe.Source}' not found; skipping {recipe.Output}.");
                    continue;
                }

                var mono = ToMono(source);
                var slice = recipe.Percussive
                    ? SharpestOnsetSlice(mono, source.frequency, recipe.SourceSeconds)
                    : LoudestSlice(mono, source.frequency, recipe.SourceSeconds);
                var shifted = Resample(slice, source.frequency, recipe.Pitch);

                if (recipe.Percussive) ShapePercussive(shifted);
                else Shape(shifted);

                Normalise(shifted, recipe.Peak);

                WriteWav($"{OutputFolder}/{recipe.Output}.wav", shifted);
                built++;

                Debug.Log($"[CreatureVoiceBuilder] {recipe.Output}: {recipe.Source} at {recipe.Pitch:0.00}x " +
                          $"-> {shifted.Length / (float)SampleRate:0.00}s");
            }

            AssetDatabase.Refresh();
            Debug.Log($"[CreatureVoiceBuilder] Built {built} voice(s) into {OutputFolder}.");
        }

        private static AudioClip LoadSource(string name)
        {
            foreach (string extension in new[] { "wav", "ogg" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SourceFolder}/{name}.{extension}");
                if (clip != null) return clip;
            }

            return null;
        }

        private static float[] ToMono(AudioClip clip)
        {
            var interleaved = new float[clip.samples * clip.channels];
            clip.GetData(interleaved, 0);

            if (clip.channels == 1) return interleaved;

            var mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                float sum = 0f;
                for (int c = 0; c < clip.channels; c++) sum += interleaved[i * clip.channels + c];
                mono[i] = sum / clip.channels;
            }

            return mono;
        }

        /// <summary>
        /// The loudest window of the requested length.
        ///
        /// Field recordings open and close with room tone, and a roar that starts with half a second
        /// of hiss sounds like a roar played off a tape. Scanning for peak energy lands on the part
        /// of the take that is actually the animal.
        /// </summary>
        private static float[] LoudestSlice(float[] samples, int sourceRate, float seconds)
        {
            int window = Mathf.Min(samples.Length, Mathf.RoundToInt(seconds * sourceRate));
            if (window <= 0 || window >= samples.Length) return samples;

            // Running sum of squares, so the scan is linear rather than quadratic.
            double energy = 0;
            for (int i = 0; i < window; i++) energy += samples[i] * samples[i];

            double best = energy;
            int bestStart = 0;

            for (int i = window; i < samples.Length; i++)
            {
                energy += samples[i] * samples[i];
                energy -= samples[i - window] * samples[i - window];

                if (energy <= best) continue;

                best = energy;
                bestStart = i - window + 1;
            }

            var slice = new float[window];
            System.Array.Copy(samples, bestStart, slice, 0, window);
            return slice;
        }

        /// <summary>
        /// The window starting at the source's sharpest attack.
        ///
        /// <see cref="LoudestSlice"/> is the wrong tool for a bite. It finds the most energetic
        /// stretch, and in a dog recording that is the middle of a sustained snarl — every bite built
        /// that way came out as a growl fragment that swelled to its loudest near the end. These
        /// takes do contain real barks: measured across the six sources, each has attacks that rise
        /// from silence in 7-10ms.
        ///
        /// Finding them is a matter of looking for the biggest RISE in the envelope rather than the
        /// biggest value, then backing up a few milliseconds so the very start of the attack is
        /// included — cutting exactly on the peak of the rise clips the leading edge, which is the
        /// part that carries the snap.
        /// </summary>
        private static float[] SharpestOnsetSlice(float[] samples, int sourceRate, float seconds)
        {
            int window = Mathf.Min(samples.Length, Mathf.RoundToInt(seconds * sourceRate));
            if (window <= 0 || window >= samples.Length) return samples;

            // Short smoothing, so the envelope follows the attack rather than individual cycles.
            int smoothing = Mathf.Max(1, Mathf.RoundToInt(sourceRate * 0.003f));
            var envelope = new float[samples.Length];
            float running = 0f;

            for (int i = 0; i < samples.Length; i++)
            {
                running += Mathf.Abs(samples[i]);
                if (i >= smoothing) running -= Mathf.Abs(samples[i - smoothing]);
                envelope[i] = running / smoothing;
            }

            // Rise over a 10ms lookahead. Long enough to span a whole attack, short enough that a
            // slow swell does not compete with a real one.
            int lookahead = Mathf.Max(1, Mathf.RoundToInt(sourceRate * 0.010f));
            float bestRise = float.NegativeInfinity;
            int bestIndex = 0;

            for (int i = 0; i + lookahead < samples.Length; i++)
            {
                float rise = envelope[i + lookahead] - envelope[i];
                if (rise <= bestRise) continue;

                bestRise = rise;
                bestIndex = i;
            }

            int preroll = Mathf.RoundToInt(sourceRate * 0.008f);
            int start = Mathf.Clamp(bestIndex - preroll, 0, samples.Length - window);

            var slice = new float[window];
            System.Array.Copy(samples, start, slice, 0, window);
            return slice;
        }

        /// <summary>
        /// Resample at <paramref name="pitch"/> times the original rate, output at 44.1kHz.
        ///
        /// Linear interpolation. Slowing down means reading between existing samples rather than
        /// inventing detail above the original bandwidth, so there is no aliasing to filter and the
        /// simple interpolator is adequate — the artefacts a fancier kernel would fix live in the
        /// high end, which is precisely what a pitch drop of this size discards anyway.
        /// </summary>
        private static float[] Resample(float[] samples, int sourceRate, float pitch)
        {
            double sourceStep = (double)sourceRate * pitch / SampleRate;
            int count = (int)(samples.Length / sourceStep);
            var output = new float[Mathf.Max(1, count)];

            for (int i = 0; i < output.Length; i++)
            {
                double position = i * sourceStep;
                int index = (int)position;
                float frac = (float)(position - index);

                float a = samples[Mathf.Clamp(index, 0, samples.Length - 1)];
                float b = samples[Mathf.Clamp(index + 1, 0, samples.Length - 1)];

                output[i] = Mathf.Lerp(a, b, frac);
            }

            return output;
        }

        /// <summary>
        /// Short fades at both ends, so a slice cut out of the middle of a take does not begin and
        /// end on a click.
        /// </summary>
        private static void Shape(float[] samples)
        {
            int fade = Mathf.Min(samples.Length / 8, SampleRate / 40);
            if (fade <= 0) return;

            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                samples[i] *= t;
                samples[samples.Length - 1 - i] *= t;
            }
        }

        /// <summary>
        /// A percussive envelope: near-instant attack, and the recording's own decay after it.
        ///
        /// The attack is 2ms rather than zero only to avoid starting on a discontinuity; at that
        /// length it is inaudible as a fade. The finished clip's attack is longer — the recording's
        /// own 9ms rise is stretched by the pitch drop, so the heavy bite arrives in about 40ms and
        /// the light one in 20ms. That difference is worth keeping: a bigger jaw closes more slowly,
        /// and hearing that is part of why the two read as different animals.
        ///
        /// There is deliberately NO forced decay curve. An earlier version rode the clip down to
        /// -45dB, which fixed the envelope and broke the sound: it left the heavy bite with 90ms of
        /// audible content and the light one with 39ms, and 39ms of anything is a click, not a bite.
        /// A bark already decays on its own, and its own decay is the one that still sounds like a
        /// throat. How much of the bark is kept is set by the window length in the recipe; the only
        /// shaping here is a 25ms fade at the end so the tail does not stop on a step.
        /// </summary>
        private static void ShapePercussive(float[] samples)
        {
            if (samples.Length == 0) return;

            int attack = Mathf.Min(samples.Length, Mathf.Max(1, SampleRate / 500));
            for (int i = 0; i < attack; i++) samples[i] *= i / (float)attack;

            int fade = Mathf.Min(samples.Length, Mathf.Max(1, SampleRate / 40));
            for (int i = 0; i < fade; i++)
            {
                samples[samples.Length - 1 - i] *= i / (float)fade;
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

        /// <summary>16-bit mono PCM WAV. Unity imports these without any per-file setup.</summary>
        private static void WriteWav(string path, float[] samples)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? OutputFolder);

            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);

            int dataBytes = samples.Length * 2;

            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);              // PCM
            writer.Write((short)1);              // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);        // byte rate
            writer.Write((short)2);              // block align
            writer.Write((short)16);             // bits
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);

            foreach (float sample in samples)
            {
                writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }
        }
    }
}
