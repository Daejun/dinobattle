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

        [Tooltip("How much faster a stationary creature turns. Turning in place is a different action " +
                 "from steering while running, and it is the one that decides whether a heavy " +
                 "dinosaur can ever bring its head round onto something circling it.")]
        [Min(1f)]
        [SerializeField] private float pivotTurnMultiplier = 10f;

        [Tooltip("Below this speed the creature counts as turning in place.")]
        [SerializeField] private float pivotSpeedThreshold = 1.5f;

        [Tooltip("How hard the creature is pushed toward its walk speed. Higher feels snappier and heavier.")]
        [SerializeField] private float acceleration = 20f;

        [Tooltip("Layers treated as walkable ground for the grounded check.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Distance below the pivot still considered grounded.")]
        [SerializeField] private float groundProbeDistance = 1.2f;

        [Tooltip("How hard a stationary creature bleeds off leftover momentum.")]
        [SerializeField] private float brakeDamping = 8f;

        private Rigidbody body;
        private bool stopped;

        /// <summary>
        /// The steering command from the most recent frame, applied on the physics tick.
        ///
        /// Kept as state rather than acted on immediately because <see cref="Steer"/> is called from
        /// the brain's Update, and Update does not line up with FixedUpdate. Adding force straight
        /// from Update meant a physics step could receive two frames' worth of force, or none at all,
        /// depending on where the frame boundaries happened to fall — which showed up as creatures
        /// visibly wobbling as they walked. A steering velocity is a continuous control signal, so
        /// the latest value simply stands until it is replaced.
        /// </summary>
        private Vector3 desiredVelocity;

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

            if (!stopped) ApplySteering();
        }

        /// <summary>
        /// Drive the Rigidbody toward the steering velocity. Runs on the physics tick, exactly once
        /// per step, which is the whole point of storing the command rather than applying it inline.
        /// </summary>
        private void ApplySteering()
        {
            Vector3 current = body.linearVelocity;

            // A zero command means "stop here" — brake rather than coast. This is what holds an
            // attacker still through a swing instead of letting it drift through its own animation.
            if (desiredVelocity.sqrMagnitude < 0.0001f)
            {
                current.x = Mathf.Lerp(current.x, 0f, brakeDamping * Time.fixedDeltaTime);
                current.z = Mathf.Lerp(current.z, 0f, brakeDamping * Time.fixedDeltaTime);
                body.linearVelocity = current;
                return;
            }

            if (!IsGrounded) return;

            Vector3 change = new(desiredVelocity.x - current.x, 0f, desiredVelocity.z - current.z);
            body.AddForce(Vector3.ClampMagnitude(change, moveSpeed) * acceleration, ForceMode.Acceleration);
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
        public void Steer(Vector3 desired, Vector3? faceTarget = null)
        {
            if (stopped) return;

            Vector3 flat = desired;
            flat.y = 0f;

            // Rotation stays on the frame tick. It is purely visual — nothing physical depends on it
            // — and turning at the render rate is smoother than stepping it at 50Hz.
            Vector3 lookAt = faceTarget ?? (flat.sqrMagnitude > 0.01f ? transform.position + flat : transform.position);
            float angle = FaceTowards(lookAt);

            // Only gate on facing when steering by travel direction. While locked onto a target the
            // creature is expected to strafe sideways, which would otherwise never pass the check.
            if (faceTarget == null && angle > moveFacingTolerance) flat = Vector3.zero;

            desiredVelocity = flat;
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
                // Pivoting on the spot is far quicker than turning mid-stride: planted feet can just
                // step round, where a running animal has to arc. It matters because the situations
                // that most need a fast turn are exactly the stationary ones — a heavy dinosaur
                // braked in melee, being circled by something it cannot otherwise ever face.
                float rate = turnSpeedDegrees;
                if (CurrentSpeed < pivotSpeedThreshold) rate *= pivotTurnMultiplier;

                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, desired, rate * Time.deltaTime);
            }

            return Quaternion.Angle(transform.rotation, desired);
        }

        /// <summary>
        /// Stop steering and let the death animation play. Rotation stays frozen on purpose: unfreezing
        /// it made the corpse tumble, which fights the death clip instead of reading as a fall.
        /// Revisit only if this is replaced with a real ragdoll.
        /// </summary>
        public void OnUnitDied()
        {
            stopped = true;
            desiredVelocity = Vector3.zero;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
