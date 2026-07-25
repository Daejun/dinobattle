using DinoBattle.Data;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Lets a big predator seize a much smaller victim in its jaws, lift it off the ground, shake it,
    /// and throw it away. Turns a bite from "numbers go down" into a physical event you can watch.
    ///
    /// Only a large mass advantage allows a grab, so a raptor pack still swarms a T-Rex normally while
    /// the T-Rex can pick a single raptor up. The victim is driven to the jaw each frame rather than
    /// re-parented: re-parenting fights the victim's own Rigidbody and its animator's root, and
    /// leaves ugly cleanup if either creature dies mid-hold.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureUnit))]
    public class GrappleHold : MonoBehaviour
    {
        [Tooltip("Attacker mass must exceed victim mass by at least this factor to seize it.")]
        [Min(1f)]
        [SerializeField] private float massRatioToSeize = 4f;

        [Tooltip("Seconds the victim is held before being thrown clear.")]
        [SerializeField] private float holdDuration = 2.2f;

        [Tooltip("Damage per second applied while held, on top of the bite that started it.")]
        [SerializeField] private float crushDamagePerSecond = 140f;

        [Tooltip("Speed the victim is thrown at on release.")]
        [SerializeField] private float throwSpeed = 9f;

        [Tooltip("How violently the held victim is shaken, in metres of offset.")]
        [SerializeField] private float shakeAmplitude = 0.35f;

        [SerializeField] private float shakeFrequency = 7f;

        [Tooltip("Bone name to hold prey at. Falls back to the aim point when the rig has no such bone.")]
        [SerializeField] private string jawBoneName = "Head";

        [Tooltip("How far in front of the jaw the victim sits, roughly the depth of the mouth.")]
        [SerializeField] private float jawForwardOffset = 0.5f;

        private CreatureUnit self;
        private CreatureUnit victim;
        private Transform jaw;
        private float holdRemaining;
        private float shakePhase;

        /// <summary>Distance from the victim's pivot up to its visual centre, measured when seized.</summary>
        private float victimCenterHeight;

        /// <summary>True while a victim is in this creature's jaws.</summary>
        public bool IsHolding => victim != null;

        private void Awake()
        {
            self = GetComponent<CreatureUnit>();
            jaw = FindJaw();
        }

        /// <summary>
        /// The rig's head bone if it has one, otherwise the aim point. Anchoring to the real bone
        /// means the victim rides the bite animation instead of hovering near an approximation.
        /// </summary>
        private Transform FindJaw()
        {
            if (!string.IsNullOrEmpty(jawBoneName))
            {
                foreach (var bone in GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name.Equals(jawBoneName, System.StringComparison.OrdinalIgnoreCase)) return bone;
                }
            }

            return self.AimPoint;
        }

        /// <summary>Could this creature seize <paramref name="candidate"/> if it landed a bite?</summary>
        public bool CanSeize(CreatureUnit candidate)
        {
            if (IsHolding || candidate == null || candidate.IsDead || self.IsDead) return false;

            CreatureDefinition mine = self.Definition;
            CreatureDefinition theirs = candidate.Definition;
            if (mine == null || theirs == null || theirs.mass <= 0f) return false;

            return mine.mass / theirs.mass >= massRatioToSeize;
        }

        /// <summary>Take <paramref name="prey"/> into the jaws. Call from the attack that connected.</summary>
        public void Seize(CreatureUnit prey)
        {
            if (!CanSeize(prey)) return;

            victim = prey;
            holdRemaining = holdDuration;
            shakePhase = 0f;

            // A creature's pivot is at its feet, so parking that pivot at the jaw leaves the body
            // standing on top of the mouth. Measure where its visual centre actually is and hold
            // THAT at the jaw instead.
            victimCenterHeight = MeasureCenterHeight(prey);

            SetVictimCaptured(true);
        }

        private void LateUpdate()
        {
            if (victim == null) return;

            // Either party dying ends the hold immediately, or the corpse hangs in mid-air.
            if (self.IsDead || victim.IsDead)
            {
                Release(throwClear: false);
                return;
            }

            holdRemaining -= Time.deltaTime;
            shakePhase += Time.deltaTime * shakeFrequency;

            Vector3 shake = jaw.right * (Mathf.Sin(shakePhase) * shakeAmplitude)
                          + jaw.up * (Mathf.Sin(shakePhase * 1.7f) * shakeAmplitude * 0.5f);

            // Held sideways across the jaws and thrashing, the way a predator actually carries prey.
            Quaternion carry = Quaternion.LookRotation(self.transform.right, Vector3.up)
                             * Quaternion.Euler(0f, 0f, Mathf.Sin(shakePhase * 1.3f) * 25f);

            // LateUpdate so this wins over the animator, which has already posed the jaw this frame.
            Vector3 mouth = jaw.position + self.transform.forward * jawForwardOffset + shake;
            victim.transform.SetPositionAndRotation(mouth - Vector3.up * victimCenterHeight, carry);

            victim.Health.TakeDamage(crushDamagePerSecond * Time.deltaTime);

            if (holdRemaining <= 0f) Release(throwClear: true);
        }

        /// <summary>Drop or throw the victim and hand it back to its own AI.</summary>
        public void Release(bool throwClear)
        {
            if (victim == null) return;

            var released = victim;
            victim = null;

            SetVictimCaptured(false, released);

            if (!throwClear || released.IsDead) return;

            if (released.TryGetComponent<Rigidbody>(out var body))
            {
                Vector3 direction = (self.AimPoint.forward + Vector3.up * 0.6f).normalized;
                body.linearVelocity = direction * throwSpeed;
            }
        }

        private void OnDisable()
        {
            // Never leave a victim frozen mid-air because this creature was destroyed or disabled.
            Release(throwClear: false);
        }

        /// <summary>
        /// Height from a creature's pivot to the middle of its visible body, from renderer bounds so
        /// it works for imported models and blocked-out primitives alike.
        /// </summary>
        private static float MeasureCenterHeight(CreatureUnit unit)
        {
            var renderers = unit.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0.5f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            return Mathf.Max(0.1f, bounds.center.y - unit.transform.position.y);
        }

        private void SetVictimCaptured(bool captured, CreatureUnit explicitVictim = null)
        {
            var target = explicitVictim != null ? explicitVictim : victim;
            if (target == null) return;

            foreach (var brain in target.GetComponentsInChildren<CreatureBrain>())
            {
                // A captured creature stops steering; its own death handling still runs.
                if (!target.IsDead) brain.CombatEnabled = !captured;
            }

            if (target.TryGetComponent<Rigidbody>(out var body))
            {
                body.isKinematic = captured;
                if (!captured) body.linearVelocity = Vector3.zero;
            }
        }
    }
}
