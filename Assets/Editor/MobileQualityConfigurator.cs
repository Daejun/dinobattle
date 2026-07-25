using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Quality settings sized for a phone and for THIS arena.
    ///
    /// The project was running on the Ultra preset: 150m of shadow distance across four cascades at
    /// high resolution, unlimited skin weights, realtime reflection probes. Those are desktop
    /// defaults that nobody chose — they are simply what a new 3D project starts with, and an
    /// Android title inherits them silently.
    ///
    /// Kept in code, and applied as part of the Android build, for the same reason the scene is:
    /// a fresh clone should behave identically without anyone remembering to tick boxes.
    /// </summary>
    public static class MobileQualityConfigurator
    {
        /// <summary>
        /// Shadows only need to cover the fight, not the horizon. The arena is 22 units in radius and
        /// the camera fits at most a 10-unit focus radius, so 60 covers everything the player can
        /// actually look at. At 150 the same shadow map was stretched over six times the area, which
        /// cost more AND gave blockier shadows on the creatures.
        /// </summary>
        private const float ShadowDistance = 60f;

        [MenuItem("Dino Battle/6. Apply Mobile Quality Settings", priority = 140)]
        public static void Apply()
        {
            // Hard shadows. Soft shadows are one of the most expensive things a mobile GPU can be
            // asked for, and against flat-shaded low-poly dinosaurs the difference is barely visible.
            QualitySettings.shadows = ShadowQuality.HardOnly;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = ShadowDistance;

            // Two cascades, not four. Cascades trade shadow-map renders for texel density over
            // distance; over 60 units there is not enough distance to justify four splits.
            QualitySettings.shadowCascades = 2;

            // The arena has exactly one light. Budgeting for four per-pixel lights just leaves the
            // renderer prepared for passes that never happen.
            QualitySettings.pixelLightCount = 1;

            // Four bones per vertex is the mobile standard and what the Quaternius rigs are built
            // for. Unlimited makes every skinned creature more expensive for no visible gain.
            QualitySettings.skinWeights = SkinWeights.FourBones;

            // Nothing in the scene is reflective and nothing uses soft particles.
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.softParticles = false;

            // MSAA stays on: tile-based mobile GPUs resolve it cheaply, and these models are all
            // hard silhouette edges, which is precisely the case where it earns its cost.
            QualitySettings.antiAliasing = 2;

            // No LOD groups exist, so a bias of 2 only inflated culling work.
            QualitySettings.lodBias = 1f;

            // Keep simulating when the window is not focused.
            //
            // This is a development concern, not a shipping one — Android suspends the app either
            // way. It matters because the editor is driven remotely here: with it off, play mode
            // freezes the instant focus moves elsewhere, and a tool querying the running game reads
            // a world that has not advanced a single frame since the last time someone clicked on
            // Unity. Measurements taken that way look plausible and are worthless.
            PlayerSettings.runInBackground = true;

            AssetDatabase.SaveAssets();

            Debug.Log($"[MobileQualityConfigurator] Applied to quality level " +
                      $"'{QualitySettings.names[QualitySettings.GetQualityLevel()]}': " +
                      $"shadows={QualitySettings.shadows} dist={QualitySettings.shadowDistance} " +
                      $"cascades={QualitySettings.shadowCascades} skinWeights={QualitySettings.skinWeights}");
        }
    }
}
