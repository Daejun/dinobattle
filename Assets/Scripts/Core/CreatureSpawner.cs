using System.Collections.Generic;
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

        [Tooltip("Parent for the inert placement-phase models. Kept separate from the live container " +
                 "so clearing one can never take the other with it.")]
        [SerializeField] private Transform previewContainer;

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

            if (previewContainer == null)
            {
                previewContainer = new GameObject("Placement Previews").transform;
                previewContainer.SetParent(transform, false);
            }
        }

        /// <summary>
        /// Spawn one placement. The scales are the gauntlet's per-tier difficulty and default to 1,
        /// so versus mode and every existing caller are untouched.
        /// </summary>
        public CreatureUnit Spawn(PlacedCreature placement, float healthScale = 1f, float damageScale = 1f)
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

            unit.Initialize(definition, placement.Team, ColorFor(placement.Team), healthScale, damageScale);
            return unit;
        }

        /// <summary>
        /// Show the pending arrangement as actual creatures standing in the arena.
        ///
        /// Until this existed, the placement screen was an empty field: units were only created on
        /// Start, so a player picked a dinosaur and nothing appeared. It also hid the fact that
        /// tapping the ground places one at all — a playtester concluded the game had no manual
        /// placement, when what it had was no feedback.
        ///
        /// Rebuilt wholesale on every change. The list is at most a handful of entries and only ever
        /// changes on a deliberate player action, so tracking individual adds and removals would be
        /// bookkeeping with no payoff.
        /// </summary>
        public void ShowPreviews(IReadOnlyList<PlacedCreature> placements)
        {
            ClearPreviews();

            if (placements == null) return;

            foreach (var placement in placements)
            {
                var definition = placement.Definition;
                if (definition == null || definition.prefab == null) continue;

                var instance = Instantiate(
                    definition.prefab,
                    placement.Position,
                    Quaternion.Euler(0f, placement.YawDegrees, 0f),
                    previewContainer);

                // Before anything else this frame: an uninitialised creature registers itself in
                // Start, and a preview must never appear in a targeting query.
                if (instance.TryGetComponent<CreatureUnit>(out var unit))
                {
                    unit.MarkAsPreview();
                    unit.ApplyPreviewTeamColor(ColorFor(placement.Team));
                }
            }
        }

        public void ClearPreviews()
        {
            if (previewContainer == null) return;

            for (int i = previewContainer.childCount - 1; i >= 0; i--)
            {
                var child = previewContainer.GetChild(i);

                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
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
