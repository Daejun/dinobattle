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

        [Tooltip("Ease rate for turning. Shapes HOW the turn arrives — high still means quick, but " +
                 "always with a slow-down into the final heading instead of stopping dead.")]
        [SerializeField] private float turnSharpness = 14f;

        [Tooltip("Ease rate for changes in the steering command. Lower is smoother and more languid, " +
                 "higher snaps to each new intention. Around 12 reads as deliberate without lag.")]
        [SerializeField] private float steeringSmoothing = 12f;

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

        /// <summary>
        /// The brain's raw command, before smoothing.
        ///
        /// The AI switches between quite different intentions from one frame to the next — brake for
        /// a swing, circle, pursue, break off — and each switch is a step change in the requested
        /// velocity. Feeding those steps straight to the Rigidbody is what made movement look
        /// mechanical. Easing between them costs a fraction of a second of responsiveness and buys
        /// motion that reads as an animal changing its mind rather than a state machine ticking.
        /// </summary>
        private Vector3 commandedVelocity;

        /// <summary>
        /// Seconds of lost footing remaining. While this is running the steering does not touch
        /// velocity, so a collision shove survives instead of being erased on the next physics step.
        /// </summary>
        private float staggerRemaining;

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

            if (staggerRemaining > 0f)
            {
                // Knocked off balance: physics has the creature until it recovers. Rotation still
                // runs, so it keeps facing its enemy while being shoved back.
                staggerRemaining -= Time.fixedDeltaTime;
                return;
            }

            if (!stopped) ApplySteering();
        }

        /// <summary>
        /// Drive the Rigidbody toward the steering velocity. Runs on the physics tick, exactly once
        /// per step, which is the whole point of storing the command rather than applying it inline.
        /// </summary>
        private void ApplySteering()
        {
            // Ease toward the brain's latest intention rather than adopting it whole.
            desiredVelocity = Vector3.Lerp(
                desiredVelocity, commandedVelocity, 1f - Mathf.Exp(-steeringSmoothing * Time.fixedDeltaTime));

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

            commandedVelocity = flat;
        }

        /// <summary>
        /// Knock this creature off balance for a moment, letting physics carry it.
        ///
        /// Takes the longest pending stagger rather than adding: being hit twice in a scrum should
        /// not accumulate into a creature that cannot move for a second and a half.
        /// </summary>
        public void Stagger(float seconds)
        {
            if (stopped) return;

            staggerRemaining = Mathf.Max(staggerRemaining, seconds);
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

                // Exponential approach, then clamped to the rate limit.
                //
                // RotateTowards alone turns at a constant angular velocity: it starts instantly, runs
                // flat out, and stops dead on arrival. That is precisely the mechanical quality —
                // nothing alive turns with a square velocity profile. Easing gives the natural
                // slow-down into the final heading, while the clamp still stops a heavy creature
                // from snapping round faster than its weight allows.
                Quaternion eased = Quaternion.Slerp(
                    transform.rotation, desired, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));

                Quaternion next = Quaternion.RotateTowards(transform.rotation, eased, rate * Time.deltaTime);

                // Through the Rigidbody, not the Transform.
                //
                // Interpolation is on, so the physics pose is authoritative: every frame the
                // interpolator restores the transform toward it, and a rotation written straight to
                // the Transform is quietly undone. With freezeRotation the physics rotation never
                // changes on its own, so the two fought to a standstill — measured at 0.9 degrees of
                // progress over 688 frames.
                //
                // That was the deadlock behind mid-fight standoffs. Creatures brake on contact, a
                // braked creature cannot turn, so it never lines up, so it never swings, so it stays
                // braked. Everything froze in place a few seconds into every fight.
                if (body != null) body.rotation = next;
                else transform.rotation = next;
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
            staggerRemaining = 0f;
            desiredVelocity = Vector3.zero;
            commandedVelocity = Vector3.zero;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
