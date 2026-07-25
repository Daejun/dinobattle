using System.Collections.Generic;
using System.IO;
using DinoBattle.Data;
using DinoBattle.UI;
using DinoBattle.Units;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Builds a creature prefab, <see cref="CreatureDefinition"/> and roster for every entry in
    /// <see cref="CreatureBlueprints"/>. Uses the imported model when one is present and falls back
    /// to blocked-out primitives when it is not, so the game is playable before any art lands.
    ///
    /// This file is the factory only — the balance numbers live in CreatureBlueprints.cs, which is
    /// what a designer actually edits.
    ///
    /// Menu: Dino Battle > 1. Generate Sample Content
    /// </summary>
    public static class SampleContentBuilder
    {
        private const string CreatureDataPath = "Assets/GameData/Creatures";
        private const string RosterPath = "Assets/GameData/Rosters/Roster_Default.asset";
        private const string PrefabPath = "Assets/Prefabs/Creatures";

        [MenuItem("Dino Battle/1. Generate Sample Content", priority = 100)]
        public static void Generate()
        {
            EnsureFolder("Assets/GameData");
            EnsureFolder(CreatureDataPath);
            EnsureFolder("Assets/GameData/Rosters");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabPath);

            var definitions = new List<CreatureDefinition>();

            foreach (var blueprint in CreatureBlueprints.All)
            {
                string safeName = blueprint.Name.Replace(" ", "").Replace("-", "");
                GameObject prefab = CreatePlaceholderPrefab(blueprint, safeName);
                CreatureDefinition definition = CreateDefinition(blueprint, safeName, prefab);
                definitions.Add(definition);
            }

            var roster = AssetDatabase.LoadAssetAtPath<CreatureRoster>(RosterPath);
            if (roster == null)
            {
                roster = ScriptableObject.CreateInstance<CreatureRoster>();
                AssetDatabase.CreateAsset(roster, RosterPath);
            }

            // creatures is private; write through SerializedObject so the change is recorded properly.
            var serialized = new SerializedObject(roster);
            var list = serialized.FindProperty("creatures");
            list.arraySize = definitions.Count;
            for (int i = 0; i < definitions.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SampleContentBuilder] Generated {definitions.Count} creatures and the default roster.");
            Selection.activeObject = roster;
        }

        private static CreatureDefinition CreateDefinition(CreatureBlueprint blueprint, string safeName, GameObject prefab)
        {
            string path = $"{CreatureDataPath}/Creature_{safeName}.asset";

            var definition = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(path);
            bool isNew = definition == null;
            if (isNew) definition = ScriptableObject.CreateInstance<CreatureDefinition>();

            definition.displayName = blueprint.Name;
            definition.description = $"Placeholder stat block for {blueprint.Name}.";
            definition.prefab = prefab;
            definition.cost = blueprint.Cost;
            definition.footprintRadius = Mathf.Max(blueprint.BodySize.x, blueprint.BodySize.z) * 0.6f;
            definition.maxHealth = blueprint.Health;
            definition.armor = blueprint.Armor;
            definition.moveSpeed = blueprint.Speed;
            definition.turnSpeedDegrees = Mathf.Lerp(240f, 90f, Mathf.InverseLerp(900f, 8600f, blueprint.Mass));
            definition.mass = blueprint.Mass;
            definition.attackDamage = blueprint.Damage;
            definition.attackInterval = blueprint.Interval;
            definition.attackRange = blueprint.Range;
            definition.attackWindup = blueprint.Interval * 0.25f;
            definition.aggroRange = 90f;

            if (isNew) AssetDatabase.CreateAsset(definition, path);
            else EditorUtility.SetDirty(definition);

            return definition;
        }

        /// <summary>
        /// Blocked-out stand-in creature: a capsule body, a snout marker, and an aim point. Good enough
        /// to watch a fight resolve and to validate stats before art exists.
        /// </summary>
        private static GameObject CreatePlaceholderPrefab(CreatureBlueprint blueprint, string safeName)
        {
            string path = $"{PrefabPath}/Creature_{safeName}.prefab";

            var root = new GameObject($"Creature_{safeName}");

            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2; // Z, so the capsule lies along the body length.

            // Deliberately narrower and shorter than the visible body. The collider is a spacing
            // constraint, not a hitbox: sized to the full silhouette it held attackers a body-width
            // apart and bites appeared to land from thin air. Undersizing lets them close and overlap
            // slightly, which is what a real scrap looks like.
            collider.radius = blueprint.BodySize.x * 0.3f;
            collider.height = Mathf.Max(blueprint.BodySize.z * 0.5f, collider.radius * 2f);

            // center.y MUST equal the radius. This is a horizontal capsule, so its lowest point sits
            // (center.y - radius) above the root; anything higher and the creature settles that far
            // BELOW the ground once physics drops it. Centring on the body mass buried them by 0.6
            // units, which sank the team ring out of sight and — worse — put the root under the
            // ground probe's ray origin, so IsGrounded never returned true and nothing could move.
            collider.center = new Vector3(0f, collider.radius, 0f);

            var body = root.AddComponent<Rigidbody>();
            body.mass = blueprint.Mass;
            body.freezeRotation = true;

            // Order matters. RequireComponent auto-adds a missing dependency, so adding CreatureBrain
            // first pulled in a CreatureUnit, and the explicit AddComponent<CreatureUnit>() below then
            // created a SECOND one. Only one of the pair ever got Initialize() called, leaving the
            // other on its default team -- two hostile ghosts sharing one transform, killing each
            // other on spawn. Add dependencies before dependents, and use EnsureComponent so a
            // reordering mistake reuses the existing component instead of duplicating it.
            EnsureComponent<Health>(root);
            var unit = EnsureComponent<CreatureUnit>(root);
            EnsureComponent<CreatureLocomotion>(root);
            EnsureComponent<CreatureBrain>(root);
            EnsureComponent<GrappleHold>(root);
            var attack = EnsureComponent<MeleeAttack>(root);

            // Real art if the pack has been imported, blocked-out primitives otherwise. Keeping both
            // paths here means one generator owns the prefab and re-running the menu never wipes art.
            Animator animator = AttachModelVisual(root, blueprint, safeName);
            if (animator == null) AttachPlaceholderVisual(root, blueprint, safeName);

            AddTeamRing(root, blueprint);

            var aimPoint = new GameObject(CreatureRig.AimPoint).transform;
            aimPoint.SetParent(root.transform, false);
            aimPoint.localPosition = new Vector3(0f, blueprint.BodySize.y * 0.8f, blueprint.BodySize.z * 0.4f);

            // Wire the serialized references that Initialize does not set at runtime.
            var unitSerialized = new SerializedObject(unit);
            unitSerialized.FindProperty("aimPoint").objectReferenceValue = aimPoint;
            unitSerialized.FindProperty("corpseLifetime").floatValue = 12f;
            unitSerialized.ApplyModifiedPropertiesWithoutUndo();

            var attackSerialized = new SerializedObject(attack);
            attackSerialized.FindProperty("damage").floatValue = blueprint.Damage;
            attackSerialized.FindProperty("interval").floatValue = blueprint.Interval;
            attackSerialized.FindProperty("range").floatValue = blueprint.Range;
            attackSerialized.FindProperty("windup").floatValue = blueprint.Interval * 0.25f;
            attackSerialized.FindProperty("animator").objectReferenceValue = animator;
            attackSerialized.ApplyModifiedPropertiesWithoutUndo();

            // Wire the Animator explicitly rather than leaving it to the runtime GetComponent fallbacks.
            // check-project.sh verifies these property names, so a rename cannot silently unhook it.
            var brainSerialized = new SerializedObject(root.GetComponent<CreatureBrain>());
            brainSerialized.FindProperty("animator").objectReferenceValue = animator;
            brainSerialized.ApplyModifiedPropertiesWithoutUndo();

            AddHealthBar(root, blueprint, safeName);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Parent the imported model under the creature root, scaled so its length matches the design's
        /// BodySize.z. The pack authors models roughly six times larger than this game's scale, and the
        /// stat block — attackRange above all — is written in game units, so the model must come to the
        /// stats rather than the other way round.
        ///
        /// Returns the Animator, or null when the model or its controller is not present.
        /// </summary>
        private static Animator AttachModelVisual(GameObject root, CreatureBlueprint blueprint, string safeName)
        {
            if (string.IsNullOrEmpty(blueprint.Model)) return null;

            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Models/{blueprint.Model}.fbx");
            if (modelPrefab == null) return null;

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                $"Assets/Art/Animations/AC_{blueprint.Model}.controller");

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, root.transform);
            visual.name = CreatureRig.ModelVisual;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            Vector3 measured = CreatureArtImporter.MeasureModel(modelPrefab);
            float scale = measured.z > 0.001f ? blueprint.BodySize.z / measured.z : 1f;
            visual.transform.localScale = Vector3.one * scale;

            // Leave the imported materials alone unless this entry is a deliberate reskin. Flattening
            // every model to one blueprint colour threw away the shading the pack ships with, which is
            // the whole reason the creatures stopped looking like real animals.
            if (blueprint.Recolor)
            {
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    TintRenderer(renderer.gameObject, blueprint.Tint, safeName);
                }
            }

            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;   // CreatureLocomotion drives the Rigidbody, not the clip.

            if (controller == null)
            {
                Debug.LogWarning($"[SampleContentBuilder] {blueprint.Name}: model found but no Animator " +
                                 $"Controller (AC_{blueprint.Model}). Run 'Dino Battle > 4c. Prepare Creature Animation'.");
            }

            return animator;
        }

        /// <summary>
        /// Flat disc on the ground, recoloured per team at spawn by CreatureUnit. This carries the
        /// team read so the dinosaurs themselves can keep realistic colouring.
        /// </summary>
        private static void AddTeamRing(GameObject root, CreatureBlueprint blueprint)
        {
            // Cylinder, not Quad: a quad is square, and a square patch under each creature reads as a
            // paint blob rather than a unit marker. A flattened cylinder gives a disc.
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = CreatureRig.TeamRing;
            Object.DestroyImmediate(ring.GetComponent<Collider>());

            ring.transform.SetParent(root.transform, false);

            // Just above the ground to avoid z-fighting with the arena plane.
            ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            // Wide enough to read from the spectator camera but still inside the creature's footprint.
            // Keyed to body length: width alone made a raptor's marker a barely-visible speck.
            float diameter = Mathf.Max(1.2f, blueprint.BodySize.z * 0.6f);
            ring.transform.localScale = new Vector3(diameter, 0.01f, diameter);

            // Unlit so the marker stays readable regardless of how the sun hits the creature.
            // Saved as one shared asset: an unsaved Material would be serialised into each prefab
            // separately, giving six near-identical copies with no way to retune them together.
            // CreatureUnit calls renderer.material at spawn, which instances it per creature anyway.
            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Materials");
            const string ringMaterialPath = "Assets/Art/Materials/TeamRing.mat";

            var ringMaterial = AssetDatabase.LoadAssetAtPath<Material>(ringMaterialPath);
            if (ringMaterial == null)
            {
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                ringMaterial = new Material(shader);
                AssetDatabase.CreateAsset(ringMaterial, ringMaterialPath);
            }

            ring.GetComponent<Renderer>().sharedMaterial = ringMaterial;
        }

        /// <summary>Blocked-out capsule body plus a snout cube, used until real art is imported.</summary>
        private static void AttachPlaceholderVisual(GameObject root, CreatureBlueprint blueprint, string safeName)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = CreatureRig.PlaceholderBody;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, blueprint.BodySize.y * 0.5f, 0f);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(blueprint.BodySize.x, blueprint.BodySize.z * 0.5f, blueprint.BodySize.x);
            TintRenderer(visual, blueprint.Tint, safeName);

            // Snout marker so it is obvious which way the creature faces.
            var snout = GameObject.CreatePrimitive(PrimitiveType.Cube);
            snout.name = CreatureRig.PlaceholderHead;
            Object.DestroyImmediate(snout.GetComponent<Collider>());
            snout.transform.SetParent(root.transform, false);
            snout.transform.localPosition = new Vector3(0f, blueprint.BodySize.y * 0.85f, blueprint.BodySize.z * 0.45f);
            snout.transform.localScale = Vector3.one * (blueprint.BodySize.x * 0.55f);
            TintRenderer(snout, blueprint.Tint * 0.7f, safeName);
        }

        private static void AddHealthBar(GameObject root, CreatureBlueprint blueprint, string safeName)
        {
            var bar = new GameObject(CreatureRig.HealthBar);
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, blueprint.BodySize.y + 1.2f, 0f);

            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Background";
            Object.DestroyImmediate(background.GetComponent<Collider>());
            background.transform.SetParent(bar.transform, false);
            background.transform.localScale = new Vector3(blueprint.BodySize.z * 0.8f, 0.28f, 1f);
            TintRenderer(background, new Color(0.06f, 0.06f, 0.08f), safeName);

            var fillPivot = new GameObject("FillPivot");
            fillPivot.transform.SetParent(bar.transform, false);
            fillPivot.transform.localPosition = new Vector3(-blueprint.BodySize.z * 0.4f, 0f, -0.02f);

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            Object.DestroyImmediate(fill.GetComponent<Collider>());
            fill.transform.SetParent(fillPivot.transform, false);
            // Pivot the quad at its left edge so scaling X drains the bar from the right.
            fill.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            fill.transform.localScale = Vector3.one;
            TintRenderer(fill, new Color(0.25f, 0.85f, 0.35f), safeName);

            fillPivot.transform.localScale = new Vector3(blueprint.BodySize.z * 0.78f, 0.22f, 1f);

            var billboard = bar.AddComponent<HealthBarBillboard>();
            var serialized = new SerializedObject(billboard);
            serialized.FindProperty("health").objectReferenceValue = root.GetComponent<Health>();
            serialized.FindProperty("fill").objectReferenceValue = fillPivot.transform;
            serialized.FindProperty("fillRenderer").objectReferenceValue = fill.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Give <paramref name="target"/> its own tinted material, named per creature so the asset is
        /// identifiable and so re-running this menu command reuses it.
        ///
        /// This used to call GenerateUniqueAssetPath keyed on the GameObject name, which produced
        /// "Placeholder_Visual_Body 1..5" -- unidentifiable, and another full set on every re-run.
        /// </summary>
        private static void TintRenderer(GameObject target, Color color, string creatureName)
        {
            if (!target.TryGetComponent<Renderer>(out var renderer)) return;

            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Materials");
            string path = $"Assets/Art/Materials/Placeholder_{creatureName}_{target.name}.mat";

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

        /// <summary>
        /// Return the existing component of type T, or add one. Guards against the RequireComponent
        /// duplication trap: a dependency may already have been auto-added by a dependent.
        /// </summary>
        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
