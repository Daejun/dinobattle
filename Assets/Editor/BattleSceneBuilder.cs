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

        /// <summary>Playable area, bounded by invisible walls.</summary>
        private const float ArenaSize = 120f;

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
            var (manager, placement) = CreateManagers(cameraRig);
            CreateHud(manager, placement);

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

            // Invisible walls so knockback cannot punt a raptor out of the arena.
            for (int i = 0; i < 4; i++)
            {
                var wall = new GameObject($"Boundary_{i}");
                var collider = wall.AddComponent<BoxCollider>();
                collider.size = new Vector3(ArenaSize * 2f, 40f, 2f);

                float half = ArenaSize * 0.5f;
                wall.transform.SetPositionAndRotation(
                    Quaternion.Euler(0f, 90f * i, 0f) * new Vector3(0f, 20f, half),
                    Quaternion.Euler(0f, 90f * i, 0f));
            }

            var sun = new GameObject("Directional Light");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
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
        }

        /// <summary>
        /// Rocks, boulders and a ring of hills, placed procedurally.
        ///
        /// Deterministic: the random stream is seeded, so rebuilding the scene reproduces the same
        /// arena rather than reshuffling it under a diff. All of it is decoration parented under one
        /// object and pushed outside the fighting area — the arena floor stays clear, since the
        /// steering has no obstacle avoidance and would walk creatures straight into anything placed
        /// in the middle.
        /// </summary>
        private static void CreateTerrainDressing()
        {
            var root = new GameObject("Environment").transform;

            var random = new System.Random(20260725);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            float half = ArenaSize * 0.5f;

            // Ring of hills beyond the boundary walls, giving the horizon something to sit against.
            const int hillCount = 22;
            for (int i = 0; i < hillCount; i++)
            {
                float angle = (i / (float)hillCount) * Mathf.PI * 2f + Range(-0.06f, 0.06f);
                float radius = half * Range(1.3f, 2.2f);
                float height = Range(18f, 42f);
                float width = Range(60f, 130f);

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

            // Boulders hugging the arena edge. Colliders left on so a creature shoved outward hits
            // something solid instead of sliding along an invisible wall.
            const int boulderCount = 26;
            for (int i = 0; i < boulderCount; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = half * Range(0.82f, 0.97f);
                // Kept small relative to the creatures. At 1.6-4.4 units against a 5-unit T-Rex these
                // read as outbuildings rather than rocks.
                float size = Range(0.9f, 2.4f);

                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = $"Boulder_{i}";
                rock.transform.SetParent(root, false);
                rock.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, size * Range(0.15f, 0.35f), Mathf.Sin(angle) * radius);
                rock.transform.localRotation = Quaternion.Euler(Range(-18f, 18f), Range(0f, 360f), Range(-18f, 18f));
                rock.transform.localScale = new Vector3(size, size * Range(0.5f, 0.9f), size * Range(0.7f, 1.2f));

                // Darker than the ground, not lighter. Pale grey against green made them glow.
                TintShared(rock, Color.Lerp(
                    new Color(0.20f, 0.20f, 0.19f), new Color(0.30f, 0.29f, 0.26f), (float)random.NextDouble()));
            }

            // Flat scatter across the floor for a sense of scale underfoot. No colliders: these must
            // never trip a charging creature.
            const int patchCount = 40;
            for (int i = 0; i < patchCount; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = half * Range(0.05f, 0.9f);
                float size = Range(2.5f, 7f);

                var patch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                patch.name = $"GroundPatch_{i}";
                Object.DestroyImmediate(patch.GetComponent<Collider>());
                patch.transform.SetParent(root, false);
                patch.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, 0.02f, Mathf.Sin(angle) * radius);
                patch.transform.localScale = new Vector3(size, 0.01f, size * Range(0.6f, 1.4f));

                // Kept close to the ground colour. Higher contrast turned these into obvious pale
                // discs stamped on the field rather than subtle variation underfoot.
                TintShared(patch, Color.Lerp(
                    new Color(0.22f, 0.26f, 0.19f), new Color(0.27f, 0.30f, 0.21f), (float)random.NextDouble()));
            }
        }

        private static OrbitCameraController CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            camera.backgroundColor = new Color(0.42f, 0.55f, 0.68f);
            camera.farClipPlane = 600f;

            cameraObject.AddComponent<AudioListener>();

            var rig = cameraObject.AddComponent<OrbitCameraController>();
            cameraObject.AddComponent<BattleCameraDirector>();
            return rig;
        }

        // ---------------------------------------------------------------- managers

        private static (BattleManager, PlacementController) CreateManagers(OrbitCameraController cameraRig)
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

            return (manager, placement);
        }

        // ---------------------------------------------------------------- HUD

        private static void CreateHud(BattleManager manager, PlacementController placement)
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
            // The roster is the control the player uses most, so it gets roughly half the bar.
            var placementPanel = CreatePanel(canvasObject.transform, "PlacementPanel",
                new Vector2(0f, 0f), new Vector2(1f, 0.28f));

            var teamButton = CreateButton(placementPanel.transform, "TeamToggle", "TEAM: RED",
                new Vector2(0.02f, 0.72f), new Vector2(0.30f, 0.97f));
            var undoButton = CreateButton(placementPanel.transform, "Undo", "UNDO",
                new Vector2(0.34f, 0.72f), new Vector2(0.55f, 0.97f));
            var startButton = CreateButton(placementPanel.transform, "Start", "START BATTLE",
                new Vector2(0.59f, 0.72f), new Vector2(0.98f, 0.97f));
            var budgetLabel = CreateLabel(placementPanel.transform, "Budget", "1000 / 1000",
                new Vector2(0.02f, 0.55f), new Vector2(0.55f, 0.70f), TextAnchor.MiddleLeft);

            var rosterContainer = CreatePanel(placementPanel.transform, "RosterContainer",
                new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.52f));
            var layout = rosterContainer.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 4, 4);

            // childControl* defaults to false, which makes the group position children but leave their
            // size alone. A button instantiated from the stretched template then computes a preferred
            // size of zero and renders as nothing -- present in the hierarchy, invisible on screen.
            // childControl* must be true for childForceExpand* to actually divide the row up.
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var rosterTemplate = CreateButton(rosterContainer.transform, "RosterButtonTemplate", "Creature",
                Vector2.zero, Vector2.one);
            rosterTemplate.gameObject.SetActive(false);

            // ---- fighting panel (top bar) ----
            var fightingPanel = CreatePanel(canvasObject.transform, "FightingPanel",
                new Vector2(0f, 0.90f), new Vector2(1f, 1f));

            var redCount = CreateLabel(fightingPanel.transform, "RedCount", "0",
                new Vector2(0.02f, 0.1f), new Vector2(0.22f, 0.9f), TextAnchor.MiddleLeft);
            redCount.color = new Color(1f, 0.45f, 0.40f);

            var blueCount = CreateLabel(fightingPanel.transform, "BlueCount", "0",
                new Vector2(0.78f, 0.1f), new Vector2(0.98f, 0.9f), TextAnchor.MiddleRight);
            blueCount.color = new Color(0.45f, 0.70f, 1f);

            var speedButton = CreateButton(fightingPanel.transform, "Speed", "x1",
                new Vector2(0.40f, 0.1f), new Vector2(0.60f, 0.9f));
            var speedLabel = speedButton.GetComponentInChildren<Text>();

            // ---- result panel (center) ----
            var resultPanel = CreatePanel(canvasObject.transform, "ResultPanel",
                new Vector2(0.15f, 0.38f), new Vector2(0.85f, 0.62f));
            resultPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            var winnerLabel = CreateLabel(resultPanel.transform, "Winner", "RED WINS",
                new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.95f), TextAnchor.MiddleCenter);
            winnerLabel.fontSize = 48;

            var rematchButton = CreateButton(resultPanel.transform, "Rematch", "REMATCH",
                new Vector2(0.25f, 0.08f), new Vector2(0.75f, 0.40f));

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
            s.FindProperty("startButton").objectReferenceValue = startButton;
            s.FindProperty("undoButton").objectReferenceValue = undoButton;
            s.FindProperty("teamToggleButton").objectReferenceValue = teamButton;
            s.FindProperty("teamLabel").objectReferenceValue = teamButton.GetComponentInChildren<Text>();
            s.FindProperty("budgetLabel").objectReferenceValue = budgetLabel;
            s.FindProperty("rosterContainer").objectReferenceValue = rosterContainer.transform;
            s.FindProperty("rosterButtonTemplate").objectReferenceValue = rosterTemplate;
            s.FindProperty("speedButton").objectReferenceValue = speedButton;
            s.FindProperty("speedLabel").objectReferenceValue = speedLabel;
            s.FindProperty("redCountLabel").objectReferenceValue = redCount;
            s.FindProperty("blueCountLabel").objectReferenceValue = blueCount;
            s.FindProperty("winnerLabel").objectReferenceValue = winnerLabel;
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
