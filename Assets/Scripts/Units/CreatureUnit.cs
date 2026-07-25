using System;
using DinoBattle.Core;
using DinoBattle.Data;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Root component on every creature prefab. Owns team identity, wires the stat block from the
    /// definition, and keeps the <see cref="UnitRegistry"/> in sync.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class CreatureUnit : MonoBehaviour
    {
        [SerializeField] private Team team = Team.Red;
        [SerializeField] private CreatureDefinition definition;

        [Tooltip("Chest/head transform used for range checks and damage origin. Falls back to the root.")]
        [SerializeField] private Transform aimPoint;

        [Tooltip("Renderers tinted to the team color on spawn. Leave empty to tint every child renderer.")]
        [SerializeField] private Renderer[] teamTintRenderers;

        [Tooltip("Seconds the corpse stays in the arena after dying. Negative keeps it forever.")]
        [SerializeField] private float corpseLifetime = -1f;

        public Team Team => team;
        public CreatureDefinition Definition => definition;
        public Health Health { get; private set; }
        public bool IsDead => Health == null || Health.IsDead;
        public Transform AimPoint => aimPoint != null ? aimPoint : transform;

        /// <summary>Raised when this creature dies. The battle manager uses it to check the win condition.</summary>
        public event Action<CreatureUnit> Died;

        private bool registered;
        private bool initialized;

        private void Awake()
        {
            Health = GetComponent<Health>();
        }

        /// <summary>
        /// Called by the spawner immediately after Instantiate, before the object is enabled for the
        /// first Update. Stats always flow definition -> components so the prefab stays a dumb visual.
        /// </summary>
        public void Initialize(CreatureDefinition creatureDefinition, Team assignedTeam, Color teamColor)
        {
            // Drop out of the old team's list before the team changes, or Unregister would later
            // search the wrong list and leave a phantom entry behind.
            if (registered)
            {
                UnitRegistry.Unregister(this);
                registered = false;
            }

            definition = creatureDefinition;
            team = assignedTeam;
            initialized = true;

            if (Health == null) Health = GetComponent<Health>();
            if (definition != null) Health.Configure(definition.maxHealth, definition.armor);

            foreach (var locomotion in GetComponentsInChildren<CreatureLocomotion>())
            {
                locomotion.Configure(definition);
            }

            foreach (var attack in GetComponentsInChildren<MeleeAttack>())
            {
                attack.Configure(definition);
            }

            ApplyTeamTint(teamColor);
            gameObject.name = $"{(definition != null ? definition.displayName : "Creature")} [{team}]";

            EnsureRegistered();
        }

        private void OnEnable()
        {
            Health.Died += HandleDied;

            // OnEnable fires during Instantiate, before the spawner has assigned a team, so only
            // register here for creatures that are already initialized (scene-authored, or re-enabled).
            if (initialized) EnsureRegistered();
        }

        private void Start()
        {
            // A creature dropped straight into the scene never gets Initialize called; trust its
            // serialized team and stat block instead of sitting invisible to every targeting query.
            if (initialized) return;

            initialized = true;
            if (definition != null) Health.Configure(definition.maxHealth, definition.armor);
            EnsureRegistered();
        }

        private void OnDisable()
        {
            Health.Died -= HandleDied;
            Deregister();
        }

        private void EnsureRegistered()
        {
            if (registered || IsDead) return;

            UnitRegistry.Register(this);
            registered = true;
        }

        private void Deregister()
        {
            if (!registered) return;

            UnitRegistry.Unregister(this);
            registered = false;
        }

        private void HandleDied()
        {
            // Leave the registry right away so nothing keeps chasing a corpse.
            Deregister();
            Died?.Invoke(this);

            foreach (var brain in GetComponentsInChildren<CreatureBrain>()) brain.OnUnitDied();
            foreach (var locomotion in GetComponentsInChildren<CreatureLocomotion>()) locomotion.OnUnitDied();

            if (corpseLifetime >= 0f) Destroy(gameObject, corpseLifetime);
        }

        private void ApplyTeamTint(Color color)
        {
            var targets = teamTintRenderers != null && teamTintRenderers.Length > 0
                ? teamTintRenderers
                : GetComponentsInChildren<Renderer>();

            foreach (var renderer in targets)
            {
                if (renderer == null) continue;

                // Instance the material so tinting one creature does not recolor the whole team's shared asset.
                foreach (var material in renderer.materials)
                {
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                    else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                }
            }
        }
    }
}
