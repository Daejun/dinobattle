using DinoBattle.Core;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// The winners dance when the match is won.
    ///
    /// Asked for directly: "승리하면 신나는 노래가 나오면서 이긴애들이 춤을춰."
    ///
    /// Procedural, because the creature pack ships Idle, Walk, Run, Attack and Death and nothing
    /// that looks like celebrating. Rather than fake a new clip, this drives the transform — hop,
    /// spin, tilt — while holding the animator's Speed parameter up so the legs keep cycling. Moving
    /// legs plus a bouncing body reads as dancing; either one alone reads as a bug.
    ///
    /// Each creature is given its own phase offset, so a winning team bounces as a group of animals
    /// rather than as one animation applied six times.
    ///
    /// Runs on unscaled time and writes the transform directly. By this point the brain and the
    /// locomotion have both been switched off — BattleManager stands the survivors down when it
    /// declares a result — so nothing is competing for the position, and the Rigidbody is asleep.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureUnit))]
    public class VictoryDance : MonoBehaviour
    {
        [Tooltip("Hops per second.")]
        [SerializeField] private float tempo = 2.2f;

        [Tooltip("Hop height, as a fraction of the creature's own height, so a raptor and a boss " +
                 "bounce by the same amount visually rather than the same number of metres.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float hopHeight = 0.13f;

        [Tooltip("Degrees the creature rocks left and right.")]
        [SerializeField] private float sway = 14f;

        [Tooltip("Degrees per second it turns on the spot while dancing.")]
        [SerializeField] private float spin = 55f;

        [Tooltip("Value fed to the animator's Speed parameter, to keep the legs moving. Matched to " +
                 "the walk end of the locomotion blend rather than the run end — a sprinting-in-place " +
                 "dinosaur reads as a treadmill accident.")]
        [SerializeField] private float animatorSpeed = 0.45f;

        private CreatureUnit unit;
        private Animator animator;
        private Rigidbody body;
        private BattleManager battleManager;

        private bool dancing;
        private float phase;
        private Vector3 groundPosition;
        private float height = 2f;

        private void Awake()
        {
            unit = GetComponent<CreatureUnit>();
            animator = GetComponentInChildren<Animator>();
            body = GetComponent<Rigidbody>();

            // Offset per creature so a winning team does not move as one object.
            phase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Update()
        {
            TryBind();

            if (!dancing) return;

            // Stop if this creature somehow died after the result — nothing should dance a corpse.
            if (unit == null || unit.IsDead) { dancing = false; return; }

            float t = Time.unscaledTime * tempo * Mathf.PI * 2f + phase;

            // Abs of a sine, so the creature sits on the ground between hops instead of sinking
            // below it. A plain sine spends half its cycle underground.
            float hop = Mathf.Abs(Mathf.Sin(t)) * height * hopHeight;

            transform.position = groundPosition + Vector3.up * hop;
            transform.rotation = Quaternion.Euler(
                0f,
                transform.eulerAngles.y + spin * Time.unscaledDeltaTime,
                Mathf.Sin(t * 0.5f) * sway);

            if (animator != null) animator.SetFloat("Speed", animatorSpeed);
        }

        private void TryBind()
        {
            if (battleManager != null) return;

            battleManager = BattleManager.Instance;
            if (battleManager == null) return;

            battleManager.BattleEnded += HandleBattleEnded;
            battleManager.PhaseChanged += HandlePhaseChanged;
        }

        private void OnDestroy()
        {
            if (battleManager == null) return;

            battleManager.BattleEnded -= HandleBattleEnded;
            battleManager.PhaseChanged -= HandlePhaseChanged;
        }

        private void HandlePhaseChanged(BattlePhase phase)
        {
            if (phase != BattlePhase.Finished) dancing = false;
        }

        private void HandleBattleEnded(Team winner)
        {
            if (unit == null || unit.IsDead || unit.Team != winner) return;

            // Remember where the feet are. The hop is applied on top of this rather than accumulated
            // onto the current position, which would walk the creature into the sky.
            groundPosition = transform.position;
            height = MeasureHeight();

            // Freeze the physics body. Left awake it fights the transform writes and the creature
            // jitters instead of bouncing.
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            dancing = true;
        }

        private float MeasureHeight()
        {
            float top = 0f;

            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                if (renderer.GetComponentInParent<UI.HealthBarBillboard>() != null) continue;

                top = Mathf.Max(top, renderer.bounds.max.y - transform.position.y);
            }

            return Mathf.Max(0.5f, top);
        }
    }
}
