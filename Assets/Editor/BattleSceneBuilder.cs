using System.Collections.Generic;
using DinoBattle.CameraRig;
using DinoBattle.Core;
using DinoBattle.Data;
using DinoBattle.Placement;
using DinoBattle.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Builds the playable arena scene from code: ground, lighting, camera rig, managers and a
    /// working HUD. Hand-editing a .unity file is a bad time, so the scene is generated instead —
    /// re-run this after changing the layout rather than fixing the scene by hand.
    ///
    /// Menu: Dino Battle > 2. Build Battle Scene
    /// </summary>
    public static class BattleSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        /// <summary>
        /// Radius of the circular playable area.
        ///
        /// Circular rather than square: with a square, creatures pushed into a corner had two walls
        /// to slide along and could sit there indefinitely. A ring has no corners to hide in, and
        /// every direction from the centre is the same distance to the edge.
        ///
        /// Sized against the creatures — a T-Rex is 5 units long, so a 22-unit radius is about nine
        /// body lengths across. Small enough that two sides meet within a few seconds instead of
        /// spending the opening of every match walking toward each other.
        /// </summary>
        private const float ArenaRadius = 22f;

        /// <summary>Full width of the playable area, kept for the places that want a diameter.</summary>
        private const float ArenaSize = ArenaRadius * 2f;

        /// <summary>Ground plane size as a multiple of the arena, so scenery has land to stand on.</summary>
        private const float GroundExtent = 4f;

        /// <summary>CC0 vegetation used to dress the arena. See ATTRIBUTIONS.md.</summary>
        private const string NatureFolder = "Assets/Art/Models/Nature";

        [MenuItem("Dino Battle/2. Build Battle Scene", priority = 101)]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Battle Scene",
                    $"This creates or overwrites {ScenePath}.\n\nAny hand edits to that scene will be lost. Continue?",
                    "Build", "Cancel"))
            {
                return;
            }

            BuildNoPrompt();
        }

        /// <summary>
        /// The build itself, with no confirmation dialog.
        ///
        /// Separate from <see cref="Build"/> because a modal dialog deadlocks any non-interactive
        /// caller: batch mode, CI, or an agent driving the editor over MCP all hang on it until a
        /// human clicks. The prompt still guards the interactive menu path, where it is useful.
        /// </summary>
        [MenuItem("Dino Battle/Advanced/Build Battle Scene (no prompt)", priority = 200)]
        public static void BuildNoPrompt()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateEnvironment();
            var gauntletArena = CreateGauntletArena();
            var cameraRig = CreateCamera();
            var (manager, placement, autoPlacer) = CreateManagers(cameraRig);
            CreateGauntletDirector(manager, gauntletArena, cameraRig);
            CreateHud(manager, placement, autoPlacer);
            CreateAudio(manager);

            WarnAboutObstructions();

            SampleContentBuilder.EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddSceneToBuildSettings();

            Debug.Log($"[BattleSceneBuilder] Built {ScenePath}. Press Play, tap the ground to place, then Start Battle.");
        }

        /// <summary>
        /// Background music and the victory celebration.
        ///
        /// Both hang off their own object rather than the camera: the camera is where listening
        /// happens, and a 2D music source parented to a rig that orbits and zooms is asking for one
        /// of them to start panning the soundtrack the first time someone changes the audio settings.
        /// </summary>
        private static void CreateAudio(BattleManager manager)
        {
            var host = new GameObject("Audio");

            var music = host.AddComponent<BattleMusic>();
            var musicSerialized = new SerializedObject(music);
            // The owner's own track, used for both the setup screen and the win. Renamed from the
            // Korean filename it arrived with: the Android build pipeline and the LFS pointer both
            // handle non-ASCII asset paths poorly enough that it is not worth finding out where.
            musicSerialized.FindProperty("placementTrack").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Music/music_tyranno.mp3");
            musicSerialized.FindProperty("battleTrack").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Music/music_battle.mp3");
            musicSerialized.FindProperty("victoryTrack").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Music/music_tyranno.mp3");
            musicSerialized.ApplyModifiedPropertiesWithoutUndo();

            var celebration = host.AddComponent<VictoryCelebration>();
            var celebrationSerialized = new SerializedObject(celebration);
            celebrationSerialized.FindProperty("fanfare").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/sfx_victory.wav");
            celebrationSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- environment

        private static void CreateEnvironment()
        {
            // Extends well past the playable arena. Sized to ArenaSize the plane ended right where the
            // hills begin, so the horizon dressing floated over open space with a hard edge under it.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = Vector3.one * (ArenaSize * GroundExtent / 10f);
            Tint(ground, new Color(0.19f, 0.22f, 0.14f));

            CreateCircularBoundary();

            var sun = new GameObject("Directional Light");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            // Hard, not soft. Soft shadows are among the most expensive things a mobile GPU can be
            // asked for, and against flat-shaded low-poly models the difference barely registers.
            light.shadows = LightShadows.Hard;
            // Warm and faintly green — daylight that has come through a canopy, not open sky.
            light.color = new Color(1f, 0.97f, 0.82f);
            sun.transform.rotation = Quaternion.Euler(48f, 34f, 0f);

            // A code-built scene has no skybox, and Unity's default ambient source IS the skybox — so
            // ambient light lands at roughly zero and every surface facing away from the sun renders
            // black. The creatures looked like silhouettes for exactly this reason. An explicit
            // trilight gradient restores the fill light a skybox would normally provide.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.58f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.42f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.23f, 0.17f);
            RenderSettings.ambientIntensity = 1f;

            // Distance fog hides the hard edge where the ground plane stops, which otherwise reads as
            // the world simply ending a short walk away.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // Humid green haze. A grey-blue fog reads as an overcast field; the colour of the air is
            // most of what separates "jungle" from "somewhere with trees on it".
            RenderSettings.fogColor = new Color(0.58f, 0.66f, 0.56f);
            RenderSettings.fogStartDistance = ArenaSize * 0.9f;
            RenderSettings.fogEndDistance = ArenaSize * 2.6f;

            CreateTerrainDressing();
            RenderSettings.ambientIntensity = 1f;

            MarkSceneryStatic();
            StripSceneryShadowCasting();
        }

        /// <summary>
        /// Fail loudly if anything solid ended up inside the ring.
        ///
        /// This exists because the bug it catches was invisible: scenery colliders drifted inside the
        /// play area, creatures wedged against them, and the only symptom was that fights in one part
        /// of the arena quietly never resolved. Nothing in the console, nothing in check-project.sh —
        /// static analysis cannot see world-space geometry. So the builder measures it.
        /// </summary>
        private static void WarnAboutObstructions()
        {
            var offenders = new System.Collections.Generic.List<string>();

            // No sort mode: Unity 6.5 deprecated the overload that takes one.
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
            {
                // The ground is meant to be underfoot, and the boundary is meant to be at the edge.
                if (collider.gameObject.name == "Ground") continue;
                if (collider.gameObject.name.StartsWith("Boundary_")) continue;

                Vector3 p = collider.transform.position;
                float radius = new Vector2(p.x, p.z).magnitude;

                if (radius < ArenaRadius) offenders.Add($"{collider.gameObject.name} at r={radius:0.0}");
            }

            if (offenders.Count == 0) return;

            Debug.LogError(
                $"[BattleSceneBuilder] {offenders.Count} collider(s) sit inside the {ArenaRadius}-unit " +
                "play area. The steering has no obstacle avoidance, so creatures will wedge against " +
                "them and fights in that spot will stall with no health lost:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Ring of flat wall segments approximating a circle.
        ///
        /// Unity has no inside-out cylinder collider, so the boundary is a polygon of thin boxes,
        /// each rotated to face the centre. Enough segments that a creature sliding along it feels a
        /// curve rather than a series of flats.
        ///
        /// A low kerb is added at ground level as well: knockback occasionally lofted a light
        /// creature over a wall whose base sat above the ground.
        /// </summary>
        private static void CreateCircularBoundary()
        {
            const int segments = 28;
            var root = new GameObject("Boundary").transform;

            // Overlap each segment slightly so the seams between them cannot be squeezed through.
            float segmentWidth = 2f * Mathf.PI * ArenaRadius / segments * 1.25f;

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var wall = new GameObject($"Boundary_{i}");
                wall.transform.SetParent(root, false);

                var collider = wall.AddComponent<BoxCollider>();
                collider.size = new Vector3(segmentWidth, 40f, 1.5f);

                Vector3 outward = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                // Centred at y=0 with 40 units of height, so it reaches well below ground too.
                wall.transform.SetPositionAndRotation(
                    outward * ArenaRadius,
                    Quaternion.LookRotation(outward, Vector3.up));
            }
        }

        /// <summary>
        /// Rocks, boulders and a ring of hills, placed procedurally.
        ///
        /// Deterministic: the random stream is seeded, so rebuilding the scene reproduces the same
        /// arena rather than reshuffling it under a diff.
        ///
        /// EVERYTHING solid lives outside <see cref="ArenaRadius"/>. The steering has no obstacle
        /// avoidance, so a collider inside the ring is something creatures walk into and stall
        /// against — an earlier pass put 26 boulders at 0.82-0.97 of the radius, all with colliders,
        /// and fights on that side of the arena simply stopped: attackers wedged on rock, never
        /// reached anyone, and no health drained. Only flat, collider-less ground decals go inside.
        /// </summary>
        /// <summary>
        /// Flag the scenery for static batching.
        ///
        /// Ground, boundary and dressing add up to nearly ninety renderers that never move, and every
        /// one of them was being submitted individually — the scene had zero static geometry, so the
        /// batcher had nothing to work with. They are combined into shared meshes at build time
        /// instead. Creatures are excluded for the obvious reason.
        ///
        /// BatchingStatic only. ContributeGI would drag in lightmap baking, which this project does
        /// not do, and OccluderStatic needs an occlusion bake that nothing here would benefit from.
        /// </summary>
        private static void MarkSceneryStatic()
        {
            foreach (string name in new[] { "Ground", "Boundary", "Environment" })
            {
                var root = GameObject.Find(name);
                if (root == null) continue;

                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObjectUtility.SetStaticEditorFlags(
                        child.gameObject,
                        StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                }
            }
        }

        /// <summary>
        /// Stop the scenery casting shadows. It still receives them.
        ///
        /// Measured in a live battle: 280 shadow casters, of which ten were dinosaurs. The other
        /// two hundred and seventy were palms, bushes, boulders, hills, floor tufts and stones —
        /// every one of them submitted to the shadow map as a second full geometry pass, on a phone,
        /// so that a shrub outside the boundary wall could darken a patch of ground nobody is
        /// looking at.
        ///
        /// Casting is what costs; receiving is nearly free and is what actually reads. A creature's
        /// shadow is worth paying for because it is what plants the animal on the ground and sells
        /// its size — so creatures are untouched, and they still land their shadows on all of this.
        /// The scenery's own shadows contribute nothing the player would miss.
        ///
        /// Deliberately not done by giving the props a shader with no caster pass: which objects
        /// cast is a property of this scene, not of the material, and the next arena may want its
        /// obstacles casting.
        /// </summary>
        private static void StripSceneryShadowCasting()
        {
            int stripped = 0;

            foreach (string name in new[] { "Ground", "Boundary", "Environment" })
            {
                var root = GameObject.Find(name);
                if (root == null) continue;

                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    // The ground is the surface every creature shadow lands on, so it must keep
                    // receiving even though it has nothing worth casting.
                    renderer.receiveShadows = true;
                    stripped++;
                }
            }

            Debug.Log($"[BattleSceneBuilder] {stripped} scenery renderer(s) no longer cast shadows.");
        }

        private const string EnvironmentShader = "DinoBattle/EnvironmentFlat";

        // ---------------------------------------------------------------- gauntlet board
        //
        // Every constant here is a measurement, not a preference. RampClimbProbe walked the shipping
        // locomotion up this geometry before any of it was built; the numbers and the reasoning are
        // in Docs/gauntlet-step1-ramp-probe.md section 14.

        /// <summary>
        /// Ramp angle. The probe cleared 8-25 degrees at 75/75 arrivals with zero backslide, so this
        /// is not the limit — it is the limit minus margin. Above 15 the fastest creature starts
        /// launching off crests, and a launched creature has no steering authority at all.
        /// </summary>
        private const float GauntletRampAngle = 12f;

        private const int GauntletTiers = 10;
        private const float GauntletTierRise = 2f;
        private const float GauntletPlatformDepth = 22f;
        private const float GauntletWidth = 26f;

        /// <summary>
        /// Hard ceiling on any vertical lip anywhere on the board.
        ///
        /// Measured, per species, and it tracks collider radius: the Velociraptor climbs 0.1 and
        /// fails at 0.2. The parent design guessed 0.3 — twice too generous. Anything on this board
        /// with a taller step than this stops a raptor dead, and the symptom would be "the fights on
        /// tier six are weird" rather than anything pointing at the geometry.
        /// </summary>
        private const float GauntletMaxLedge = 0.15f;

        /// <summary>Far from the versus arena, which covers +/-88 on the ground plane.</summary>
        private const float GauntletOriginX = 600f;

        private static readonly string[] RockModels = { "Rock_1", "Rock_2", "Rock_3", "Rock_4", "Rock_5" };
        private static readonly string[] PalmModels = { "PalmTree_1", "PalmTree_2", "PalmTree_3", "PalmTree_4", "PalmTree_5" };
        private static readonly string[] BushModels = { "Bush", "Bush_Large", "Bush_Small" };
        private static readonly string[] GroundModels = { "Grass_Large", "Grass_Small", "Plant_1", "Plant_2" };

        /// <summary>
        /// Dress the arena as a jungle clearing.
        ///
        /// The old dressing was tinted primitives — spheres for hills, cubes for rocks — and it read
        /// as exactly that. What makes somewhere feel like jungle is not detail on the floor but
        /// enclosure: a wall of canopy on every side, so the arena is a clearing that something cut
        /// out of a forest rather than an open field with objects on it.
        ///
        /// Hence the layout. A dense double ring of palms just past the boundary forms the wall,
        /// undergrowth banks up in front of it to hide the join between trunk and ground, and only
        /// small flat things go inside the ring.
        ///
        /// Nothing here gets a collider, inside the ring or out. The steering has no obstacle
        /// avoidance, and the arena's hard-won rule is that a creature can never meet anything solid
        /// except the boundary wall.
        /// </summary>
        private static void CreateJungle(Transform root, System.Random random, System.Func<float, float, float> Range)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureFolder}/PalmTree_1.fbx") == null)
            {
                Debug.LogWarning($"[BattleSceneBuilder] No vegetation in {NatureFolder}; " +
                                 "the arena will be bare. See ATTRIBUTIONS.md for the source models.");
                return;
            }

            // Canopy wall. Two staggered rings rather than one: a single ring of trunks has gaps you
            // can see straight through, and the horizon showing between them undoes the enclosure the
            // trees are there to create.
            const int palmCount = 56;
            for (int i = 0; i < palmCount; i++)
            {
                float angle = i / (float)palmCount * Mathf.PI * 2f + Range(-0.04f, 0.04f);
                // Both rings clear the camera. The placement view orbits out to about 28 units
                // horizontally, and at 1.16/1.40 the rings landed at 25.5 and 30.8 — putting the
                // camera between them, so half the screen was foreground fronds and the fight was
                // behind a tree. Everything the player looks through has to sit outside that.
                float ring = i % 2 == 0 ? 1.50f : 1.80f;
                float radius = ArenaRadius * ring * Range(0.97f, 1.05f);

                var palm = PlaceModel(root, PalmModels[random.Next(PalmModels.Length)], $"Palm_{i}",
                    new Vector3(Mathf.Cos(angle) * radius, -0.2f, Mathf.Sin(angle) * radius),
                    Range(0f, 360f), Range(1.5f, 2.6f));

                if (palm == null) continue;

                // Leaves vary a lot, trunks barely at all. Real canopy is a patchwork of greens
                // because every crown catches a different amount of light, while the trunks below are
                // all in the same shade.
                PaintModel(palm,
                    Color.Lerp(new Color(0.13f, 0.30f, 0.14f), new Color(0.28f, 0.46f, 0.19f), (float)random.NextDouble()),
                    Color.Lerp(new Color(0.24f, 0.19f, 0.14f), new Color(0.32f, 0.26f, 0.19f), (float)random.NextDouble()));
            }

            // Undergrowth banked against the treeline, covering where the trunks meet the ground.
            const int bushCount = 60;
            for (int i = 0; i < bushCount; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = ArenaRadius * Range(1.38f, 1.75f);

                var bush = PlaceModel(root, BushModels[random.Next(BushModels.Length)], $"Bush_{i}",
                    new Vector3(Mathf.Cos(angle) * radius, -0.1f, Mathf.Sin(angle) * radius),
                    Range(0f, 360f), Range(1.4f, 3.2f));

                if (bush == null) continue;

                Color leaf = Color.Lerp(new Color(0.11f, 0.26f, 0.13f), new Color(0.24f, 0.40f, 0.17f), (float)random.NextDouble());
                PaintModel(bush, leaf, leaf);
            }

            // Inside the ring: only low ground cover, and none of it near the middle where the fight
            // happens. Anything tall enough to hide a creature defeats the point of watching.
            const int groundCount = 44;
            for (int i = 0; i < groundCount; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = ArenaRadius * Range(0.45f, 0.97f);

                var plant = PlaceModel(root, GroundModels[random.Next(GroundModels.Length)], $"Undergrowth_{i}",
                    new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius),
                    Range(0f, 360f), Range(0.8f, 1.6f));

                if (plant == null) continue;

                Color leaf = Color.Lerp(new Color(0.16f, 0.31f, 0.15f), new Color(0.30f, 0.44f, 0.20f), (float)random.NextDouble());
                PaintModel(plant, leaf, leaf);
            }
        }

        /// <summary>
        /// Drop a nature model into the scene, stripped of anything that could interfere with play.
        /// Returns null when the model is missing, so a clone without the art degrades to a bare
        /// arena instead of throwing.
        /// </summary>
        private static GameObject PlaceModel(
            Transform parent, string modelName, string instanceName, Vector3 position, float yaw, float scale)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureFolder}/{modelName}.fbx");
            if (source == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = instanceName;
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one * scale;

            // Belt and braces: FBX import does not generate colliders by default, but a single
            // stray collider inside the ring is the bug that made fights silently stall, and it is
            // not worth depending on an import setting nobody will remember to check.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            return instance;
        }

        /// <summary>
        /// Flat-colour a nature model, splitting foliage from wood by material name.
        ///
        /// The source pack is textured, and its textures are 20MB bark maps we deliberately did not
        /// download — so the materials arrive with nothing bound and render white. Painting them flat
        /// is not a workaround for that, it is the point: the creatures are flat-shaded, and scenery
        /// carrying photographic bark next to them would look like two games spliced together.
        /// </summary>
        private static void PaintModel(GameObject target, Color foliage, Color wood)
        {
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var painted = new Material[materials.Length];

                for (int i = 0; i < materials.Length; i++)
                {
                    string name = materials[i] != null ? materials[i].name : string.Empty;

                    bool isWood = name.Contains("Trunk") || name.Contains("Bark") || name.Contains("Wood");
                    painted[i] = SharedEnvironmentMaterial(isWood ? wood : foliage);
                }

                renderer.sharedMaterials = painted;
            }
        }

        /// <summary>
        /// The gauntlet board: a start platform and ten tiers climbing away from it, joined by ramps.
        ///
        /// Built into the same scene as the round arena and switched off. Two arenas resident beats
        /// loading a second scene — an inactive renderer costs nothing at runtime, and it keeps both
        /// layouts inside one generated artefact that a diff can review.
        ///
        /// The steps are RAMPS, not steps, and that is the whole reason step 1 existed.
        /// CreatureLocomotion propels on X and Z only; its steering force has no vertical component
        /// and it stops steering altogether when its ground probe misses. A horizontal push is
        /// redirected up an incline by the surface normal, so a ramp works — but at a vertical face
        /// there is nothing to redirect, and creatures simply stop. The probe measured where that
        /// starts to bite (GauntletMaxLedge) and how steep a ramp can be before the fast ones launch
        /// off the top (GauntletRampAngle).
        ///
        /// So: no kerbs, no lips, no decorative rocks on the walking surface. Every joint here is
        /// flush by construction, and the walls are the only vertical geometry.
        /// </summary>
        private static GauntletArena CreateGauntletArena()
        {
            var root = new GameObject("GauntletArena");
            root.transform.position = new Vector3(GauntletOriginX, 0f, 0f);

            var arena = root.AddComponent<GauntletArena>();
            var tiers = new List<GauntletTier>();

            float run = GauntletTierRise / Mathf.Tan(GauntletRampAngle * Mathf.Deg2Rad);
            float z = 0f;
            float y = 0f;

            // Somewhere to muster that is unambiguously not tier one, so the first wave has a moment
            // of walking before anything happens to it.
            var start = AddBoardSlab(root.transform, "StartPlatform",
                new Vector3(0f, -0.5f, z + GauntletPlatformDepth * 0.5f),
                new Vector3(GauntletWidth, 1f, GauntletPlatformDepth));
            z += GauntletPlatformDepth;

            for (int i = 0; i < GauntletTiers; i++)
            {
                var ramp = AddBoardSlab(root.transform, $"Ramp_{i:00}",
                    new Vector3(0f, y + GauntletTierRise * 0.5f - 0.5f * Mathf.Cos(GauntletRampAngle * Mathf.Deg2Rad),
                                z + run * 0.5f),
                    new Vector3(GauntletWidth, 1f, Mathf.Sqrt(run * run + GauntletTierRise * GauntletTierRise)));
                ramp.transform.localRotation = Quaternion.Euler(-GauntletRampAngle, 0f, 0f);

                z += run;
                y += GauntletTierRise;

                var platform = AddBoardSlab(root.transform, $"Tier_{i:00}",
                    new Vector3(0f, y - 0.5f, z + GauntletPlatformDepth * 0.5f),
                    new Vector3(GauntletWidth, 1f, GauntletPlatformDepth));

                var tier = platform.AddComponent<GauntletTier>();

                // The objective sits a third of the way onto the platform, not at its centre: the
                // wave should step clear of the ramp mouth and stop, rather than walking into the
                // middle of the monsters it is about to meet.
                var objective = new GameObject("Objective").transform;
                objective.SetParent(platform.transform, false);
                objective.position = new Vector3(GauntletOriginX, y, z + GauntletPlatformDepth * 0.3f);

                // Monsters wait at the far end, spread across the width, facing back down the board.
                var points = new List<Transform>();
                for (int n = 0; n < 12; n++)
                {
                    var point = new GameObject($"Spawn_{n:00}").transform;
                    point.SetParent(platform.transform, false);

                    int row = n / 4;
                    int column = n % 4;
                    point.position = new Vector3(
                        GauntletOriginX + (column - 1.5f) * (GauntletWidth * 0.22f),
                        y,
                        z + GauntletPlatformDepth * (0.62f + row * 0.14f));
                    points.Add(point);
                }

                AddTierNumber(platform.transform, i + 1, y, z);

                tier.Configure(objective, points);
                tiers.Add(tier);

                z += GauntletPlatformDepth;
            }

            AddBoardWalls(root.transform, z, y);
            AddSea(root.transform, z);

            // Static flags and shadow casting are applied HERE rather than by MarkSceneryStatic and
            // StripSceneryShadowCasting, which sweep the scene by name via GameObject.Find. Find does
            // not return inactive objects, and this board is deactivated on the next line — so both
            // sweeps would silently skip it and the only symptom would be thirty extra shadow casters
            // appearing the first time anyone selected the mode.
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }

            arena.Configure(tiers, start.transform);
            root.SetActive(false);

            Debug.Log($"[BattleSceneBuilder] Gauntlet board: {GauntletTiers} tiers at {GauntletRampAngle}deg, " +
                      $"{z:0} units long, rising {y:0}. Max permitted ledge {GauntletMaxLedge}.");
            return arena;
        }

        /// <summary>
        /// Walls down both sides, so a creature shoved sideways on a ramp cannot leave the board.
        ///
        /// Shoving is real and measured — CreatureImpact throws creatures by mass ratio — and unlike
        /// the round arena there is nowhere safe to land here. Placed OUTSIDE the walking surface and
        /// tall enough to cover the climb, with no lip where they meet the deck.
        /// </summary>
        private static void AddBoardWalls(Transform parent, float length, float rise)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var wall = AddBoardSlab(parent, side < 0 ? "Wall_Left" : "Wall_Right",
                    new Vector3(side * (GauntletWidth * 0.5f + 0.5f), rise * 0.5f + 2f, length * 0.5f),
                    new Vector3(1f, rise + 8f, length));

                // Invisible: a wall this tall alongside the whole board would box the shot in and
                // there is nothing to see on it. It only has to stop things.
                var renderer = wall.GetComponent<Renderer>();
                if (renderer != null) Object.DestroyImmediate(renderer);
            }
        }

        /// <summary>
        /// Paint the tier's number onto its floor.
        ///
        /// Asked for directly, and it is the better place for it: the HUD had "4층 / 10" in a strip
        /// over the arena, which is a number about the world sitting on top of the world. On the deck
        /// it is diegetic — the player reads how high they are by looking at where they are standing,
        /// and it costs no screen space at all.
        ///
        /// A 3D TextMesh rather than a generated texture. It is unlit, it needs no material plumbing,
        /// and it stays crisp at the distance the camera actually sits — a decal would have to be
        /// authored at a resolution guessed from that distance.
        ///
        /// Laid flat and turned to face back down the board, so it is upright from the direction the
        /// player is always climbing from.
        /// </summary>
        private static void AddTierNumber(Transform platform, int number, float height, float z)
        {
            var label = new GameObject($"Number_{number:00}");
            label.transform.SetParent(platform, false);

            // Just above the deck: coplanar with it would z-fight along the whole platform.
            label.transform.position = new Vector3(GauntletOriginX, height + 0.02f, z + GauntletPlatformDepth * 0.5f);
            // Flat on the deck, reading toward the top of the board. The first version was turned
            // the other way and came out upside down from the only direction anyone approaches from.
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var text = label.AddComponent<TextMesh>();
            text.text = number.ToString();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;

            // Large font size scaled down, rather than a small one scaled up: the glyph is rasterised
            // at fontSize, so a small font blown up by transform scale is a blurry number.
            text.fontSize = 96;
            text.characterSize = 0.16f;
            text.color = new Color(1f, 0.94f, 0.78f, 0.55f);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var renderer = label.GetComponent<MeshRenderer>();
            if (text.font != null) renderer.sharedMaterial = text.font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static GameObject AddBoardSlab(Transform parent, string slabName, Vector3 localPosition, Vector3 size)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = slabName;
            slab.transform.SetParent(parent, false);
            slab.transform.localPosition = localPosition;
            slab.transform.localScale = size;

            slab.GetComponent<Renderer>().sharedMaterial = BrickMaterial();
            return slab;
        }

        /// <summary>
        /// The board's masonry. One shared material for every slab, so the whole thing batches.
        ///
        /// World-space tiled — see EnvironmentBrick.shader. These slabs range from 1 unit thick to
        /// 26 wide and the primitive cube's UVs do not care, so anything sampled by UV would paint a
        /// single enormous brick on the long pieces.
        /// </summary>
        private static Material BrickMaterial()
        {
            const string path = "Assets/Art/Materials/Env_Brick.mat";

            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder("Assets/Art/Materials");

            var shader = Shader.Find("DinoBattle/EnvironmentBrick") ?? Shader.Find(EnvironmentShader);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            var brick = AssetDatabase.LoadAssetAtPath<Texture2D>(BrickTextureBuilder.BrickTexturePath);
            if (brick == null)
            {
                BrickTextureBuilder.Rebuild();
                brick = AssetDatabase.LoadAssetAtPath<Texture2D>(BrickTextureBuilder.BrickTexturePath);
            }

            if (brick != null) material.SetTexture("_MainTex", brick);
            if (material.HasProperty("_Tiling")) material.SetFloat("_Tiling", 0.22f);
            if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(0.92f, 0.90f, 0.88f));

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Open water under and around the board.
        ///
        /// The climb reads better with nothing beneath it: a staircase over a plain fades into the
        /// backdrop, and the same staircase over the sea has stakes. It is also cheap — one big quad
        /// on the flat environment shader, no reflection, no waves.
        ///
        /// Sized to the far clip rather than to the board so there is no visible edge to the world
        /// from any camera angle the rig permits, and dropped below the start platform so the bottom
        /// of the board sits just above the surface.
        /// </summary>
        private static void AddSea(Transform parent, float boardLength)
        {
            var sea = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sea.name = "Sea";
            sea.transform.SetParent(parent, false);
            sea.transform.localPosition = new Vector3(0f, -3f, boardLength * 0.5f);
            sea.transform.localScale = new Vector3(1600f, 1f, 1600f);

            Object.DestroyImmediate(sea.GetComponent<Collider>());
            TintShared(sea, new Color(0.06f, 0.24f, 0.36f));
        }

        private static void CreateGauntletDirector(BattleManager manager, GauntletArena arena,
                                                   OrbitCameraController cameraRig)
        {
            var director = manager.gameObject.AddComponent<GauntletDirector>();
            var serialized = new SerializedObject(director);

            serialized.FindProperty("arena").objectReferenceValue = arena;
            serialized.FindProperty("ladder").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Data.GauntletLadder>(SampleContentBuilder.GauntletLadderPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Pan box from the board's real extents rather than a guessed constant, with a margin so
            // the camera can sit behind the start platform and past the boss tier.
            float run = GauntletTierRise / Mathf.Tan(GauntletRampAngle * Mathf.Deg2Rad);
            float length = GauntletPlatformDepth + GauntletTiers * (run + GauntletPlatformDepth);

            var switcher = manager.gameObject.AddComponent<ArenaSwitcher>();
            var versusRoots = new List<GameObject>();
            foreach (string name in new[] { "Ground", "Boundary", "Environment" })
            {
                var root = GameObject.Find(name);
                if (root != null) versusRoots.Add(root);
            }

            switcher.Configure(versusRoots, arena.gameObject, cameraRig,
                new Vector2(GauntletOriginX - GauntletWidth, -30f),
                new Vector2(GauntletOriginX + GauntletWidth, length + 30f));
        }

        private static void CreateTerrainDressing()
        {
            var root = new GameObject("Environment").transform;

            var random = new System.Random(20260725);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            float half = ArenaRadius;

            // Ring of hills beyond the boundary walls, giving the horizon something to sit against.
            const int hillCount = 22;
            for (int i = 0; i < hillCount; i++)
            {
                // Everything here is a multiple of the arena, not an absolute size. Fixed numbers
                // tuned against the old larger field turned into hills wider than the whole arena
                // once it shrank, closing in over the fight.
                float angle = (i / (float)hillCount) * Mathf.PI * 2f + Range(-0.06f, 0.06f);
                float radius = half * Range(1.7f, 2.8f);
                float height = half * Range(0.3f, 0.65f);
                float width = half * Range(0.7f, 1.5f);

                var hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = $"Hill_{i}";
                Object.DestroyImmediate(hill.GetComponent<Collider>());
                hill.transform.SetParent(root, false);

                // Wide and mostly buried, so only a shallow cap shows. Narrow spheres sitting high
                // read as beach balls on the horizon rather than landforms.
                //
                // The sphere's half-height is scale.y/2 = height * 1.5, so the cap that stays above
                // ground is height * (1.5 - sink). Sink must be under 1.5 or the hill vanishes
                // entirely — at 1.5-2.2 they were all at or below the ground plane, invisible.
                float sink = Range(1.15f, 1.35f);
                hill.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, -height * sink, Mathf.Sin(angle) * radius);
                hill.transform.localScale = new Vector3(width, height * 3f, width * Range(0.8f, 1.3f));

                TintShared(hill, Color.Lerp(
                    new Color(0.17f, 0.26f, 0.17f), new Color(0.25f, 0.33f, 0.21f), (float)random.NextDouble()));
            }

            // Boulders ringing the arena from OUTSIDE the boundary, so they dress the edge without
            // ever standing in the way. Their colliders are removed as well: the boundary wall
            // already stops anything leaving, and a rock that can trap a creature is a liability
            // whichever side of the line it sits on.
            const int boulderCount = 26;
            for (int i = 0; i < boulderCount; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);

                // Pushed further out. At 1.08 of the radius a boulder sat barely past the wall, close
                // enough for the camera to end up inside one — a grey slab covering a corner of the
                // screen with no explanation.
                float radius = half * Range(1.5f, 2.1f);

                var rock = PlaceModel(root, RockModels[i % RockModels.Length], $"Boulder_{i}",
                    new Vector3(Mathf.Cos(angle) * radius, -0.15f, Mathf.Sin(angle) * radius),
                    Range(0f, 360f), Range(1.2f, 2.6f));

                if (rock != null)
                {
                    Color stone = Color.Lerp(
                        new Color(0.26f, 0.25f, 0.22f), new Color(0.36f, 0.34f, 0.29f), (float)random.NextDouble());
                    PaintModel(rock, stone, stone);
                }
            }

            CreateJungle(root, random, Range);

            ScatterFloorDressing(root, random, Range, half);
        }

        /// <summary>
        /// Small grass tufts and stones scattered over the arena floor.
        ///
        /// These used to be flat tinted discs, and that was the problem: a disc lying on the ground
        /// under a creature is exactly what a team ring is, so the floor dressing and the red/blue
        /// markers read as the same kind of object. A player could not tell which circles meant
        /// something. Reported directly — "바닥에 동그란 지형은 다른 걸로 바꿔줘 파란/빨강 표시하는거랑
        /// 헷갈려".
        ///
        /// Actual geometry standing up off the floor cannot be confused with a marker painted on it,
        /// which makes the team rings the only circles in the arena again.
        ///
        /// No colliders, like everything else inside the ring — PlaceModel strips them, and
        /// WarnAboutObstructions fails the build if anything solid slips in.
        /// </summary>
        private static void ScatterFloorDressing(
            Transform root, System.Random random, System.Func<float, float, float> Range, float half)
        {
            // Kept sparse and small. The discs were singled out by a playtester for covering the
            // floor, and the fix for that was fewer and fainter — swapping the shape is no licence to
            // fill the arena again. Ground dressing works when you do not notice it.
            const int tuftCount = 22;
            const int stoneCount = 9;

            for (int i = 0; i < tuftCount; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = half * Range(0.05f, 0.92f);

                var tuft = PlaceModel(root, random.Next(2) == 0 ? "Grass_Small" : "Grass_Large",
                    $"FloorTuft_{i}",
                    new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius),
                    Range(0f, 360f), Range(0.5f, 0.95f));

                if (tuft == null) continue;

                // Darker and less saturated than the treeline undergrowth, so the floor reads as
                // ground with things on it rather than as a second layer of canopy.
                Color blade = Color.Lerp(new Color(0.17f, 0.28f, 0.14f), new Color(0.26f, 0.37f, 0.18f),
                    (float)random.NextDouble());
                PaintModel(tuft, blade, blade);
            }

            for (int i = 0; i < stoneCount; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = half * Range(0.1f, 0.9f);

                var stone = PlaceModel(root, $"Rock_{random.Next(1, 6)}", $"FloorStone_{i}",
                    new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius),
                    Range(0f, 360f), Range(0.25f, 0.5f));

                if (stone == null) continue;

                Color grey = Color.Lerp(new Color(0.30f, 0.30f, 0.28f), new Color(0.42f, 0.41f, 0.37f),
                    (float)random.NextDouble());
                PaintModel(stone, grey, grey);
            }
        }

        private static OrbitCameraController CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            // Matched to RenderSettings.fogColor on purpose. The ground fades to fog with distance, so
            // any other sky colour leaves a hard seam along the horizon where the plane simply stops —
            // which is exactly the visible edge that made the world look like it ran out.
            // Solid colour, not the skybox. The default skybox is what was actually being drawn, so
            // matching the background colour to the fog achieved nothing and the ground plane's far
            // corner stayed silhouetted against a brown gradient. Clearing to the fog colour makes
            // the horizon genuinely dissolve — the ground fades into a sky of the same value and
            // there is no edge left to see. Ambient light is Trilight, so nothing needs the skybox.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.58f, 0.66f, 0.56f);

            // 200, not 600. Nothing in this scene is further away than the outer hills at roughly 60
            // units, and a 0.3-to-600 depth range wastes precision on empty space — precision that
            // the near-coplanar ground dressing needs. HDR off: there is no bloom or tonemapping to
            // consume it, so it only costs a wider framebuffer and a resolve on mobile.
            camera.farClipPlane = 200f;
            camera.allowHDR = false;

            cameraObject.AddComponent<AudioListener>();

            var rig = cameraObject.AddComponent<OrbitCameraController>();
            cameraObject.AddComponent<BattleCameraDirector>();

            // Tapping a creature points the director at it. Added after a four-year-old's playtest:
            // the automatic framing follows the densest fighting and kept swinging away from the one
            // dinosaur he was trying to watch.
            cameraObject.AddComponent<CreatureFocusPicker>();

            // Tie the pan bounds to the arena instead of leaving the component default, which was
            // sized for the old, larger field and would let the player pan off into empty ground.
            var rigSerialized = new SerializedObject(rig);
            rigSerialized.FindProperty("panLimit").floatValue = ArenaRadius * 1.1f;
            rigSerialized.ApplyModifiedPropertiesWithoutUndo();

            return rig;
        }

        // ---------------------------------------------------------------- managers

        private static (BattleManager, PlacementController, AutoPlacer) CreateManagers(OrbitCameraController cameraRig)
        {
            var managerObject = new GameObject("BattleManager");
            managerObject.AddComponent<CreatureSpawner>();
            managerObject.AddComponent<MobilePerformance>();
            var manager = managerObject.AddComponent<BattleManager>();

            var roster = AssetDatabase.LoadAssetAtPath<CreatureRoster>("Assets/GameData/Rosters/Roster_Default.asset");
            if (roster == null)
            {
                Debug.LogWarning("[BattleSceneBuilder] Roster_Default not found. Run 'Dino Battle > 1. Generate Sample Content' first.");
            }

            var managerSerialized = new SerializedObject(manager);
            managerSerialized.FindProperty("roster").objectReferenceValue = roster;
            managerSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Auto-placement lives on the manager object and is told the arena size from here, so
            // the formation radius cannot drift out of step with the boundary.
            var autoPlacer = managerObject.AddComponent<AutoPlacer>();
            var autoSerialized = new SerializedObject(autoPlacer);
            autoSerialized.FindProperty("battleManager").objectReferenceValue = manager;
            autoSerialized.FindProperty("arenaRadius").floatValue = ArenaRadius;
            autoSerialized.FindProperty("bossRoster").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<CreatureRoster>(SampleContentBuilder.BossRosterPath);
            autoSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Translucent disc showing where the next creature will land.
            var preview = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            preview.name = "PlacementPreview";
            Object.DestroyImmediate(preview.GetComponent<Collider>());
            preview.transform.localScale = new Vector3(3f, 0.05f, 3f);
            preview.SetActive(false);

            var placementObject = new GameObject("PlacementController");
            var placement = placementObject.AddComponent<PlacementController>();

            var placementSerialized = new SerializedObject(placement);
            placementSerialized.FindProperty("placementCamera").objectReferenceValue = cameraRig.GetComponent<Camera>();
            placementSerialized.FindProperty("battleManager").objectReferenceValue = manager;
            placementSerialized.FindProperty("previewMarker").objectReferenceValue = preview;
            placementSerialized.ApplyModifiedPropertiesWithoutUndo();

            return (manager, placement, autoPlacer);
        }

        // ---------------------------------------------------------------- HUD

        private static void CreateHud(BattleManager manager, PlacementController placement, AutoPlacer autoPlacer)
        {
            var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            // Landscape reference, matching the orientation AndroidBuilder locks the player to.
            // With match = 0.5 the scale depends only on refWidth * refHeight, so a portrait reference
            // happened to produce identical numbers -- but it silently breaks if match is ever changed.
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));

            // ---- placement panel (bottom bar) ----
            //
            // Two buttons: fill the arena, and start. Nothing else.
            //
            // The bar used to carry a team toggle, undo, mirror, a budget readout and a row of six
            // species buttons, all in service of hand-placing an army one creature at a time. In a
            // spectator game that is a chore standing between the player and the only part they came
            // for, and a playtester never worked out that any of it did anything. Auto-fill IS the
            // setup step, so it is the setup UI.
            var placementPanel = CreatePanel(canvasObject.transform, "PlacementPanel",
                new Vector2(0f, 0f), new Vector2(1f, 0.16f));

            var autoFillButton = CreateButton(placementPanel.transform, "AutoFill", "자동 배치",
                new Vector2(0.04f, 0.18f), new Vector2(0.35f, 0.82f), icon: ButtonIconBuilder.AutoFill);
            var bossButton = CreateButton(placementPanel.transform, "BossBattle", "보스 전투",
                new Vector2(0.37f, 0.18f), new Vector2(0.63f, 0.82f), icon: ButtonIconBuilder.Boss);
            var startButton = CreateButton(placementPanel.transform, "Start", "전투 시작",
                new Vector2(0.65f, 0.18f), new Vector2(0.96f, 0.82f), icon: ButtonIconBuilder.Start);

            // ---- mode bar (top of the setup screen) ----
            //
            // Top, not bottom, because the bottom bar is the setup controls and the mode is a choice
            // ABOUT that setup — it changes what "자동 배치" and "전투 시작" will do. It also has to
            // be somewhere a thumb does not rest during placement, or a mis-tap throws the arrangement
            // away.
            //
            // Only shown during placement. Switching arena mid-fight would leave creatures standing
            // on geometry that had just been deactivated.
            var modePanel = CreatePanel(canvasObject.transform, "ModePanel",
                new Vector2(0.22f, 0.90f), new Vector2(0.78f, 1f));

            var versusModeButton = CreateButton(modePanel.transform, "ModeVersus", "대결",
                new Vector2(0.04f, 0.15f), new Vector2(0.48f, 0.85f));
            var gauntletModeButton = CreateButton(modePanel.transform, "ModeGauntlet", "계단 등반",
                new Vector2(0.52f, 0.15f), new Vector2(0.96f, 0.85f));

            // ---- fighting panel (top bar) ----
            // Reported: "hud가 너무 커서 공룡 전투액션을 다 가리는 문제가 잇어."
            //
            // This bar was a tenth of the screen, full width, at 72% opacity — and in a gauntlet it
            // was joined by a second one just below it, so a fifth of the display was chrome laid
            // over the only thing the player came to watch. A spectator game cannot afford that: the
            // readouts exist to describe the fight, and they were covering it.
            //
            // Shorter and much more transparent. The counts and bars stay legible against it because
            // they are bright on dark, and what is behind them now shows through.
            var fightingPanel = CreatePanel(canvasObject.transform, "FightingPanel",
                new Vector2(0f, 0.928f), new Vector2(1f, 1f));
            fightingPanel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.42f);

            // Count plus a bar of the team's remaining share of its starting health. The count on its
            // own cannot distinguish three creatures at full strength from three about to fall over,
            // and those are the two situations a spectator most wants to tell apart.
            var redCount = CreateLabel(fightingPanel.transform, "RedCount", "0",
                new Vector2(0.02f, 0.1f), new Vector2(0.09f, 0.9f), TextAnchor.MiddleLeft);
            redCount.color = new Color(1f, 0.45f, 0.40f);

            // The two bars go on their own nested canvas. They are the only UI in the game that
            // changes every frame — BattleHUD writes fillAmount and colour from Update while the
            // health is draining — and in UGUI a single dirty Graphic rebuilds and re-batches its
            // WHOLE canvas. On one canvas for the entire HUD, that meant every button, icon and
            // label being re-batched sixty times a second so that two bars could move.
            //
            // A nested Canvas is the standard fix: it makes its own rebuild scope, so the churn stops
            // at this boundary. The cost is that its contents cannot batch with the panel around them
            // — a draw call or two — which is a good trade against a full rebuild per frame.
            var liveReadouts = CreateIsolatedLayer(fightingPanel.transform, "LiveReadouts");

            var redHealthFill = CreateTeamHealthBar(liveReadouts, "RedHealth",
                new Vector2(0.10f, 0.28f), new Vector2(0.29f, 0.72f),
                new Color(1f, 0.30f, 0.25f), Image.OriginHorizontal.Left);

            var blueCount = CreateLabel(fightingPanel.transform, "BlueCount", "0",
                new Vector2(0.91f, 0.1f), new Vector2(0.98f, 0.9f), TextAnchor.MiddleRight);
            blueCount.color = new Color(0.45f, 0.70f, 1f);

            // Filled from the right, mirroring the red bar, so both drain toward the middle and the
            // two lengths can be compared directly.
            var blueHealthFill = CreateTeamHealthBar(liveReadouts, "BlueHealth",
                new Vector2(0.71f, 0.28f), new Vector2(0.90f, 0.72f),
                new Color(0.35f, 0.65f, 1f), Image.OriginHorizontal.Right);

            // Mid-fight controls, where a spectator actually wants them: restart this same match, or
            // stop watching. The speed control that used to sit here is gone — in a match that
            // resolves in under a minute it was a button that changed how fast the thing you came to
            // watch went past, and nothing else.
            var fightReplayButton = CreateButton(fightingPanel.transform, "FightReplay", "다시 하기",
                new Vector2(0.30f, 0.08f), new Vector2(0.545f, 0.92f), icon: ButtonIconBuilder.Replay);
            var fightQuitButton = CreateButton(fightingPanel.transform, "FightQuit", "종료",
                new Vector2(0.565f, 0.08f), new Vector2(0.72f, 0.92f), icon: ButtonIconBuilder.Quit);

            // ---- gauntlet readouts (under the fight bar, gauntlet mode only) ----
            //
            // Its own panel rather than fields borrowed from the fight bar, because the two modes
            // want to say different things: versus reports two armies' strength, a climb reports how
            // far up you are and what you have left to spend.
            //
            // "더 보내기" is the whole economy in one button. It is only interactable when everything
            // sent is dead, so it cannot be used to stack waves — the run is a sequence of attempts,
            // not a tap-to-win.
            // A compact centred strip, not a second full-width bar. Two readouts and a button do not
            // need the whole width, and stacking two opaque bands was what buried the fight.
            var gauntletPanel = CreatePanel(canvasObject.transform, "GauntletPanel",
                new Vector2(0.30f, 0.862f), new Vector2(0.70f, 0.924f));
            gauntletPanel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.42f);

            // One line across the whole strip. There was a second label beside this one showing the
            // remaining run budget; waves are unlimited now, so it had nothing to say and the strip
            // gets the width back.
            var tierLabel = CreateLabel(gauntletPanel.transform, "Tier", "1층 / 10",
                new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), TextAnchor.MiddleCenter);

            // Below the strip and only visible when a wave is actually owed, so it is not a button
            // sitting over the arena for the whole run.
            var sendWaveButton = CreateButton(canvasObject.transform, "SendWave", "더 보내기",
                new Vector2(0.38f, 0.775f), new Vector2(0.62f, 0.855f), icon: ButtonIconBuilder.Start);
            sendWaveButton.gameObject.SetActive(false);

            gauntletPanel.SetActive(false);

            // ---- result panel (center) ----
            // Sits high on the screen, not across the middle.
            //
            // Centred, it landed squarely on top of the winning creature — the game ends and the
            // thing you want to look at is hidden behind a black box announcing that it won. The
            // result belongs out of the way of the last survivor standing on the field.
            var resultPanel = CreatePanel(canvasObject.transform, "ResultPanel",
                new Vector2(0.18f, 0.62f), new Vector2(0.82f, 0.95f));
            resultPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            var winnerLabel = CreateLabel(resultPanel.transform, "Winner", "빨강 승리",
                new Vector2(0.05f, 0.58f), new Vector2(0.95f, 0.98f), TextAnchor.MiddleCenter);
            // No explicit size: best-fit grows it to the rect, and the rect is what decides how
            // prominent the result is relative to the panel.

            // What actually happened, not just who won. "RED WINS" alone tells a player nothing
            // about whether it was close.
            var resultSummary = CreateLabel(resultPanel.transform, "Summary", "",
                new Vector2(0.05f, 0.34f), new Vector2(0.95f, 0.58f), TextAnchor.MiddleCenter);

            // Two endings, because they are different wishes. "Again" re-runs the same two armies,
            // which is what you want after a close result; "new setup" throws the arrangement away.
            // One button labelled "rematch" used to do the second while reading as the first.
            var replayButton = CreateButton(resultPanel.transform, "Replay", "한 번 더",
                new Vector2(0.08f, 0.04f), new Vector2(0.48f, 0.30f), icon: ButtonIconBuilder.Replay);
            var rematchButton = CreateButton(resultPanel.transform, "Rematch", "새로 짜기",
                new Vector2(0.52f, 0.04f), new Vector2(0.92f, 0.30f), icon: ButtonIconBuilder.Shuffle);

            resultPanel.SetActive(false);
            fightingPanel.SetActive(false);

            // ---- wire it up ----
            var hud = canvasObject.AddComponent<BattleHUD>();
            var s = new SerializedObject(hud);
            s.FindProperty("battleManager").objectReferenceValue = manager;
            s.FindProperty("placement").objectReferenceValue = placement;
            s.FindProperty("placementPanel").objectReferenceValue = placementPanel;
            s.FindProperty("fightingPanel").objectReferenceValue = fightingPanel;
            s.FindProperty("resultPanel").objectReferenceValue = resultPanel;
            s.FindProperty("autoPlacer").objectReferenceValue = autoPlacer;
            s.FindProperty("autoFillButton").objectReferenceValue = autoFillButton;
            s.FindProperty("startButton").objectReferenceValue = startButton;
            s.FindProperty("bossButton").objectReferenceValue = bossButton;
            s.FindProperty("replayButton").objectReferenceValue = replayButton;

            // The mirror, undo, team-toggle, budget and roster references stay in BattleHUD and stay
            // null. Every serialized reference there is optional by design, so the HUD simply skips
            // the controls that no longer exist — and putting the manual-placement bar back is a
            // matter of recreating the widgets here, with no runtime code to rewrite.
            // speedButton / speedLabel intentionally left null — the control was removed and every
            // HUD reference is optional, so the code that would drive it simply never runs.
            s.FindProperty("fightReplayButton").objectReferenceValue = fightReplayButton;
            s.FindProperty("fightQuitButton").objectReferenceValue = fightQuitButton;
            s.FindProperty("redCountLabel").objectReferenceValue = redCount;
            s.FindProperty("blueCountLabel").objectReferenceValue = blueCount;
            s.FindProperty("redHealthFill").objectReferenceValue = redHealthFill;
            s.FindProperty("blueHealthFill").objectReferenceValue = blueHealthFill;
            s.FindProperty("winnerLabel").objectReferenceValue = winnerLabel;
            s.FindProperty("resultSummaryLabel").objectReferenceValue = resultSummary;
            s.FindProperty("rematchButton").objectReferenceValue = rematchButton;

            s.FindProperty("versusReadouts").objectReferenceValue = liveReadouts.gameObject;
            s.FindProperty("modePanel").objectReferenceValue = modePanel;
            s.FindProperty("versusModeButton").objectReferenceValue = versusModeButton;
            s.FindProperty("gauntletModeButton").objectReferenceValue = gauntletModeButton;
            s.FindProperty("gauntletPanel").objectReferenceValue = gauntletPanel;
            s.FindProperty("tierLabel").objectReferenceValue = tierLabel;
            s.FindProperty("sendWaveButton").objectReferenceValue = sendWaveButton;
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- uGUI helpers

        /// <summary>
        /// A team strength bar: a dark trough with a filled bar inside it. Returns the fill Image, so
        /// the HUD drives it with fillAmount and never has to know how it was assembled.
        /// </summary>
        /// <summary>
        /// A transparent child canvas covering its parent, used to fence off UI that changes every
        /// frame from UI that does not.
        ///
        /// Stretched to the full parent rect on purpose: everything inside keeps addressing the same
        /// coordinate space it did before, so the anchors of whatever moves in here do not have to be
        /// recomputed. No GraphicRaycaster — nothing on this layer is meant to be touched, and adding
        /// one would put it in front of the buttons underneath.
        /// </summary>
        private static Transform CreateIsolatedLayer(Transform parent, string name)
        {
            var layer = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            layer.transform.SetParent(parent, false);
            Stretch(layer.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

            // Inherit the parent's sorting rather than override it, so draw order is unchanged.
            layer.GetComponent<Canvas>().overrideSorting = false;
            return layer.transform;
        }

        private static Image CreateTeamHealthBar(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Color color, Image.OriginHorizontal origin)
        {
            var trough = new GameObject(name, typeof(Image));
            trough.transform.SetParent(parent, false);
            Stretch(trough.GetComponent<RectTransform>(), anchorMin, anchorMax);
            trough.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var fillObject = new GameObject("Fill", typeof(Image));
            fillObject.transform.SetParent(trough.transform, false);
            Stretch(fillObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

            var fill = fillObject.GetComponent<Image>();
            fill.color = color;

            // Filled needs a sprite to fill; the builtin UI sprite is always present and is what an
            // Image created from the menu uses. Without one the fill draws nothing at all.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)origin;
            fill.fillAmount = 1f;

            return fill;
        }

        /// <summary>
        /// Put a generated icon on the left of a button. Silently does nothing when the icon is
        /// missing, so a scene built before 'Generate Button Icons' has run is still usable.
        /// </summary>
        private static void AddIcon(Transform parent, string icon)
        {
            if (string.IsNullOrEmpty(icon)) return;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/UI/{icon}.png");
            if (sprite == null)
            {
                Debug.LogWarning($"[BattleSceneBuilder] Icon '{icon}' not found — run " +
                                 "'Dino Battle > 7. Generate Button Icons'.");
                return;
            }

            var iconObject = new GameObject("Icon", typeof(Image));
            iconObject.transform.SetParent(parent, false);

            // Square, hugging the left edge. Anchored rather than sized in pixels so it keeps its
            // proportions on any display.
            Stretch(iconObject.GetComponent<RectTransform>(), new Vector2(0.05f, 0.14f), new Vector2(0.32f, 0.86f));

            var image = iconObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = new Color(1f, 0.95f, 0.80f);
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var panel = new GameObject(name, typeof(Image));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            Stretch(rect, anchorMin, anchorMax);

            panel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.72f);
            return panel;
        }

        private static Button CreateButton(Transform parent, string name, string caption,
            Vector2 anchorMin, Vector2 anchorMax, string icon = null)
        {
            var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Stretch(buttonObject.GetComponent<RectTransform>(), anchorMin, anchorMax);

            buttonObject.GetComponent<Image>().color = new Color(0.20f, 0.24f, 0.32f, 0.95f);

            // A picture as well as the words. The four-year-old this was tested on cannot read, and
            // picked a button by pressing the biggest one — including, twice, the one that quits.
            // The caption stays for everyone who can read; the icon is what makes the button mean
            // something to someone who cannot.
            AddIcon(buttonObject.transform, icon);

            // The caption fills 80% of the button, by insetting its rect 10% on every side and
            // letting best-fit grow the glyphs into what is left. Sizing captions with a fixed point
            // size meant they sat as small text in the middle of a large slab whatever the button's
            // dimensions were — and this game's audience includes people who cannot read yet, for
            // whom a big label is most of what makes a control legible.
            var label = CreateLabel(buttonObject.transform, "Label", caption,
                icon == null ? new Vector2(0.10f, 0.10f) : new Vector2(0.36f, 0.10f),
                new Vector2(0.90f, 0.90f), TextAnchor.MiddleCenter);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            return button;
        }

        private static Text CreateLabel(Transform parent, string name, string content,
            Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment)
        {
            var labelObject = new GameObject(name, typeof(Text));
            labelObject.transform.SetParent(parent, false);
            Stretch(labelObject.GetComponent<RectTransform>(), anchorMin, anchorMax);

            var text = labelObject.GetComponent<Text>();
            text.text = content;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.font = LoadUiFont();

            // Best-fit rather than a point size. The HUD is laid out in screen fractions, so the
            // pixel size of every box depends on the device; a fixed font size that looks right on
            // one display is tiny on a tablet and clipped on a small phone. Growing the glyphs to the
            // rect makes the text scale with the layout instead of fighting it.
            //
            // The max is deliberately far above any size that will actually be chosen — it is a
            // ceiling, not a target, and leaving it at the default 40 would silently cap the text.
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = 300;

            return text;
        }

        private const string KoreanFontPath = "Assets/Fonts/NanumGothic-Regular.ttf";

        /// <summary>
        /// The HUD font. Prefers the bundled Korean face, falls back to the builtin.
        ///
        /// Every string in this game is Korean, and the builtin font is Arial — it has no Hangul at
        /// all. In the editor that is invisible, because Unity's dynamic font system quietly borrows
        /// glyphs from the operating system, and Windows has Malgun Gothic. Android has no such
        /// guarantee: the fallback list varies by manufacturer and Android version, and when it comes
        /// up empty every label renders as blank boxes. Shipping the face inside the APK is the only
        /// way to make the text a property of the build rather than of the phone.
        ///
        /// Nanum Gothic covers all 11,172 Hangul syllables plus ASCII, verified against the font's
        /// own cmap, and costs 2 MB.
        /// </summary>
        private static Font LoadUiFont()
        {
            var korean = AssetDatabase.LoadAssetAtPath<Font>(KoreanFontPath);
            if (korean != null) return korean;

            Debug.LogWarning($"[BattleSceneBuilder] {KoreanFontPath} is missing — falling back to the " +
                             "builtin font, which has no Hangul. Korean labels may be blank on device.");
            return LoadBuiltinFont();
        }

        /// <summary>The builtin font was renamed in newer editors; try both names before giving up.</summary>
        private static Font LoadBuiltinFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) Debug.LogWarning("[BattleSceneBuilder] No builtin font found; assign fonts on the HUD manually.");
            return font;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Colour a decoration using a shared, quantised palette material.
        ///
        /// The dressing is ~90 objects. One material asset each would mean ninety near-identical
        /// files and ninety draw-call batches; rounding the colour to a coarse step collapses them
        /// onto a handful of reused assets instead, which also lets them batch.
        /// </summary>
        private static void TintShared(GameObject target, Color color)
        {
            if (!target.TryGetComponent<Renderer>(out var renderer)) return;

            renderer.sharedMaterial = SharedEnvironmentMaterial(color);
        }

        /// <summary>
        /// A shared opaque material for the requested colour, created once and reused.
        ///
        /// Colours are quantised to a 12-step palette before lookup, so the hundreds of randomised
        /// tints across the scenery collapse onto a few dozen assets. That is what lets the whole
        /// environment static-batch: two objects only ever share a batch if they share a material,
        /// and a unique material per rock would mean a draw call per rock.
        /// </summary>
        private static Material SharedEnvironmentMaterial(Color color)
        {
            int r = Mathf.RoundToInt(color.r * 12f);
            int g = Mathf.RoundToInt(color.g * 12f);
            int b = Mathf.RoundToInt(color.b * 12f);

            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder("Assets/Art/Materials");
            string path = $"Assets/Art/Materials/Env_Palette_{r}_{g}_{b}.mat";

            var shader = Shader.Find(EnvironmentShader) ?? Shader.Find("Standard");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                // These materials were created on Standard and are cached on disk, so simply changing
                // the line above would have left every existing one untouched — the same serialized-
                // value staleness that has caught this project three times. Re-point them.
                material.shader = shader;
            }

            // Free if static batching already covers a prop, and the fallback when it does not:
            // shadow passes and anything later excluded from batching can still be instanced. Every
            // prop sharing a palette colour shares this exact material, so a plain uniform is enough
            // — no per-instance property block needed.
            material.enableInstancing = true;

            var quantised = new Color(r / 12f, g / 12f, b / 12f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", quantised);
            if (material.HasProperty("_Color")) material.SetColor("_Color", quantised);

            // Vegetation and rock are matte. Standard defaults to half smoothness, which put a
            // plastic sheen on every leaf in the canopy.
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.05f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Tint(GameObject target, Color color)
        {
            if (!target.TryGetComponent<Renderer>(out var renderer)) return;

            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder("Assets/Art/Materials");
            string path = $"Assets/Art/Materials/Env_{target.name}.mat";

            // Reuse the asset on a rebuild instead of piling up "Env_Ground 1.mat" duplicates.
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(renderer.sharedMaterial);
                AssetDatabase.CreateAsset(material, path);
            }

            // The ground gets the flat shader too, and it is the one that matters most: it is a
            // single quad covering most of the screen, so every pixel of it runs this shader. It was
            // inheriting Standard from the primitive's Default-Material — a full PBR evaluation, at
            // very nearly full-screen resolution, to draw one flat olive colour.
            //
            // Same staleness trap as the palette materials: the asset is cached on disk, so a new
            // shader in the code above would never reach a material that already exists.
            var flat = Shader.Find(EnvironmentShader);
            if (flat != null && material.shader != flat) material.shader = flat;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);

            renderer.sharedMaterial = material;
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath)) return;

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
