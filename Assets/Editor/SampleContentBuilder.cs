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
    /// Generates placeholder creature prefabs, <see cref="CreatureDefinition"/> assets and a roster
    /// so the game is playable before any real art lands. Replace the primitive visuals with imported
    /// models later — the definitions and prefab wiring stay valid.
    ///
    /// Menu: Dino Battle > 1. Generate Sample Content
    /// </summary>
    public static class SampleContentBuilder
    {
        private const string CreatureDataPath = "Assets/GameData/Creatures";
        private const string RosterPath = "Assets/GameData/Rosters/Roster_Default.asset";
        private const string PrefabPath = "Assets/Prefabs/Creatures";

        /// <summary>Starting balance pass. Costs are tuned so ~1000 buys a small mixed team.</summary>
        private readonly struct Blueprint
        {
            public readonly string Name;
            public readonly int Cost;
            public readonly float Health;
            public readonly float Armor;
            public readonly float Damage;
            public readonly float Interval;
            public readonly float Range;
            public readonly float Speed;
            public readonly float Mass;
            public readonly Vector3 BodySize;
            public readonly Color Tint;

            public Blueprint(string name, int cost, float health, float armor, float damage,
                float interval, float range, float speed, float mass, Vector3 bodySize, Color tint)
            {
                Name = name; Cost = cost; Health = health; Armor = armor; Damage = damage;
                Interval = interval; Range = range; Speed = speed; Mass = mass;
                BodySize = bodySize; Tint = tint;
            }
        }

        private static readonly Blueprint[] Blueprints =
        {
            //            name             cost  hp     armor dmg   int   rng  spd  mass  body size                 tint
            new("T-Rex",          420, 4200f, 12f, 480f, 1.6f, 5.0f, 6.5f, 8000f, new Vector3(2.0f, 2.4f, 5.0f), new Color(0.45f, 0.32f, 0.22f)),
            new("Bio T-Rex",      520, 4800f, 20f, 560f, 1.5f, 5.2f, 7.0f, 8600f, new Vector3(2.1f, 2.5f, 5.2f), new Color(0.20f, 0.55f, 0.35f)),
            new("Spinosaurus",    380, 3600f,  8f, 420f, 1.5f, 4.6f, 6.8f, 6800f, new Vector3(1.9f, 2.3f, 5.4f), new Color(0.35f, 0.38f, 0.52f)),
            new("Triceratops",    300, 4400f, 22f, 300f, 1.9f, 3.8f, 5.8f, 7400f, new Vector3(2.2f, 1.9f, 4.4f), new Color(0.52f, 0.44f, 0.30f)),
            new("Velociraptor",    90,  600f,  2f, 110f, 0.7f, 2.4f, 11.0f, 900f, new Vector3(0.8f, 1.0f, 2.0f), new Color(0.62f, 0.50f, 0.28f)),
            new("Ankylosaurus",   320, 5200f, 30f, 260f, 2.2f, 3.4f, 4.6f, 8200f, new Vector3(2.3f, 1.6f, 4.2f), new Color(0.40f, 0.42f, 0.38f)),
        };

        [MenuItem("Dino Battle/1. Generate Sample Content", priority = 100)]
        public static void Generate()
        {
            EnsureFolder("Assets/GameData");
            EnsureFolder(CreatureDataPath);
            EnsureFolder("Assets/GameData/Rosters");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabPath);

            var definitions = new List<CreatureDefinition>();

            foreach (var blueprint in Blueprints)
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

        private static CreatureDefinition CreateDefinition(Blueprint blueprint, string safeName, GameObject prefab)
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
        private static GameObject CreatePlaceholderPrefab(Blueprint blueprint, string safeName)
        {
            string path = $"{PrefabPath}/Creature_{safeName}.prefab";

            var root = new GameObject($"Creature_{safeName}");

            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2; // Z, so the capsule lies along the body length.
            collider.radius = blueprint.BodySize.x * 0.5f;
            collider.height = Mathf.Max(blueprint.BodySize.z, collider.radius * 2f);
            collider.center = new Vector3(0f, blueprint.BodySize.y * 0.5f, 0f);

            var body = root.AddComponent<Rigidbody>();
            body.mass = blueprint.Mass;
            body.freezeRotation = true;

            root.AddComponent<Health>();
            root.AddComponent<CreatureLocomotion>();
            root.AddComponent<CreatureBrain>();
            var unit = root.AddComponent<CreatureUnit>();
            var attack = root.AddComponent<MeleeAttack>();

            // Visual body
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual_Body";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, blueprint.BodySize.y * 0.5f, 0f);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(blueprint.BodySize.x, blueprint.BodySize.z * 0.5f, blueprint.BodySize.x);
            TintRenderer(visual, blueprint.Tint);

            // Snout marker so it is obvious which way the creature faces.
            var snout = GameObject.CreatePrimitive(PrimitiveType.Cube);
            snout.name = "Visual_Head";
            Object.DestroyImmediate(snout.GetComponent<Collider>());
            snout.transform.SetParent(root.transform, false);
            snout.transform.localPosition = new Vector3(0f, blueprint.BodySize.y * 0.85f, blueprint.BodySize.z * 0.45f);
            snout.transform.localScale = Vector3.one * (blueprint.BodySize.x * 0.55f);
            TintRenderer(snout, blueprint.Tint * 0.7f);

            var aimPoint = new GameObject("AimPoint").transform;
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
            attackSerialized.ApplyModifiedPropertiesWithoutUndo();

            AddHealthBar(root, blueprint);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void AddHealthBar(GameObject root, Blueprint blueprint)
        {
            var bar = new GameObject("HealthBar");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, blueprint.BodySize.y + 1.2f, 0f);

            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Background";
            Object.DestroyImmediate(background.GetComponent<Collider>());
            background.transform.SetParent(bar.transform, false);
            background.transform.localScale = new Vector3(blueprint.BodySize.z * 0.8f, 0.28f, 1f);
            TintRenderer(background, new Color(0.06f, 0.06f, 0.08f));

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
            TintRenderer(fill, new Color(0.25f, 0.85f, 0.35f));

            fillPivot.transform.localScale = new Vector3(blueprint.BodySize.z * 0.78f, 0.22f, 1f);

            var billboard = bar.AddComponent<HealthBarBillboard>();
            var serialized = new SerializedObject(billboard);
            serialized.FindProperty("health").objectReferenceValue = root.GetComponent<Health>();
            serialized.FindProperty("fill").objectReferenceValue = fillPivot.transform;
            serialized.FindProperty("fillRenderer").objectReferenceValue = fill.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TintRenderer(GameObject target, Color color)
        {
            if (!target.TryGetComponent<Renderer>(out var renderer)) return;

            var material = new Material(renderer.sharedMaterial);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Materials");
            string path = AssetDatabase.GenerateUniqueAssetPath($"Assets/Art/Materials/Placeholder_{target.name}.mat");
            AssetDatabase.CreateAsset(material, path);

            renderer.sharedMaterial = material;
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
