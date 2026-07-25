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

        [Tooltip("Fraction of full reach at which a swing may be committed. Below 1 so a creature " +
                 "closes properly before biting rather than snapping at the very edge of its range.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float commitRangeFactor = 0.8f;

        [Tooltip("How much of the target's bulk counts as extra reach, as a multiple of this " +
                 "creature's own range. Caps the credit so a small attacker cannot bite a large " +
                 "target from open ground simply because the target is big.")]
        [Range(0f, 2f)]
        [SerializeField] private float targetExtentCredit = 0.6f;

        [Tooltip("Seconds after damage lands during which the creature stays planted. The attack clip " +
                 "keeps playing past the hit, and it animates the feet standing still — drifting " +
                 "through the rest of it is what makes a fighting creature look like it is skating.")]
        [SerializeField] private float attackRecovery = 0.35f;

        [Tooltip("Impulse applied to the victim on a landed hit. Sells the weight of big dinosaurs.")]
        [SerializeField] private float knockback = 4f;

        [Header("Presentation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string attackTriggerName = "Attack";
        [Tooltip("Shared voice source on the creature root. PlayOneShot layers over any roar in " +
                 "progress instead of cutting it off, which Play() would.")]
        [SerializeField] private AudioSource attackAudio;

        [SerializeField] private AudioClip attackClip;
        [SerializeField] private ParticleSystem impactEffect;

        private float cooldownRemaining;
        private float windupRemaining = -1f;
        private float recoveryRemaining;
        private CreatureUnit self;
        private CreatureUnit pendingTarget;

        public float Range => range;
        public bool IsSwinging => windupRemaining >= 0f;
        public bool IsReady => cooldownRemaining <= 0f && !IsSwinging;

        /// <summary>
        /// Mid-attack: winding up, or in the follow-through before the clip hands back to locomotion.
        /// Callers should keep the creature planted for the whole of it. The attack animation has the
        /// feet stationary, so any translation during it is pure foot-sliding.
        /// </summary>
        public bool IsCommitted => IsSwinging || recoveryRemaining > 0f;

        /// <summary>
        /// Swings started vs. swings that actually dealt damage. A swing plays its animation and its
        /// sound the moment it starts, so every whiff is a bite the player watched connect with no
        /// health lost. These counters exist to make that ratio measurable instead of guessed at.
        /// </summary>
        public int SwingsStarted { get; private set; }

        public int SwingsLanded { get; private set; }

        private void Awake()
        {
            self = GetComponentInParent<CreatureUnit>();

            // The Animator lives on the imported model, which is a CHILD of the creature root — so
            // searching only upward finds nothing and attacks play silently with no animation.
            if (animator == null) animator = GetComponentInParent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
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
            if (recoveryRemaining > 0f) recoveryRemaining -= Time.deltaTime;

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

            // Committing is deliberately tighter than resolving. A swing started at the very edge of
            // reach only had to drift a little during the windup to fall outside the damage check —
            // and every whiff is a bite the player watched land with no health lost. Requiring the
            // creature to close first means normal jostling stays comfortably inside the slack, and
            // it has the side benefit of putting the two bodies nearer for the actual exchange.
            float reach = EffectiveRange(target) * commitRangeFactor;
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

            // Capped against this creature's OWN reach. Uncapped, the credit scaled with whatever it
            // was fighting: a raptor with 1.8 of reach inherited a T-Rex's 3.0 footprint and could
            // bite from 4.8 out, while the T-Rex's flank is only 1.4 from its centre — so the raptor
            // snapped at two units of open ground. A small animal does not get long reach by virtue
            // of attacking something large; it has to close.
            return range + Mathf.Min(targetExtent, range * targetExtentCredit);
        }

        /// <summary>Begin a swing at <paramref name="target"/>. No-op if still on cooldown.</summary>
        public bool TryAttack(CreatureUnit target)
        {
            if (!IsReady || target == null || target.IsDead) return false;

            pendingTarget = target;
            cooldownRemaining = interval;
            windupRemaining = windup;
            SwingsStarted++;

            if (animator != null && !string.IsNullOrEmpty(attackTriggerName))
            {
                animator.SetTrigger(attackTriggerName);
            }

            if (attackAudio != null && attackClip != null) attackAudio.PlayOneShot(attackClip);

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

            // Set before any of the early exits: the follow-through is an animation fact, not a
            // consequence of connecting. A whiffed swing plays exactly the same clip.
            recoveryRemaining = attackRecovery;

            if (target == null || target.IsDead || self == null) return;

            // The target may have walked out during the windup; allow a little slack, then whiff.
            //
            // MUST use the same EffectiveRange that IsInRange used to start the swing. Checking the
            // raw range here instead meant a creature could be close enough to attack but too far to
            // connect: it wound up, swung, and the damage check silently rejected every hit. Fights
            // ran to completion with full health bars on both sides.
            float reach = EffectiveRange(target) + windupRangeSlack;
            Vector3 toTarget = target.transform.position - self.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > reach * reach) return;

            SwingsLanded++;
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
