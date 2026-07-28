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
        private const string CacheRoot = ".assets-cache";
        private const string ModelFolder = "Assets/Art/Models";

        /// <summary>
        /// Which cached download folder lands where under <see cref="ModelFolder"/>.
        ///
        /// An explicit table rather than a flat sweep, because the destination is not uniform:
        /// creatures go to the root, where the animation step looks for them, and scenery goes to
        /// Nature. Sweeping everything into one folder would import several hundred palm trees
        /// alongside the dinosaurs, twice.
        ///
        /// It is a table rather than the single hardcoded path it used to be because that single
        /// path is how the dragon boss came to be copied in by hand — and an asset copied in by hand
        /// is an asset whose origin nobody wrote down, which is exactly what happened. Everything the
        /// game ships should be reproducible by running this menu item against the cache.
        /// </summary>
        private static readonly (string Cache, string Destination)[] CacheMap =
        {
            ("quaternius-dinosaurs", ModelFolder),
            ("monster",              ModelFolder),
            ("easyenemy",            ModelFolder),

            // Quaternius Ultimate Monsters, CC0. Only the eight rigs that have a ground walk cycle —
            // the pack's other half are flyers whose locomotion clip is a wing beat, and this game
            // drives a Rigidbody along the floor, so those would slide.
            ("quaternius-monsters-ground", ModelFolder),
            ("nature",               ModelFolder + "/Nature"),
        };

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
                CacheRoot.Replace('/', Path.DirectorySeparatorChar));

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
            string root = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName,
                CacheRoot.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(root))
            {
                Debug.LogError($"[CreatureArtImporter] Cache folder not found: {root}");
                return;
            }

            SampleContentBuilder.EnsureFolder("Assets/Art");

            var copied = new List<string>();
            var absent = new List<string>();

            foreach (var (cache, destinationFolder) in CacheMap)
            {
                string sourceFolder = Path.Combine(root, cache);
                if (!Directory.Exists(sourceFolder)) { absent.Add(cache); continue; }

                SampleContentBuilder.EnsureFolder(destinationFolder);

                foreach (var source in Directory.EnumerateFiles(sourceFolder, "*.fbx", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(destinationFolder, Path.GetFileName(source));
                    File.Copy(source, destination, overwrite: true);
                    copied.Add(destination);
                }
            }

            AssetDatabase.Refresh();

            // Not an error. The cache is gitignored, so a fresh clone has none of these until the
            // fetch scripts have run, and copying whichever ones are present is the useful behaviour.
            if (absent.Count > 0)
                Debug.Log($"[CreatureArtImporter] Not in the cache yet: {string.Join(", ", absent)}");

            if (copied.Count == 0) Debug.LogWarning($"[CreatureArtImporter] No .fbx found under {root}");
            else Debug.Log($"[CreatureArtImporter] Copied {copied.Count} FBX:\n  " + string.Join("\n  ", copied));
        }

        // ---------------------------------------------------------------- import settings + controllers

        private const string ControllerFolder = "Assets/Art/Animations";

        /// <summary>
        /// Action suffixes we look for. Clips are named "Armature|{Species}_{Action}", but the species
        /// prefix is unreliable — Trex.fbx uses "TRex_", and Apatosaurus.fbx ships a clip actually named
        /// "Stegosaurus_Death". Matching on the suffix alone sidesteps both problems.
        /// </summary>
        /// <summary>
        /// _Flying is here because the boss dragon has no idle: its locomotion clip is the wing beat,
        /// and a wing beat that plays once and freezes leaves the creature hanging mid-flap.
        /// </summary>
        private static readonly string[] LoopingActions = { "_Idle", "_Walk", "_Run", "_Flying" };

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

            // Match on the action alone, after dropping the armature prefix.
            //
            // Three naming conventions are in play and none of them agree. The dinosaurs ship
            // "Armature|TRex_Idle", the boss dragon "DragonArmature|Dragon_Flying", and the monster
            // pack "CharacterArmature|Idle" — no species token at all, so the separator before the
            // action is a bar rather than an underscore. A plain EndsWith("_Idle") silently found
            // nothing on the whole monster pack, and a creature with no idle clip is skipped
            // entirely, so the failure surfaced as monsters with no controller rather than as
            // anything pointing at naming.
            //
            // Exact first, then underscore-separated, then contained. The order matters: BlueDemon
            // has both "Idle" and "Jump_Idle", and a bare Contains would happily return the latter.
            static string Tail(string clipName)
            {
                int bar = clipName.LastIndexOf('|');
                return bar >= 0 ? clipName[(bar + 1)..] : clipName;
            }

            AnimationClip Find(string action)
            {
                string want = action.TrimStart('_');
                var cmp = System.StringComparison.OrdinalIgnoreCase;

                return clips.FirstOrDefault(c => Tail(c.name).Equals(want, cmp))
                    ?? clips.FirstOrDefault(c => Tail(c.name).EndsWith("_" + want, cmp))
                    ?? clips.FirstOrDefault(c => Tail(c.name).Contains(want, cmp));
            }

            // Fall back through alternatives per state rather than requiring the dinosaur pack's exact
            // naming. The boss dragon comes from a different pack and has no Idle, Walk or Run at all
            // — it has Flying, because it is a flying creature. Insisting on the dinosaur vocabulary
            // would have meant either no boss or a boss with no animation, when the honest mapping is
            // that a hovering dragon's idle IS its flap.
            AnimationClip FindAny(params string[] suffixes)
            {
                foreach (string suffix in suffixes)
                {
                    var found = Find(suffix);
                    if (found != null) return found;
                }

                return null;
            }

            var idle = FindAny("_Idle", "_Flying", "_Hover");
            var walk = FindAny("_Walk", "_Flying");
            var run = FindAny("_Run", "_Flying");
            // Bite and Punch before Hit, and this order is load-bearing.
            //
            // The monster pack names its attacks "Bite_Front" and "Punch", neither of which contains
            // "Attack", so the old list fell through to "_Hit" — and in that pack "HitRecieve" and
            // "HitReact" are the FLINCH, the animation for being hit rather than hitting. Every one
            // of the eight monsters was built with a controller that played a recoil when it bit.
            //
            // "_Hit" stays, last, because the boss dragon's attack clip is genuinely "Dragon_Hit".
            var attack = FindAny("_Attack", "Bite", "Punch", "Headbutt", "_Hit");
            var death = FindAny("_Death", "_Die");

            string species = Path.GetFileNameWithoutExtension(modelPath);

            if (idle == null)
            {
                Debug.LogWarning($"[CreatureArtImporter] {species}: no idle-like clip; skipping controller.");
                return null;
            }

            string controllerPath = $"{ControllerFolder}/AC_{species}.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            // A bool, not a trigger. A trigger is consumed by whichever transition takes it and is
            // easy to lose if the state machine happens to be mid-transition when it is raised; a
            // bool simply stays true, so the corpse gets to Death whatever else was going on.
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

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

                // AnyState means ANY state, Death included. Without this guard a swing raised in the
                // same frame the creature died pulled the corpse straight back out of Death, and the
                // Attack state exits to Locomotion — leaving a dead dinosaur standing there idling.
                // Measured after a 3v3: one corpse in three was upright, playing TRex_Idle.
                toAttack.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0f, "Dead");
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
                toDeath.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Dead");
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
