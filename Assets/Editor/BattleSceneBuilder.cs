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
            var cameraRig = CreateCamera();
            var (manager, placement, autoPlacer) = CreateManagers(cameraRig);
            CreateHud(manager, placement, autoPlacer);

            WarnAboutObstructions();

            SampleContentBuilder.EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddSceneToBuildSettings();

            Debug.Log($"[BattleSceneBuilder] Built {ScenePath}. Press Play, tap the ground to place, then Start Battle.");
        }

        // ---------------------------------------------------------------- environment

        private static void CreateEnvironment()
        {
            // Extends well past the playable arena. Sized to ArenaSize the plane ended right where the
            // hills begin, so the horizon dressing floated over open space with a hard edge under it.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = Vector3.one * (ArenaSize * GroundExtent / 10f);
            Tint(ground, new Color(0.24f, 0.28f, 0.20f));

            CreateCircularBoundary();

            var sun = new GameObject("Directional Light");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            // Hard, not soft. Soft shadows are among the most expensive things a mobile GPU can be
            // asked for, and against flat-shaded low-poly models the difference barely registers.
            light.shadows = LightShadows.Hard;
            light.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(48f, 34f, 0f);

            // A code-built scene has no skybox, and Unity's default ambient source IS the skybox — so
            // ambient light lands at roughly zero and every surface facing away from the sun renders
            // black. The creatures looked like silhouettes for exactly this reason. An explicit
            // trilight gradient restores the fill light a skybox would normally provide.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.60f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.26f, 0.22f);
            RenderSettings.ambientIntensity = 1f;

            // Distance fog hides the hard edge where the ground plane stops, which otherwise reads as
            // the world simply ending a short walk away.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.70f);
            RenderSettings.fogStartDistance = ArenaSize * 0.9f;
            RenderSettings.fogEndDistance = ArenaSize * 2.6f;

            CreateTerrainDressing();
            RenderSettings.ambientIntensity = 1f;

            MarkSceneryStatic();
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
                    new Color(0.26f, 0.31f, 0.24f), new Color(0.36f, 0.34f, 0.28f), (float)random.NextDouble()));
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

                // Kept small relative to the creatures. At 1.6-4.4 units against a 5-unit T-Rex these
                // read as outbuildings rather than rocks.
                float size = Range(0.9f, 2.4f);

                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = $"Boulder_{i}";
                Object.DestroyImmediate(rock.GetComponent<Collider>());
                rock.transform.SetParent(root, false);

                float height = size * Range(0.5f, 0.9f);
                float tilt = Range(-18f, 18f);

                // Bed them into the ground. Positioning by an arbitrary fraction of size left rocks
                // hovering with daylight underneath; half the height minus a bite sinks them so the
                // base is buried whatever the tilt does to the corners.
                rock.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, height * 0.5f - size * 0.18f, Mathf.Sin(angle) * radius);
                rock.transform.localRotation = Quaternion.Euler(tilt, Range(0f, 360f), tilt * 0.5f);
                rock.transform.localScale = new Vector3(size, height, size * Range(0.7f, 1.2f));

                // Darker than the ground, not lighter. Pale grey against green made them glow.
                TintShared(rock, Color.Lerp(
                    new Color(0.20f, 0.20f, 0.19f), new Color(0.30f, 0.29f, 0.26f), (float)random.NextDouble()));
            }

            // Flat scatter across the floor for a sense of scale underfoot. No colliders: these must
            // never trip a charging creature.
            // Fewer and smaller than before, and barely tinted.
            //
            // At forty patches of 2.5-7 units across an arena of radius 22, they covered most of the
            // floor and overlapped constantly — the result read as blotchy stains rather than as
            // ground, and a playtester singled them out as the thing they disliked most about the
            // arena. Ground dressing works when you do not notice it.
            const int patchCount = 16;
            for (int i = 0; i < patchCount; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = half * Range(0.05f, 0.9f);
                float size = Range(1.6f, 3.4f);

                var patch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                patch.name = $"GroundPatch_{i}";
                Object.DestroyImmediate(patch.GetComponent<Collider>());
                patch.transform.SetParent(root, false);
                // Every patch gets its own height. Placing them all on one plane meant any two that
                // overlapped had identical depth, so the GPU picked a different winner per frame and
                // the floor visibly flickered. A few millimetres of separation each makes the
                // ordering deterministic, and the stack stays far too shallow to see from the camera.
                // Keep the whole stack under the team ring's height, or a creature's marker ends up
                // buried beneath the scenery it is standing on.
                float lift = 0.02f + i * 0.003f;

                patch.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, lift, Mathf.Sin(angle) * radius);
                patch.transform.localScale = new Vector3(size, 0.01f, size * Range(0.6f, 1.4f));

                // Within a hair of the ground colour (0.24, 0.28, 0.20). The previous spread was wide
                // enough to see each disc's outline, which is what made them read as stains.
                TintShared(patch, Color.Lerp(
                    new Color(0.235f, 0.272f, 0.196f), new Color(0.252f, 0.290f, 0.208f), (float)random.NextDouble()));
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
            camera.backgroundColor = new Color(0.62f, 0.66f, 0.70f);

            // 200, not 600. Nothing in this scene is further away than the outer hills at roughly 60
            // units, and a 0.3-to-600 depth range wastes precision on empty space — precision that
            // the near-coplanar ground dressing needs. HDR off: there is no bloom or tonemapping to
            // consume it, so it only costs a wider framebuffer and a resolve on mobile.
            camera.farClipPlane = 200f;
            camera.allowHDR = false;

            cameraObject.AddComponent<AudioListener>();

            var rig = cameraObject.AddComponent<OrbitCameraController>();
            cameraObject.AddComponent<BattleCameraDirector>();

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
                new Vector2(0.06f, 0.18f), new Vector2(0.47f, 0.82f));
            var startButton = CreateButton(placementPanel.transform, "Start", "전투 시작",
                new Vector2(0.53f, 0.18f), new Vector2(0.94f, 0.82f));

            // ---- fighting panel (top bar) ----
            var fightingPanel = CreatePanel(canvasObject.transform, "FightingPanel",
                new Vector2(0f, 0.90f), new Vector2(1f, 1f));

            var redCount = CreateLabel(fightingPanel.transform, "RedCount", "0",
                new Vector2(0.02f, 0.1f), new Vector2(0.22f, 0.9f), TextAnchor.MiddleLeft);
            redCount.color = new Color(1f, 0.45f, 0.40f);

            var blueCount = CreateLabel(fightingPanel.transform, "BlueCount", "0",
                new Vector2(0.78f, 0.1f), new Vector2(0.98f, 0.9f), TextAnchor.MiddleRight);
            blueCount.color = new Color(0.45f, 0.70f, 1f);

            var speedButton = CreateButton(fightingPanel.transform, "Speed", "1배속",
                new Vector2(0.40f, 0.1f), new Vector2(0.60f, 0.9f));
            var speedLabel = speedButton.GetComponentInChildren<Text>();

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
            winnerLabel.fontSize = 44;

            // What actually happened, not just who won. "RED WINS" alone tells a player nothing
            // about whether it was close.
            var resultSummary = CreateLabel(resultPanel.transform, "Summary", "",
                new Vector2(0.05f, 0.34f), new Vector2(0.95f, 0.58f), TextAnchor.MiddleCenter);
            resultSummary.fontSize = 24;

            var rematchButton = CreateButton(resultPanel.transform, "Rematch", "다시 하기",
                new Vector2(0.30f, 0.04f), new Vector2(0.70f, 0.30f));

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

            // The mirror, undo, team-toggle, budget and roster references stay in BattleHUD and stay
            // null. Every serialized reference there is optional by design, so the HUD simply skips
            // the controls that no longer exist — and putting the manual-placement bar back is a
            // matter of recreating the widgets here, with no runtime code to rewrite.
            s.FindProperty("speedButton").objectReferenceValue = speedButton;
            s.FindProperty("speedLabel").objectReferenceValue = speedLabel;
            s.FindProperty("redCountLabel").objectReferenceValue = redCount;
            s.FindProperty("blueCountLabel").objectReferenceValue = blueCount;
            s.FindProperty("winnerLabel").objectReferenceValue = winnerLabel;
            s.FindProperty("resultSummaryLabel").objectReferenceValue = resultSummary;
            s.FindProperty("rematchButton").objectReferenceValue = rematchButton;
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- uGUI helpers

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
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Stretch(buttonObject.GetComponent<RectTransform>(), anchorMin, anchorMax);

            buttonObject.GetComponent<Image>().color = new Color(0.20f, 0.24f, 0.32f, 0.95f);

            var label = CreateLabel(buttonObject.transform, "Label", caption,
                Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
            label.fontSize = 26;

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
            text.fontSize = 30;
            text.raycastTarget = false;
            text.font = LoadBuiltinFont();
            return text;
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

            int r = Mathf.RoundToInt(color.r * 12f);
            int g = Mathf.RoundToInt(color.g * 12f);
            int b = Mathf.RoundToInt(color.b * 12f);

            SampleContentBuilder.EnsureFolder("Assets/Art");
            SampleContentBuilder.EnsureFolder("Assets/Art/Materials");
            string path = $"Assets/Art/Materials/Env_Palette_{r}_{g}_{b}.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(renderer.sharedMaterial);
                AssetDatabase.CreateAsset(material, path);
            }

            var quantised = new Color(r / 12f, g / 12f, b / 12f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", quantised);
            if (material.HasProperty("_Color")) material.SetColor("_Color", quantised);
            EditorUtility.SetDirty(material);

            renderer.sharedMaterial = material;
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
