using DinoBattle.Data;
using DinoBattle.Units;
using UnityEngine;

namespace DinoBattle.Core
{
    /// <summary>Turns <see cref="PlacedCreature"/> records into live creatures in the arena.</summary>
    public class CreatureSpawner : MonoBehaviour
    {
        [Header("Team colors")]
        [SerializeField] private Color redColor = new(0.85f, 0.22f, 0.18f);
        [SerializeField] private Color blueColor = new(0.20f, 0.45f, 0.90f);

        [Tooltip("Creatures are dropped this far above the ground so they settle onto it.")]
        [SerializeField] private float spawnHeightOffset = 0.5f;

        [Tooltip("Parent for spawned creatures. Created automatically if left empty.")]
        [SerializeField] private Transform container;

        public Color ColorFor(Team team) => team switch
        {
            Team.Red => redColor,
            Team.Blue => blueColor,
            _ => Color.gray
        };

        private void Awake()
        {
            if (container == null)
            {
                container = new GameObject("Spawned Creatures").transform;
                container.SetParent(transform, false);
            }
        }

        public CreatureUnit Spawn(PlacedCreature placement)
        {
            var definition = placement.Definition;
            if (definition == null || definition.prefab == null)
            {
                Debug.LogWarning($"[CreatureSpawner] Skipping placement with no prefab: {definition?.name}");
                return null;
            }

            Vector3 position = placement.Position + Vector3.up * spawnHeightOffset;
            Quaternion rotation = Quaternion.Euler(0f, placement.YawDegrees, 0f);

            var instance = Instantiate(definition.prefab, position, rotation, container);

            var unit = instance.GetComponent<CreatureUnit>();
            if (unit == null)
            {
                Debug.LogError($"[CreatureSpawner] Prefab '{definition.prefab.name}' has no CreatureUnit at its root.");
                Destroy(instance);
                return null;
            }

            unit.Initialize(definition, placement.Team, ColorFor(placement.Team));
            return unit;
        }

        /// <summary>Remove everything spawned so far. Called when a match is reset.</summary>
        public void DespawnAll()
        {
            if (container == null) return;

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);

                // Deactivate, detach, THEN destroy. Destroy() only takes effect at the end of the
                // frame — and in practice creatures despawned this way were still standing in the
                // scene many seconds later, cluttering the arena and confusing every headcount.
                // Deactivating makes the removal immediate and unconditional: the object stops
                // rendering, stops updating, and OnDisable pulls it out of the UnitRegistry, whether
                // or not the destroy lands promptly.
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }
    }
}
