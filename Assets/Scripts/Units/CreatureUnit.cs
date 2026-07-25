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
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public class CreatureUnit : MonoBehaviour
    {
        [SerializeField] private Team team = Team.Red;
        [SerializeField] private CreatureDefinition definition;

        [Tooltip("Chest/head transform used for range checks and damage origin. Falls back to the root.")]
        [SerializeField] private Transform aimPoint;

        [Tooltip("Optional renderers nudged toward the team color. Leave empty to keep the model's " +
                 "own colors untouched — the team ring already identifies the side.")]
        [SerializeField] private Renderer[] teamTintRenderers;

        [Tooltip("How far listed renderers are pushed toward the team color. 0 = natural colors only.")]
        [Range(0f, 1f)]
        [SerializeField] private float teamTintStrength;


        [Tooltip("Seconds the corpse stays in the arena after dying. Negative keeps it forever.")]
        [SerializeField] private float corpseLifetime = -1f;

        [Header("Audio")]
        [SerializeField] private AudioSource voice;
        [SerializeField] private AudioClip deathClip;

        [Tooltip("Played occasionally while alive and fighting, so a battle is not silent between hits.")]
        [SerializeField] private AudioClip roarClip;

        [SerializeField] private Vector2 roarIntervalRange = new(6f, 14f);

        private float roarTimer;

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

            // Staggered, or an army spawned on the same frame all calls out in unison.
            roarTimer = UnityEngine.Random.Range(0f, roarIntervalRange.y);
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

        private void Update()
        {
            if (voice == null || roarClip == null || IsDead) return;

            roarTimer -= Time.deltaTime;
            if (roarTimer > 0f) return;

            roarTimer = UnityEngine.Random.Range(roarIntervalRange.x, roarIntervalRange.y);

            // Never on top of an existing call from this creature, or a big pack turns into a drone.
            if (!voice.isPlaying) voice.PlayOneShot(roarClip, 0.7f);
        }

        private void HandleDied()
        {
            // Leave the registry right away so nothing keeps chasing a corpse.
            Deregister();

            if (voice != null && deathClip != null) voice.PlayOneShot(deathClip);

            Died?.Invoke(this);

            foreach (var brain in GetComponentsInChildren<CreatureBrain>()) brain.OnUnitDied();
            foreach (var locomotion in GetComponentsInChildren<CreatureLocomotion>()) locomotion.OnUnitDied();

            if (corpseLifetime >= 0f) Destroy(gameObject, corpseLifetime);
        }

        /// <summary>
        /// Show team allegiance without destroying the creature's own colouring.
        ///
        /// This used to overwrite _BaseColor outright, which painted every dinosaur flat red or blue
        /// and threw away the species colours the art defines. Instead the body keeps its natural hue
        /// with only a slight push toward the team colour, and the unambiguous signal moves to a
        /// coloured ring on the ground — the same trick RTS games use for exactly this reason.
        /// </summary>
        private void ApplyTeamTint(Color color)
        {
            var ring = transform.Find(CreatureRig.TeamRing);
            if (ring != null && ring.TryGetComponent<Renderer>(out var ringRenderer))
            {
                SetRendererColor(ringRenderer, color);
            }

            if (teamTintStrength <= 0f) return;

            // Only explicitly listed renderers get tinted. With none listed the model is left alone,
            // which is the default: the ring already carries the team read.
            if (teamTintRenderers == null) return;

            foreach (var renderer in teamTintRenderers)
            {
                if (renderer == null) continue;

                foreach (var material in renderer.materials)
                {
                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", Color.Lerp(material.GetColor("_BaseColor"), color, teamTintStrength));
                    }
                    else if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", Color.Lerp(material.GetColor("_Color"), color, teamTintStrength));
                    }
                }
            }
        }

        private static void SetRendererColor(Renderer renderer, Color color)
        {
            // renderer.material instances the shared asset, so one creature's ring does not recolour
            // every other creature that shares the material.
            var material = renderer.material;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }
    }
}
