using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Headless Android build entry points so CI (and Claude Code) can build without opening the editor.
    ///
    /// APK:
    ///   Unity.exe -quit -batchmode -nographics -projectPath . -executeMethod DinoBattle.EditorTools.AndroidBuilder.BuildApk
    /// AAB (Play Store):
    ///   Unity.exe -quit -batchmode -nographics -projectPath . -executeMethod DinoBattle.EditorTools.AndroidBuilder.BuildAab
    ///
    /// See Tools/build-android.ps1 for a wrapper that fills in the editor path.
    /// </summary>
    public static class AndroidBuilder
    {
        private const string OutputDirectory = "Build/Android";

        [MenuItem("Dino Battle/3. Build Android APK", priority = 102)]
        public static void BuildApk() => Run(false);

        [MenuItem("Dino Battle/3. Build Android AAB (Play Store)", priority = 103)]
        public static void BuildAab() => Run(true);

        private static void Run(bool appBundle)
        {
            ApplyPlayerSettings(appBundle);

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new Exception(
                    "No enabled scenes in Build Settings. Run 'Dino Battle > 2. Build Battle Scene' first.");
            }

            System.IO.Directory.CreateDirectory(OutputDirectory);
            string extension = appBundle ? "aab" : "apk";
            string output = $"{OutputDirectory}/dino-battle-{PlayerSettings.bundleVersion}.{extension}";

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            Debug.Log($"[AndroidBuilder] Building {(appBundle ? "AAB" : "APK")} -> {output} " +
                      $"({scenes.Length} scene(s))");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"Android build {summary.result}: {summary.totalErrors} error(s).");
            }

            Debug.Log($"[AndroidBuilder] {summary.result} -> {output} " +
                      $"({summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:0}s)");
        }

        /// <summary>
        /// Settings the game needs on Android. Kept in code so a fresh clone builds the same way
        /// without anyone remembering to tick boxes in the Player Settings window.
        /// </summary>
        private static void ApplyPlayerSettings(bool appBundle)
        {
            PlayerSettings.companyName = "DinoBattle";
            PlayerSettings.productName = "Dino Battle";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.dinobattle.game");

            // Unity 6.5 raised the Android floor to API 26 (Android 8.0). Anything lower is rejected
            // by the editor, so do not "helpfully" lower this to widen device support.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // 64-bit only requires IL2CPP; Play Store rejects armv7-only uploads.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Master trades a much longer IL2CPP compile for the fastest runtime — worth it for a
            // store bundle, not for the APK you flash to a device twenty times a day.
            PlayerSettings.SetIl2CppCompilerConfiguration(
                NamedBuildTarget.Android,
                appBundle ? Il2CppCompilerConfiguration.Master : Il2CppCompilerConfiguration.Release);

            PlayerSettings.SetIl2CppCodeGeneration(
                NamedBuildTarget.Android,
                appBundle ? Il2CppCodeGeneration.OptimizeSpeed : Il2CppCodeGeneration.OptimizeSize);

            EditorUserBuildSettings.buildAppBundle = appBundle;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // NOT set here: "Link Time Optimization" (ThinLTO), new in Unity 6.5. It ships a
            // precompiled, link-time-optimized libunity.so for non-development builds and is worth
            // roughly 5% on startup and frame time. Enable it by hand in
            //   Project Settings > Player > Android > Publishing Settings
            // It is deliberately left as a manual toggle rather than guessed at through the
            // PlayerSettings API — see Docs/setup.md.
        }
    }
}
