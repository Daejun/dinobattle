using DinoBattle.Core;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Body-to-body impacts: when two creatures run into each other, both get thrown.
    ///
    /// This deliberately does NOT use OnCollisionEnter. The physics capsules are tiny on purpose —
    /// roughly a quarter of the visible body — because a collider sized to the real silhouette holds
    /// attackers a body-width apart and makes bites look like they land from thin air. The cost of
    /// that choice is that two creatures fighting at melee range never actually touch: measured in a
    /// live battle, the closest opposing pair of capsules was 1.22 units apart, and 600 frames of
    /// fighting produced exactly one collision event. Waiting on the solver would mean waiting
    /// forever.
    ///
    /// So contact is judged against the creature's FOOTPRINT — the size you can see — while the
    /// physics collider keeps doing its narrower job of spacing and support.
    ///
    /// Two things then have to happen for the hit to be visible at all. An impulse worth seeing, and
    /// a moment where the steering stops overwriting it: the brain rewrites velocity every physics
    /// step, braking to a stop in melee and driving straight back at the target otherwise, so any
    /// push not protected by a stagger is gone before the next frame draws.
    ///
    /// Mass decides who moves. The impulse is split by the mass ratio, so a raptor that runs into a
    /// charging T-Rex is thrown clear while the T-Rex barely breaks stride — which is the point of
    /// having mass in the stat block at all.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureUnit))]
    [RequireComponent(typeof(Rigidbody))]
    public class CreatureImpact : MonoBehaviour
    {
        [Tooltip("Closing speed below which this is two animals leaning on each other, not a collision. " +
                 "This threshold is what keeps a melee exchange clean: creatures brake before they " +
                 "swing, so a stationary duel never trips it and only a genuine charge does.")]
        [SerializeField] private float minimumImpactSpeed = 3.5f;

        [Tooltip("Fraction of the two footprints that counts as contact. Below 1 so the shove fires " +
                 "when the bodies actually meet rather than when their radii first graze.")]
        [Range(0.3f, 1.2f)]
        [SerializeField] private float contactFraction = 0.75f;

        [Tooltip("Strength of the shove, as a multiplier on closing speed.")]
        [SerializeField] private float shoveStrength = 1.6f;

        [Tooltip("Upward part of the shove. A purely horizontal push slides creatures apart; a little " +
                 "lift makes it read as being knocked back rather than pushed along the floor.")]
        [SerializeField] private float shoveLift = 0.28f;

        [Tooltip("Seconds of lost footing after a solid hit, during which steering does not drive this " +
                 "creature. This is what lets the shove survive long enough to be seen. " +
                 "Kept short because it is downtime, and downtime is a balance lever whether or not " +
                 "it was meant as one: at 0.35 the boss battle went from an even 5-7 split to 11-1 " +
                 "for the boss, purely because a 60-tonne creature knocks its attackers off their " +
                 "feet and is never knocked off its own.")]
        [SerializeField] private float staggerDuration = 0.22f;

        [Tooltip("Hardest shove any single impact may apply. Stops a pile-up from launching anyone.")]
        [SerializeField] private float maximumShove = 12f;

        [Tooltip("Seconds before this creature can be shoved again. Without it a charge that ends in " +
                 "an overlap re-triggers every tick and the pair vibrates instead of colliding once. " +
                 "Long enough to cover a full break-off-and-charge-again cycle, so a creature fighting " +
                 "something far heavier is body-checked once per approach rather than continuously.")]
        [SerializeField] private float impactCooldown = 1.4f;

        [Tooltip("Seconds between contact scans. Impacts are not frame-critical and this runs on every " +
                 "creature, so it is staggered rather than checked every physics step.")]
        [SerializeField] private float scanInterval = 0.06f;

        private CreatureUnit self;
        private CreatureLocomotion locomotion;
        private Rigidbody body;

        private float scanTimer;
        private float cooldownRemaining;

        private void Awake()
        {
            self = GetComponent<CreatureUnit>();
            locomotion = GetComponent<CreatureLocomotion>();
            body = GetComponent<Rigidbody>();

            // Stagger the first scan so a whole army spawned on the same frame does not scan in lockstep.
            scanTimer = Random.Range(0f, scanInterval);
        }

        private void FixedUpdate()
        {
            if (cooldownRemaining > 0f) cooldownRemaining -= Time.fixedDeltaTime;

            scanTimer -= Time.fixedDeltaTime;
            if (scanTimer > 0f) return;
            scanTimer = scanInterval;

            if (cooldownRemaining > 0f) return;
            if (self == null || self.IsDead || body == null || body.isKinematic) return;
            if (BattleManager.Instance == null || BattleManager.Instance.Phase != BattlePhase.Fighting) return;

            ScanTeam(Team.Red);
            ScanTeam(Team.Blue);
        }

        private void ScanTeam(Team team)
        {
            var others = UnitRegistry.AliveOf(team);
            if (others == null) return;

            float myRadius = FootprintOf(self);

            for (int i = 0; i < others.Count; i++)
            {
                CreatureUnit other = others[i];
                if (other == null || other == self || other.IsDead) continue;

                Vector3 toOther = other.transform.position - transform.position;
                toOther.y = 0f;

                float contact = (myRadius + FootprintOf(other)) * contactFraction;
                float sqrDistance = toOther.sqrMagnitude;
                if (sqrDistance > contact * contact || sqrDistance < 0.0001f) continue;

                if (!other.TryGetComponent<Rigidbody>(out var theirBody)) continue;

                // Speed along the line between them, positive when they are closing. Two creatures
                // circling each other at melee range are not colliding no matter how fast they move.
                Vector3 axis = toOther.normalized;
                Vector3 relative = body.linearVelocity - theirBody.linearVelocity;
                relative.y = 0f;
                float closingSpeed = Vector3.Dot(relative, axis);
                if (closingSpeed < minimumImpactSpeed) continue;

                Shove(-axis, closingSpeed, theirBody.mass);
                return;
            }
        }

        private void Shove(Vector3 away, float closingSpeed, float theirMass)
        {
            // Share of the impact this creature absorbs. A light creature takes nearly all of it.
            float myMass = Mathf.Max(1f, body.mass);
            float share = Mathf.Max(1f, theirMass) / (myMass + Mathf.Max(1f, theirMass));

            Vector3 shove = (away + Vector3.up * shoveLift).normalized
                          * Mathf.Min(closingSpeed * shoveStrength * share * 2f, maximumShove);

            body.AddForce(shove, ForceMode.VelocityChange);
            cooldownRemaining = impactCooldown;

            // Scale the stagger by how one-sided the hit was: the creature that got run over loses its
            // footing, the one doing the running barely breaks stride.
            if (locomotion != null) locomotion.Stagger(staggerDuration * share * 2f);
        }

        /// <summary>
        /// The visible body radius, from the definition rather than the collider. Falls back to a
        /// modest guess for anything spawned without a definition, such as a placement preview.
        /// </summary>
        private static float FootprintOf(CreatureUnit unit)
        {
            var definition = unit.Definition;
            return definition != null ? Mathf.Max(0.5f, definition.footprintRadius) : 1f;
        }
    }
}
