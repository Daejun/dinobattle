using DinoBattle.Data;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// The autonomous fighter AI. This is the whole point of a spectator simulator: the player
    /// places creatures, presses Start, and this state machine does the fighting.
    ///
    /// Idle -> Seek (walk at nearest enemy) -> Attack (in range) -> back to Seek when the target dies.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureUnit))]
    public class CreatureBrain : MonoBehaviour
    {
        public enum State { Idle, Seek, Attack, Dead }

        [Tooltip("Seconds between target re-evaluations. Staggered per creature to spread the cost.")]
        [SerializeField] private float retargetInterval = 0.4f;

        [Tooltip("Close to this fraction of attack range, which is a root-to-root distance. Under 1 " +
                 "so bodies actually meet and overlap slightly instead of stopping at arm's length.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float approachRangeFactor = 0.8f;

        [Tooltip("Degrees around the target this creature approaches from. Randomised per creature so " +
                 "a pack surrounds its prey instead of all piling onto the nearest face.")]
        [SerializeField] private float maxFlankAngle = 70f;

        [Tooltip("Strafing speed while circling, as a fraction of full move speed.")]
        [Range(0f, 1f)]
        [SerializeField] private float circleSpeedFactor = 0.45f;

        [Header("Steering")]
        [Tooltip("Neighbours inside this radius push this creature away. Reynolds separation — the " +
                 "term that stops a pack collapsing onto a single point.")]
        [SerializeField] private float separationRadius = 3.5f;

        [Tooltip("Weight of separation while closing in. Spreads a pack across the approach so it " +
                 "arrives on several flanks instead of in single file.")]
        [Range(0f, 3f)]
        [SerializeField] private float separationWeight = 1.1f;

        [Tooltip("Weight of separation once in contact. Near zero on purpose: in the reference game " +
                 "attackers pile onto their target and interpenetrate heavily, and keeping full " +
                 "separation here is what held them at a polite distance mid-fight.")]
        [Range(0f, 3f)]
        [SerializeField] private float meleeSeparationWeight = 0.15f;

        [Tooltip("Distance at which Arrive starts easing off, so attackers settle instead of " +
                 "overshooting and oscillating.")]
        [SerializeField] private float slowingRadius = 6f;

        private float flankAngle;
        private float circleDirection = 1f;

        [SerializeField] private Animator animator;
        [SerializeField] private string speedParameterName = "Speed";
        [SerializeField] private string deathTriggerName = "Die";

        private CreatureUnit self;
        private CreatureLocomotion locomotion;
        private MeleeAttack attack;
        private PounceCling pounce;
        private CreatureDefinition definition;

        private CreatureUnit target;
        private float retargetTimer;

        public State Current { get; private set; } = State.Idle;
        public CreatureUnit Target => target;

        /// <summary>Set false during placement so nothing moves until the fight starts.</summary>
        public bool CombatEnabled { get; set; }

        private void Awake()
        {
            self = GetComponent<CreatureUnit>();
            locomotion = GetComponent<CreatureLocomotion>();
            attack = GetComponentInChildren<MeleeAttack>();
            pounce = GetComponent<PounceCling>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Offset the first retarget tick so a hundred creatures do not all scan on the same frame.
            retargetTimer = Random.Range(0f, retargetInterval);

            // Fixed per creature, not per frame: a flank angle that jittered every tick would make
            // the approach wander instead of committing to one side.
            flankAngle = Random.Range(-maxFlankAngle, maxFlankAngle);
            circleDirection = Random.value < 0.5f ? -1f : 1f;
        }

        private void Start()
        {
            definition = self.Definition;
        }

        private void Update()
        {
            if (Current == State.Dead) return;

            if (!CombatEnabled)
            {
                SetState(State.Idle);
                UpdateAnimator();
                return;
            }

            retargetTimer -= Time.deltaTime;
            if (target == null || target.IsDead || retargetTimer <= 0f)
            {
                retargetTimer = retargetInterval;
                AcquireTarget();
            }

            if (target == null)
            {
                SetState(State.Idle);
                UpdateAnimator();
                return;
            }

            TickCombat();
            UpdateAnimator();
        }

        private void AcquireTarget()
        {
            float aggro = definition != null ? definition.aggroRange : 80f;
            var nearest = UnitRegistry.FindNearestEnemy(self, aggro);

            // Keep the current target unless it is gone — constant switching makes fights look indecisive.
            if (target == null || target.IsDead) target = nearest;
        }

        /// <summary>
        /// Movement is a weighted blend of Reynolds steering behaviours rather than a single Seek.
        /// Pursue/Arrive supplies the intent, Separation keeps the pack from collapsing into one
        /// point, and in melee a tangential term makes attackers circle instead of standing still.
        /// </summary>
        private void TickCombat()
        {
            bool inRange = attack != null && attack.IsInRange(target);
            float maxSpeed = locomotion != null ? locomotion.MoveSpeed : 6f;
            // Effective range accounts for the target's body extent, so a small attacker closing on a
            // large one aims for its surface rather than a point buried inside it.
            float fightDistance = (attack != null ? attack.EffectiveRange(target) : 3f) * approachRangeFactor;

            Vector3 separation = SteeringBehaviors.Separation(self, separationRadius, maxSpeed);

            // Riding a much larger enemy: the cling does the damage, so there is nothing to steer.
            if (pounce != null && pounce.IsClinging)
            {
                SetState(State.Attack);
                return;
            }

            // Small creatures climb what they cannot outfight head-on, rather than nibbling its ankles.
            if (pounce != null && pounce.CanPounce(target) && pounce.TryPounce(target))
            {
                SetState(State.Attack);
                return;
            }

            if (inRange)
            {
                SetState(State.Attack);

                if (locomotion != null)
                {
                    // Rooted while committing to a swing; circling between them. Braking throughout is
                    // what made fights look like two statues trading damage.
                    Vector3 desired = attack.IsSwinging || attack.IsReady
                        ? Vector3.zero
                        : SteeringBehaviors.Blend(maxSpeed,
                            (TangentialVelocity(fightDistance, maxSpeed), 1f),
                            (separation, meleeSeparationWeight));

                    if (desired == Vector3.zero) locomotion.Brake();

                    // Facing is pinned to the enemy so a strafing creature still bites forward.
                    locomotion.Steer(desired, target.AimPoint.position);
                }

                attack.TryAttack(target);
                return;
            }

            SetState(State.Seek);
            if (locomotion == null) return;

            // Approach a point offset around the target, so converging attackers spread across its
            // flanks rather than queuing up nose-to-tail on the near side.
            Vector3 anchor = FlankPosition(fightDistance);
            Vector3 targetVelocity = target.GetComponent<CreatureLocomotion>() is { } targetLocomotion
                ? targetLocomotion.HorizontalVelocity
                : Vector3.zero;

            Vector3 pursue = SteeringBehaviors.Pursue(
                transform.position, anchor, targetVelocity, maxSpeed, slowingRadius);

            locomotion.Steer(SteeringBehaviors.Blend(maxSpeed,
                (pursue, 1f),
                (separation, separationWeight)));
        }

        /// <summary>
        /// Sideways velocity around the target, plus a radial correction that holds the fighting
        /// distance. Produces a circling orbit instead of a drift that slowly loses contact.
        /// </summary>
        private Vector3 TangentialVelocity(float fightDistance, float maxSpeed)
        {
            Vector3 toSelf = transform.position - target.transform.position;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude < 0.0001f) return Vector3.zero;

            float distance = toSelf.magnitude;
            Vector3 radial = toSelf / distance;
            Vector3 tangent = Vector3.Cross(Vector3.up, radial) * circleDirection;

            // Positive when too far out, negative when too close: pulls back to fightDistance.
            float correction = Mathf.Clamp((fightDistance - distance) / Mathf.Max(0.5f, fightDistance), -1f, 1f);

            return (tangent + radial * correction).normalized * (maxSpeed * circleSpeedFactor);
        }

        /// <summary>
        /// A point <paramref name="stopDistance"/> from the target, rotated by this creature's own
        /// flank offset. Attackers converge on different sides instead of stacking on the near face.
        /// </summary>
        private Vector3 FlankPosition(float stopDistance)
        {
            Vector3 toSelf = transform.position - target.transform.position;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude < 0.0001f) toSelf = -transform.forward;

            Quaternion offset = Quaternion.Euler(0f, flankAngle, 0f);
            return target.transform.position + offset * toSelf.normalized * stopDistance;
        }

        private void SetState(State next)
        {
            if (Current == next) return;
            Current = next;
        }

        private void UpdateAnimator()
        {
            if (animator == null || string.IsNullOrEmpty(speedParameterName)) return;
            if (locomotion == null) return;

            float normalized = definition != null && definition.moveSpeed > 0f
                ? locomotion.CurrentSpeed / definition.moveSpeed
                : 0f;

            animator.SetFloat(speedParameterName, normalized);
        }

        public void OnUnitDied()
        {
            Current = State.Dead;
            target = null;
            CombatEnabled = false;

            if (animator != null && !string.IsNullOrEmpty(deathTriggerName))
            {
                // Clear Speed too, or the death clip blends against a stale locomotion value.
                if (!string.IsNullOrEmpty(speedParameterName)) animator.SetFloat(speedParameterName, 0f);
                animator.SetTrigger(deathTriggerName);
            }
        }
    }
}
