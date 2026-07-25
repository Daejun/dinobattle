using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Brings downloaded creature art into the project and reports what it found.
    ///
    /// Split deliberately into an inspect step and an apply step. The clip naming and rig layout of a
    /// third-party pack are not knowable up front, so guessing at them produces a broken Animator that
    /// only fails at runtime. Inspect first, read the real names, then apply.
    ///
    /// Expected layout — extract the downloaded pack here (the folder is gitignored):
    ///   .assets-cache/quaternius-dinosaurs/
    /// </summary>
    public static class CreatureArtImporter
    {
        private const string CacheFolder = ".assets-cache/quaternius-dinosaurs";
        private const string ModelFolder = "Assets/Art/Models";

        // ---------------------------------------------------------------- inspect (read-only)

        [MenuItem("Dino Battle/4. Inspect Creature Art", priority = 120)]
        public static void Inspect()
        {
            Debug.Log(BuildReport());
        }

        /// <summary>Read-only survey of the download cache and of anything already under Assets/Art/Models.</summary>
        public static string BuildReport()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== Creature art report ===");

            string cachePath = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName,
                CacheFolder.Replace('/', Path.DirectorySeparatorChar));

            report.AppendLine($"\n[cache] {cachePath}");
            if (!Directory.Exists(cachePath))
            {
                report.AppendLine("  NOT FOUND — download the pack and extract it there. See Docs/assets.md.");
            }
            else
            {
                var models = Directory
                    .EnumerateFiles(cachePath, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".blend", System.StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .ToList();

                report.AppendLine($"  {models.Count} model file(s):");
                foreach (var m in models)
                {
                    var info = new FileInfo(m);
                    report.AppendLine($"    {Path.GetRelativePath(cachePath, m),-60} {info.Length / 1024,6} KB");
                }
            }

            report.AppendLine($"\n[imported] {ModelFolder}");
            var imported = AssetDatabase
                .FindAssets("t:Model", new[] { ModelFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToList();

            if (imported.Count == 0)
            {
                report.AppendLine("  (nothing imported yet)");
            }

            foreach (var path in imported)
            {
                report.AppendLine($"\n  {path}");

                if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                {
                    report.AppendLine($"    rig={importer.animationType} importAnimation={importer.importAnimation} scale={importer.globalScale}");
                }

                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    // __preview__ clips are editor scratch objects, not real animation.
                    if (clip.name.StartsWith("__preview__")) continue;
                    report.AppendLine($"    clip: {clip.name,-32} {clip.length:0.00}s loop={clip.isLooping}");
                }

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root != null)
                {
                    var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    var meshes = root.GetComponentsInChildren<MeshRenderer>(true);
                    report.AppendLine($"    skinned={renderers.Length} static={meshes.Length} bounds={GetBounds(root).size}");
                }
            }

            report.AppendLine("\n[definitions] expected creature names");
            var roster = AssetDatabase.LoadAssetAtPath<Data.CreatureRoster>("Assets/GameData/Rosters/Roster_Default.asset");
            if (roster != null)
            {
                foreach (var d in roster.Creatures)
                {
                    if (d != null) report.AppendLine($"    {d.displayName,-16} prefab={(d.prefab != null ? d.prefab.name : "<none>")}");
                }
            }

            return report.ToString();
        }

        /// <summary>Copy only the .fbx files out of the cache. Importing .blend and .obj too triples the work.</summary>
        [MenuItem("Dino Battle/4b. Copy FBX From Cache", priority = 121)]
        public static void CopyFbxFromCache()
        {
            string cachePath = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName,
                CacheFolder.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(cachePath))
            {
                Debug.LogError($"[CreatureArtImporter] Cache folder not found: {cachePath}");
                return;
            }

            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder(ModelFolder);

            var copied = new List<string>();
            foreach (var source in Directory.EnumerateFiles(cachePath, "*.fbx", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(ModelFolder, Path.GetFileName(source));
                File.Copy(source, destination, overwrite: true);
                copied.Add(destination);
            }

            AssetDatabase.Refresh();

            if (copied.Count == 0) Debug.LogWarning($"[CreatureArtImporter] No .fbx found under {cachePath}");
            else Debug.Log($"[CreatureArtImporter] Copied {copied.Count} FBX into {ModelFolder}:\n  " + string.Join("\n  ", copied));
        }

        // ---------------------------------------------------------------- import settings + controllers

        private const string ControllerFolder = "Assets/Art/Animations";

        /// <summary>
        /// Action suffixes we look for. Clips are named "Armature|{Species}_{Action}", but the species
        /// prefix is unreliable — Trex.fbx uses "TRex_", and Apatosaurus.fbx ships a clip actually named
        /// "Stegosaurus_Death". Matching on the suffix alone sidesteps both problems.
        /// </summary>
        private static readonly string[] LoopingActions = { "_Idle", "_Walk", "_Run" };

        [MenuItem("Dino Battle/4c. Prepare Creature Animation", priority = 122)]
        public static void PrepareAnimation()
        {
            var models = AssetDatabase
                .FindAssets("t:Model", new[] { ModelFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToList();

            if (models.Count == 0)
            {
                Debug.LogError($"[CreatureArtImporter] No models under {ModelFolder}. Run '4b. Copy FBX From Cache' first.");
                return;
            }

            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder(ControllerFolder);

            int built = 0;
            foreach (var path in models)
            {
                ApplyLoopSettings(path);
                if (BuildController(path) != null) built++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CreatureArtImporter] Prepared {models.Count} model(s), built {built} Animator Controller(s) in {ControllerFolder}.");
        }

        /// <summary>
        /// The pack ships every clip with loop disabled, so Idle/Walk/Run play once and freeze. Loop
        /// flags live on the importer's clipAnimations, not on the clip asset, so they must be set here.
        /// </summary>
        private static void ApplyLoopSettings(string modelPath)
        {
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer) return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;

            // clipAnimations is empty until it is seeded from the file's own defaultClipAnimations.
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            bool changed = false;
            foreach (var clip in clips)
            {
                bool shouldLoop = LoopingActions.Any(a => clip.name.EndsWith(a, System.StringComparison.OrdinalIgnoreCase));
                if (clip.loopTime == shouldLoop) continue;

                clip.loopTime = shouldLoop;
                changed = true;
            }

            if (!changed) return;

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// One controller per model: a Speed-driven Idle/Walk/Run blend tree, plus Attack and Die
        /// triggers. Parameter names match what CreatureBrain and MeleeAttack already set.
        /// </summary>
        private static UnityEditor.Animations.AnimatorController BuildController(string modelPath)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToList();

            AnimationClip Find(string suffix) =>
                clips.FirstOrDefault(c => c.name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase));

            var idle = Find("_Idle");
            var walk = Find("_Walk");
            var run = Find("_Run");
            var attack = Find("_Attack");
            var death = Find("_Death");

            string species = Path.GetFileNameWithoutExtension(modelPath);

            if (idle == null)
            {
                Debug.LogWarning($"[CreatureArtImporter] {species}: no _Idle clip; skipping controller.");
                return null;
            }

            string controllerPath = $"{ControllerFolder}/AC_{species}.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;

            // Locomotion blend tree. CreatureBrain feeds Speed as currentSpeed / definition.moveSpeed,
            // so the thresholds are in normalized units: 0 = still, 1 = flat out.
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = UnityEditor.Animations.BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;

            // Walk starts early, at 0.15. A creature almost never travels at its full definition
            // speed: circling an enemy is capped at circleSpeedFactor, and separation and Arrive trim
            // it further, so measured Speed in a real fight sits around 0.2-0.3. With Walk at 0.45
            // that blended to mostly-Idle, and creatures slid across the ground with motionless feet.
            // Any genuine movement should read as walking.
            tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, 0.15f);
            if (run != null) tree.AddChild(run, 0.8f);

            machine.defaultState = locomotion;

            if (attack != null)
            {
                var attackState = machine.AddState("Attack");
                attackState.motion = attack;

                var toAttack = machine.AddAnyStateTransition(attackState);
                toAttack.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Attack");
                toAttack.duration = 0.05f;
                toAttack.hasExitTime = false;
                toAttack.canTransitionToSelf = false;

                var backToLocomotion = attackState.AddTransition(locomotion);
                backToLocomotion.hasExitTime = true;
                backToLocomotion.exitTime = 0.85f;
                backToLocomotion.duration = 0.15f;
            }

            if (death != null)
            {
                var deathState = machine.AddState("Death");
                deathState.motion = death;

                // No transition out: the corpse holds the last pose until it is despawned.
                var toDeath = machine.AddAnyStateTransition(deathState);
                toDeath.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Die");
                toDeath.duration = 0.05f;
                toDeath.hasExitTime = false;
                toDeath.canTransitionToSelf = false;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>World-space size of a model prefab, measured by instantiating it briefly.</summary>
        public static Vector3 MeasureModel(GameObject modelPrefab)
        {
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            try
            {
                temp.transform.position = Vector3.zero;
                temp.transform.rotation = Quaternion.identity;
                temp.transform.localScale = Vector3.one;
                return GetBounds(temp).size;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        private static Bounds GetBounds(GameObject root)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }
    }
}
