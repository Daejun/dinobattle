using DinoBattle.Data;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Rigidbody steering for a creature: turn toward a destination, walk forward, stay grounded.
    /// Deliberately avoids NavMesh so the arena can be any physics geometry with no bake step.
    /// </summary>
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

        /// <summary>Let physics take over — the corpse should tumble, not keep walking.</summary>
        public void OnUnitDied()
        {
            stopped = true;
            body.freezeRotation = false;
        }
    }
}
