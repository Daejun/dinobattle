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
    [RequireComponent(typeof(CreatureUnit))]
    public class CreatureBrain : MonoBehaviour
    {
        public enum State { Idle, Seek, Attack, Dead }

        [Tooltip("Seconds between target re-evaluations. Staggered per creature to spread the cost.")]
        [SerializeField] private float retargetInterval = 0.4f;

        [Tooltip("Stop approaching at this fraction of attack range so creatures do not shove into each other.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float approachRangeFactor = 0.9f;

        [SerializeField] private Animator animator;
        [SerializeField] private string speedParameterName = "Speed";

        private CreatureUnit self;
        private CreatureLocomotion locomotion;
        private MeleeAttack attack;
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
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Offset the first retarget tick so a hundred creatures do not all scan on the same frame.
            retargetTimer = Random.Range(0f, retargetInterval);
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

        private void TickCombat()
        {
            bool inRange = attack != null && attack.IsInRange(target);

            if (inRange)
            {
                SetState(State.Attack);
                locomotion?.FaceTowards(target.AimPoint.position);
                locomotion?.Brake();
                attack.TryAttack(target);
                return;
            }

            SetState(State.Seek);

            if (locomotion == null) return;

            // Aim for the edge of our reach rather than the target's pivot.
            float stopDistance = (attack != null ? attack.Range : 3f) * approachRangeFactor;
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;

            Vector3 destination = toTarget.magnitude <= stopDistance
                ? transform.position
                : target.transform.position - toTarget.normalized * stopDistance;

            locomotion.MoveTowards(destination);
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
        }
    }
}
