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
        private GrappleHold grapple;

        public float Range => range;
        public bool IsSwinging => windupRemaining >= 0f;
        public bool IsReady => cooldownRemaining <= 0f && !IsSwinging;

        private void Awake()
        {
            self = GetComponentInParent<CreatureUnit>();

            // The Animator lives on the imported model, which is a CHILD of the creature root — so
            // searching only upward finds nothing and attacks play silently with no animation.
            if (animator == null) animator = GetComponentInParent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            grapple = GetComponentInParent<GrappleHold>();
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

        /// <summary>
        /// True if <paramref name="target"/> is close enough to bite right now.
        ///
        /// Measured root-to-root on the horizontal plane, NOT between aim points. Aim points sit
        /// forward of each creature, so an attacker circling its prey — facing its direction of
        /// travel rather than the enemy — swung its aim point away and reported out of range while
        /// standing right next to it. Every creature then stalled in Seek and never attacked.
        /// Distance between two bodies should not depend on which way either is looking.
        /// </summary>
        public bool IsInRange(CreatureUnit target)
        {
            if (target == null || self == null) return false;

            Vector3 offset = target.transform.position - self.transform.position;
            offset.y = 0f;

            float reach = EffectiveRange(target);
            return offset.sqrMagnitude <= reach * reach;
        }

        /// <summary>
        /// Reach against a specific target: this creature's own range plus the target's body extent.
        ///
        /// Range alone is centre-to-centre, which quietly makes small attackers unable to hit large
        /// ones. A raptor's 1.8 reach against a T-Rex whose body is five units long required standing
        /// inside it — the colliders forbade that, so the raptors circled a T-Rex forever and never
        /// landed a blow. Reach is properly measured to the target's surface, not its pivot.
        /// </summary>
        public float EffectiveRange(CreatureUnit target)
        {
            float targetExtent = target != null && target.Definition != null
                ? target.Definition.footprintRadius
                : 0f;

            return range + targetExtent;
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
            // Root-to-root for the same reason IsInRange uses it — see that method.
            float reach = range + windupRangeSlack;
            Vector3 toTarget = target.transform.position - self.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > reach * reach) return;

            target.Health.TakeDamage(damage);

            // A connecting bite on something far smaller becomes a grab: the victim is lifted, shaken
            // and thrown rather than just losing hit points. Knockback is skipped in that case — you
            // cannot both hold prey and punt it away.
            if (grapple != null && grapple.CanSeize(target))
            {
                grapple.Seize(target);
            }
            else if (knockback > 0f && target.TryGetComponent<Rigidbody>(out var victimBody))
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
