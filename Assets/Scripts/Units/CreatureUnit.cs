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

        [Tooltip("Alpha of the team ring on the ground. Needs the ring material on a shader that " +
                 "actually blends — Unlit/Color discards alpha and stays fully opaque.")]
        [Range(0f, 1f)]
        [SerializeField] private float teamRingOpacity = 0.2f;

        [Tooltip("How far this individual's hue may drift from the species palette. Small: enough " +
                 "that two of the same species are not clones, not so much that a Triceratops turns up purple.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float skinHueVariation = 0.04f;

        [Tooltip("How far this individual's brightness may drift. Does more visible work than hue — " +
                 "a paler and a darker animal of the same species read apart instantly.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float skinValueVariation = 0.18f;


        [Tooltip("Seconds the corpse stays in the arena after dying. Negative keeps it forever.")]
        [SerializeField] private float corpseLifetime = -1f;

        [Header("Audio")]
        [SerializeField] private AudioSource voice;
        [SerializeField] private AudioClip deathClip;

        [Tooltip("Played occasionally while alive and fighting, so a battle is not silent between hits.")]
        [SerializeField] private AudioClip roarClip;

        [SerializeField] private Vector2 roarIntervalRange = new(6f, 14f);

        private float roarTimer;
        private CreatureBrain brain;
        private CreatureLocomotion locomotion;
        private MeleeAttack attack;

        /// <summary>Shader property both the skin shader and the built-in Standard shader expose.</summary>
        private static readonly int BaseColorId = Shader.PropertyToID("_Color");

        public Team Team => team;
        public CreatureDefinition Definition => definition;

        /// <summary>
        /// Cached siblings, so callers that need them every frame do not pay for a lookup.
        ///
        /// The camera director asks every living creature whether it is engaged, several times per
        /// frame, and each question used to be a GetComponent — a few hundred lookups a frame with a
        /// full arena, for components that never change. Both may be null: a creature is allowed to
        /// exist without a brain or a body.
        /// </summary>
        public CreatureBrain Brain => brain;

        public CreatureLocomotion Locomotion => locomotion;

        /// <summary>This creature's weapon, so others can read whether it is mid-swing at them.</summary>
        public MeleeAttack Attack => attack;
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
            brain = GetComponent<CreatureBrain>();
            locomotion = GetComponent<CreatureLocomotion>();
            attack = GetComponentInChildren<MeleeAttack>();

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
            ApplyIndividualVariation();
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

            // Only while there is a fight on. The timer used to run off nothing but IsDead, so the
            // last creature standing carried on roaring over the result screen, and a placement
            // screen full of creatures the player had not yet sent into battle was equally noisy.
            if (brain != null && !brain.CombatEnabled) return;

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
                // Faint on purpose. At full opacity a hundred solid discs read as a red half and a
                // blue half of the arena rather than as ground the dinosaurs are standing on. The
                // marker only has to answer "whose side is this", so it can be nearly transparent.
                SetRendererColor(ringRenderer, new Color(color.r, color.g, color.b, teamRingOpacity));
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

        /// <summary>
        /// Nudge this individual's skin off the species palette.
        ///
        /// Without it every Velociraptor in a pack is pixel-identical, which reads as copy-paste
        /// rather than as a group of animals. Applied through a MaterialPropertyBlock rather than by
        /// touching renderer.material: instancing a material per creature would multiply the material
        /// count by the size of the army for what is a single colour change.
        ///
        /// Rolled once at spawn and never again — a creature that shimmered through hues as it
        /// walked would be far worse than clones.
        /// </summary>
        private void ApplyIndividualVariation()
        {
            var model = transform.Find(CreatureRig.ModelVisual);
            if (model == null) return;

            float hueShift = UnityEngine.Random.Range(-skinHueVariation, skinHueVariation);
            float valueScale = 1f + UnityEngine.Random.Range(-skinValueVariation, skinValueVariation);

            var block = new MaterialPropertyBlock();

            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !materials[i].HasProperty(BaseColorId)) continue;

                    renderer.GetPropertyBlock(block, i);
                    block.SetColor(BaseColorId, Vary(materials[i].GetColor(BaseColorId), hueShift, valueScale));
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }

        private static Color Vary(Color source, float hueShift, float valueScale)
        {
            Color.RGBToHSV(source, out float h, out float s, out float v);

            // Hue wraps, so a shift off either end of the wheel stays a valid colour.
            h = Mathf.Repeat(h + hueShift, 1f);
            v = Mathf.Clamp01(v * valueScale);

            return Color.HSVToRGB(h, s, v);
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
