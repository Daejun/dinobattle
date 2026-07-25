using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Turns a creature's head toward whatever it is fighting.
    ///
    /// The body can only come about so fast — a T-Rex needs about a second for 180 degrees — and
    /// until it does, the creature reads as ignoring an enemy that is plainly right there. Real
    /// animals solve this by looking first and turning after. So does this.
    ///
    /// It also removes the temptation to paper over slow turning by widening the attack angle. A
    /// creature still may not bite what is beside it; it just stops looking oblivious while it
    /// brings its body round.
    ///
    /// Applied as a DELTA rotation in LateUpdate rather than by setting the bone's rotation outright.
    /// Two reasons: LateUpdate runs after the Animator has posed the skeleton, so this layers on top
    /// of the animation instead of being overwritten by it; and a delta needs no knowledge of which
    /// way the bone's local axes happen to point, which varies per rig and is not worth hard-coding.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureUnit))]
    public class HeadLook : MonoBehaviour
    {
        [Tooltip("Bone names to drive, best first. The first one found under this creature wins.")]
        [SerializeField] private string[] boneNames = { "Head", "Neck", "Skull" };

        [Tooltip("Furthest the head will turn off the body's facing. Past this the creature has to " +
                 "move its body, which is the point — a head does not swivel all the way round.")]
        [Range(0f, 90f)]
        [SerializeField] private float maxYaw = 55f;

        [Tooltip("How quickly the head tracks, as an exponential rate. Deliberately near-instant: a " +
                 "head is light and an animal snaps it onto whatever it is watching. At 9 the turn " +
                 "was slow enough to read as the creature noticing late, which defeats the purpose " +
                 "of having the head lead the body at all.")]
        [SerializeField] private float responsiveness = 90f;

        private CreatureUnit self;
        private CreatureBrain brain;
        private Transform bone;
        private float currentYaw;

        /// <summary>
        /// Degrees the head is currently turned off the body's facing. Exposed because it cannot be
        /// measured from outside: the delta is applied on top of the animator's pose, and the bone's
        /// own axes do not line up with the creature's forward, so reading the bone tells you nothing.
        /// </summary>
        public float Yaw => currentYaw;

        /// <summary>Null on the placeholder primitives, which have no skeleton to drive.</summary>
        public bool HasHeadBone => bone != null;

        private void Awake()
        {
            self = GetComponent<CreatureUnit>();
            brain = GetComponent<CreatureBrain>();
            bone = FindBone();
        }

        private Transform FindBone()
        {
            var candidates = GetComponentsInChildren<Transform>(true);

            foreach (string wanted in boneNames)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate.name.IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return candidate;
                    }
                }
            }

            // No head bone is not an error. Placeholder creatures are primitives with no skeleton,
            // and they simply do not get head tracking.
            return null;
        }

        private void LateUpdate()
        {
            if (bone == null) return;

            float desiredYaw = 0f;

            var target = brain != null ? brain.Target : null;
            if (target != null && !target.IsDead && !self.IsDead && brain.CombatEnabled)
            {
                Vector3 toTarget = target.AimPoint.position - bone.position;
                toTarget.y = 0f;

                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    Vector3 bodyForward = transform.forward;
                    bodyForward.y = 0f;

                    // Signed, so the head knows which way to turn, and clamped so it stays within
                    // what a neck can do. Anything beyond this the body has to supply.
                    desiredYaw = Mathf.Clamp(
                        Vector3.SignedAngle(bodyForward, toTarget, Vector3.up), -maxYaw, maxYaw);
                }
            }

            // Eases back to neutral on its own when there is nothing to look at, because desiredYaw
            // is simply 0 in that case.
            currentYaw = Mathf.Lerp(
                currentYaw, desiredYaw, 1f - Mathf.Exp(-responsiveness * Time.deltaTime));

            bone.rotation = Quaternion.AngleAxis(currentYaw, Vector3.up) * bone.rotation;
        }
    }
}
