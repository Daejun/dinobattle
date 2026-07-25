using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Lets a small fast creature leap onto something far bigger and ride it, biting the whole time.
    ///
    /// The mirror of <see cref="GrappleHold"/>: that one is a heavyweight seizing prey, this one is
    /// prey swarming a heavyweight. Together they make a size mismatch play out physically instead
    /// of as two health bars draining at different rates — a raptor pack climbs a T-Rex, the T-Rex
    /// plucks one off and throws it, and the rest keep clinging.
    ///
    /// The host shakes clingers off after a while, which caps how long a pack can freeload and gives
    /// the big creature a way back into the fight.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureUnit))]
    public class PounceCling : MonoBehaviour
    {
        [Tooltip("Target mass must exceed this creature's by at least this factor before it will climb.")]
        [Min(1f)]
        [SerializeField] private float massRatioToClimb = 4f;

        [Tooltip("Distance at which this creature will launch itself at a valid target.")]
        [SerializeField] private float pounceRange = 6f;

        [Tooltip("Upward and forward speed of the leap.")]
        [SerializeField] private float pounceSpeed = 9f;

        [Tooltip("Seconds spent clinging before the host shakes this creature loose.")]
        [SerializeField] private float clingDuration = 4.5f;

        [Tooltip("Damage per second dealt while attached. Cheap per second, dangerous in a pack.")]
        [SerializeField] private float clingDamagePerSecond = 95f;

        [Tooltip("Seconds before this creature may pounce again after being thrown off.")]
        [SerializeField] private float pounceCooldown = 3f;

        private CreatureUnit self;
        private CreatureUnit host;
        private ClingAnchors hostAnchors;
        private Transform anchor;
        private Rigidbody body;

        private float clingRemaining;
        private float cooldownRemaining;
        private float bobPhase;

        /// <summary>True while attached to a host.</summary>
        public bool IsClinging => host != null;

        private void Awake()
        {
            self = GetComponent<CreatureUnit>();
            body = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;
        }

        /// <summary>Is <paramref name="candidate"/> big enough to be worth climbing, and reachable?</summary>
        public bool CanPounce(CreatureUnit candidate)
        {
            if (IsClinging || cooldownRemaining > 0f) return false;
            if (candidate == null || candidate.IsDead || self.IsDead) return false;

            var mine = self.Definition;
            var theirs = candidate.Definition;
            if (mine == null || theirs == null || mine.mass <= 0f) return false;
            if (theirs.mass / mine.mass < massRatioToClimb) return false;

            Vector3 offset = candidate.transform.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > pounceRange * pounceRange) return false;

            // Only worth leaping if there is somewhere left to hold on.
            var anchors = candidate.GetComponent<ClingAnchors>();
            return anchors != null && anchors.Capacity > 0;
        }

        /// <summary>Leap onto <paramref name="target"/> and latch on.</summary>
        public bool TryPounce(CreatureUnit target)
        {
            if (!CanPounce(target)) return false;

            hostAnchors = target.GetComponent<ClingAnchors>();
            if (hostAnchors == null || !hostAnchors.TryClaim(out anchor)) return false;

            host = target;
            clingRemaining = clingDuration;
            bobPhase = 0f;

            SetAttached(true);
            return true;
        }

        private void LateUpdate()
        {
            if (host == null) return;

            // Either party dying, or the host being seized itself, ends the ride.
            if (self.IsDead || host.IsDead || anchor == null)
            {
                Detach(hopOff: false);
                return;
            }

            clingRemaining -= Time.deltaTime;
            bobPhase += Time.deltaTime * 9f;

            // Ride slightly above and to the side of the bone, scrabbling. LateUpdate so this runs
            // after the host's animator has posed the skeleton for the frame.
            Vector3 ride = anchor.position
                         + host.transform.up * (0.6f + Mathf.Sin(bobPhase) * 0.12f)
                         + host.transform.right * (Mathf.Sin(bobPhase * 0.7f) * 0.25f);

            transform.SetPositionAndRotation(
                ride,
                Quaternion.LookRotation(-host.transform.up, host.transform.forward));

            host.Health.TakeDamage(clingDamagePerSecond * Time.deltaTime);

            if (clingRemaining <= 0f) Detach(hopOff: true);
        }

        /// <summary>Let go. <paramref name="hopOff"/> throws this creature clear of the host.</summary>
        public void Detach(bool hopOff)
        {
            if (host == null) return;

            var formerHost = host;
            host = null;

            hostAnchors?.Release(anchor);
            hostAnchors = null;
            anchor = null;

            SetAttached(false);
            cooldownRemaining = pounceCooldown;

            if (!hopOff || self.IsDead || body == null) return;

            Vector3 away = transform.position - formerHost.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -formerHost.transform.forward;

            body.linearVelocity = (away.normalized + Vector3.up * 0.8f).normalized * pounceSpeed * 0.6f;
        }

        private void OnDisable()
        {
            // Never leave a clinger frozen on a host that is being torn down.
            Detach(hopOff: false);
        }

        private void SetAttached(bool attached)
        {
            if (body != null)
            {
                body.isKinematic = attached;
                if (!attached) body.linearVelocity = Vector3.zero;
            }

            foreach (var locomotion in GetComponentsInChildren<CreatureLocomotion>())
            {
                locomotion.enabled = !attached;
            }
        }
    }
}
