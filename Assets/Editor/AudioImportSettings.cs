using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Import rules for audio, so the load type is a property of the folder rather than of whoever
    /// happened to drop the file in.
    ///
    /// This exists because of a measured mistake. Unity's default load type is DecompressOnLoad,
    /// which expands a clip to raw PCM in memory the first time it plays. That is the right default
    /// for a bite or a roar and completely wrong for a soundtrack: the two music tracks that ship
    /// were 138s and 96s of stereo, which is 26.5 MB and 16.9 MB of PCM — 43 MB resident, on a build
    /// whose entire APK is 25 MB. Nothing in the game had ever set the load type, and a comment in
    /// BattleMusic claimed the problem was handled when no code anywhere touched it.
    ///
    /// Split by folder rather than by length, because the two directories already mean the two
    /// different things:
    ///
    ///   Audio/Music  — long, at most two playing at once (the crossfade), never latency-critical.
    ///                  Streamed: memory drops to a small ring buffer per source. The cost is that
    ///                  Play() is not instantaneous, which does not matter for a track that fades in
    ///                  from silence anyway.
    ///   Audio/SFX    — short, dozens firing at once, must be sample-accurate on the frame a jaw
    ///                  closes. Left decompressed: streaming a 200 ms bite would add latency to
    ///                  exactly the sound that needs none, to save kilobytes.
    ///
    /// Tools/check-project.sh verifies the committed .meta files still agree with this, because an
    /// AssetPostprocessor only runs on import — an asset already in the project keeps whatever
    /// settings it was imported with, and this class would never fire on it.
    /// </summary>
    public class AudioImportSettings : AssetPostprocessor
    {
        private const string MusicFolder = "Assets/Audio/Music/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(MusicFolder)) return;
            if (assetImporter is not AudioImporter importer) return;

            var settings = importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.Streaming && importer.loadInBackground) return;

            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;

            // Decoding the first buffer on the main thread stalls it. Music is never needed on the
            // frame it is asked for, so let it arrive late rather than hitch the fight.
            importer.loadInBackground = true;

            Debug.Log($"[AudioImportSettings] {assetPath} imported as streaming music.");
        }
    }
}
