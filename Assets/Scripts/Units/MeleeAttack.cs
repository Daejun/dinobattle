using DinoBattle.Data;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Bite/claw attack driven off a cooldown. Damage lands after a windup so the hit reads as
    /// connected to the animation rather than snapping off instantly on range entry.
    /// </summary>
    public class MeleeAttack : MonoBehaviour
    {
        [SerializeField] private float damage = 120f;
        [SerializeField] private float interval = 1.4f;
        [SerializeField] private float range = 3.5f;
        [SerializeField] private float windup = 0.35f;

        [Tooltip("Extra range allowed at the moment damage lands, so a target stepping back still gets hit.")]
        [SerializeField] private float windupRangeSlack = 1.5f;

        [Tooltip("Impulse applied to the victim on a landed hit. Sells the weight of big dinosaurs.")]
        [SerializeField] private float knockback = 4f;

        [Header("Presentation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string attackTriggerName = "Attack";
        [SerializeField] private AudioSource attackAudio;
        [SerializeField] private ParticleSystem impactEffect;

        private float cooldownRemaining;
        private float windupRemaining = -1f;
        private CreatureUnit self;
        private CreatureUnit pendingTarget;

        public float Range => range;
        public bool IsSwinging => windupRemaining >= 0f;
        public bool IsReady => cooldownRemaining <= 0f && !IsSwinging;

        private void Awake()
        {
            self = GetComponentInParent<CreatureUnit>();
            if (animator == null) animator = GetComponentInParent<Animator>();
        }

        public void Configure(CreatureDefinition definition)
        {
            if (definition == null) return;

            damage = definition.attackDamage;
            interval = definition.attackInterval;
            range = definition.attackRange;
            windup = Mathf.Min(definition.attackWindup, definition.attackInterval);
        }

        private void Update()
        {
            if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;

            if (!IsSwinging) return;

            windupRemaining -= Time.deltaTime;
            if (windupRemaining > 0f) return;

            windupRemaining = -1f;
            LandHit();
        }

        /// <summary>True if <paramref name="target"/> is close enough to bite right now.</summary>
        public bool IsInRange(CreatureUnit target)
        {
            if (target == null || self == null) return false;
            float sqr = (target.AimPoint.position - self.AimPoint.position).sqrMagnitude;
            return sqr <= range * range;
        }

        /// <summary>Begin a swing at <paramref name="target"/>. No-op if still on cooldown.</summary>
        public bool TryAttack(CreatureUnit target)
        {
            if (!IsReady || target == null || target.IsDead) return false;

            pendingTarget = target;
            cooldownRemaining = interval;
            windupRemaining = windup;

            if (animator != null && !string.IsNullOrEmpty(attackTriggerName))
            {
                animator.SetTrigger(attackTriggerName);
            }

            if (attackAudio != null) attackAudio.Play();

            // Zero windup means the hit should resolve this frame, not next.
            if (windupRemaining <= 0f)
            {
                windupRemaining = -1f;
                LandHit();
            }

            return true;
        }

        private void LandHit()
        {
            var target = pendingTarget;
            pendingTarget = null;

            if (target == null || target.IsDead || self == null) return;

            // The target may have walked out during the windup; allow a little slack, then whiff.
            float reach = range + windupRangeSlack;
            Vector3 toTarget = target.AimPoint.position - self.AimPoint.position;
            if (toTarget.sqrMagnitude > reach * reach) return;

            target.Health.TakeDamage(damage);

            if (knockback > 0f && target.TryGetComponent<Rigidbody>(out var victimBody))
            {
                Vector3 push = toTarget.normalized;
                push.y = 0.15f;
                victimBody.AddForce(push.normalized * knockback, ForceMode.VelocityChange);
            }

            if (impactEffect != null)
            {
                impactEffect.transform.position = target.AimPoint.position;
                impactEffect.Play();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.4f);
            Transform origin = self != null ? self.AimPoint : transform;
            Gizmos.DrawWireSphere(origin.position, range);
        }
    }
}
