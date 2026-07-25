using DinoBattle.Data;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Rigidbody steering for a creature: turn toward a destination, walk forward, stay grounded.
    /// Deliberately avoids NavMesh so the arena can be any physics geometry with no bake step.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class CreatureLocomotion : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float turnSpeedDegrees = 180f;

        [Tooltip("Only walk forward once facing within this many degrees of the target.")]
        [SerializeField] private float moveFacingTolerance = 60f;

        [Tooltip("How hard the creature is pushed toward its walk speed. Higher feels snappier and heavier.")]
        [SerializeField] private float acceleration = 20f;

        [Tooltip("Layers treated as walkable ground for the grounded check.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Distance below the pivot still considered grounded.")]
        [SerializeField] private float groundProbeDistance = 1.2f;

        private Rigidbody body;
        private bool stopped;

        public bool IsGrounded { get; private set; }

        /// <summary>Horizontal speed this frame. Feed it to the animator's locomotion blend.</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>Horizontal velocity, used by pursuers to lead this creature's future position.</summary>
        public Vector3 HorizontalVelocity
        {
            get
            {
                if (body == null) return Vector3.zero;
                Vector3 v = body.linearVelocity;
                v.y = 0f;
                return v;
            }
        }

        public float MoveSpeed => moveSpeed;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();

            // Physics owns position; rotation is driven manually so creatures do not topple while walking.
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Configure(CreatureDefinition definition)
        {
            if (definition == null) return;

            moveSpeed = definition.moveSpeed;
            turnSpeedDegrees = definition.turnSpeedDegrees;

            if (body == null) body = GetComponent<Rigidbody>();
            body.mass = definition.mass;
        }

        private void FixedUpdate()
        {
            IsGrounded = Physics.Raycast(
                transform.position + Vector3.up * 0.1f,
                Vector3.down,
                groundProbeDistance + 0.1f,
                groundMask,
                QueryTriggerInteraction.Ignore);

            Vector3 horizontal = body.linearVelocity;
            horizontal.y = 0f;
            CurrentSpeed = horizontal.magnitude;
        }

        /// <summary>
        /// Drive toward a steering velocity produced by <see cref="SteeringBehaviors"/>.
        ///
        /// Unlike <see cref="MoveTowards"/> this takes a direction AND a magnitude, so a behaviour
        /// that wants to ease off (Arrive) or barely nudge sideways (Separation) is obeyed instead of
        /// being flattened to "run at full speed toward a point".
        ///
        /// <paramref name="faceTarget"/> lets a creature keep its head on the enemy while sidestepping;
        /// pass null to face the direction of travel.
        /// </summary>
        public void Steer(Vector3 desiredVelocity, Vector3? faceTarget = null)
        {
            if (stopped) return;

            Vector3 flat = desiredVelocity;
            flat.y = 0f;

            Vector3 lookAt = faceTarget ?? (flat.sqrMagnitude > 0.01f ? transform.position + flat : transform.position);
            float angle = FaceTowards(lookAt);

            if (flat.sqrMagnitude < 0.0001f || !IsGrounded) return;

            // Only gate on facing when steering by travel direction. While locked onto a target the
            // creature is expected to strafe sideways, which would otherwise never pass the check.
            if (faceTarget == null && angle > moveFacingTolerance) return;

            Vector3 current = body.linearVelocity;
            Vector3 change = new Vector3(flat.x - current.x, 0f, flat.z - current.z);

            body.AddForce(Vector3.ClampMagnitude(change, moveSpeed) * acceleration, ForceMode.Acceleration);
        }

        /// <summary>Turn toward <paramref name="destination"/> and walk in once roughly facing it.</summary>
        public void MoveTowards(Vector3 destination)
        {
            if (stopped) return;

            Vector3 toTarget = destination - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            float angle = FaceTowards(destination);
            if (angle > moveFacingTolerance || !IsGrounded) return;

            Vector3 desired = transform.forward * moveSpeed;
            Vector3 current = body.linearVelocity;
            Vector3 change = new Vector3(desired.x - current.x, 0f, desired.z - current.z);

            body.AddForce(Vector3.ClampMagnitude(change, moveSpeed) * acceleration, ForceMode.Acceleration);
        }

        /// <summary>Rotate toward a point. Returns the remaining angle in degrees.</summary>
        public float FaceTowards(Vector3 target)
        {
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return 0f;

            Quaternion desired = Quaternion.LookRotation(flat, Vector3.up);
            if (!stopped)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, desired, turnSpeedDegrees * Time.deltaTime);
            }

            return Quaternion.Angle(transform.rotation, desired);
        }

        /// <summary>Bleed off horizontal momentum, e.g. while winding up an attack.</summary>
        public void Brake(float damping = 8f)
        {
            Vector3 velocity = body.linearVelocity;
            velocity.x = Mathf.Lerp(velocity.x, 0f, damping * Time.fixedDeltaTime);
            velocity.z = Mathf.Lerp(velocity.z, 0f, damping * Time.fixedDeltaTime);
            body.linearVelocity = velocity;
        }

        /// <summary>
        /// Stop steering and let the death animation play. Rotation stays frozen on purpose: unfreezing
        /// it made the corpse tumble, which fights the death clip instead of reading as a fall.
        /// Revisit only if this is replaced with a real ragdoll.
        /// </summary>
        public void OnUnitDied()
        {
            stopped = true;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
