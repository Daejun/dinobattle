using DinoBattle.Core;
using DinoBattle.Units;
using UnityEngine;

namespace DinoBattle.CameraRig
{
    /// <summary>
    /// Keeps the whole fight in frame without the player having to chase it.
    ///
    /// Separated from <see cref="OrbitCameraController"/> on purpose: the controller is the rig —
    /// it knows how to orbit, pan and zoom — and this decides where to point it. Folding the two
    /// together would tangle input handling with battle state.
    ///
    /// Manual input always wins. Touching the camera suspends auto-framing, and it fades back in a
    /// few seconds later, so the player can go look at something without fighting the director.
    /// </summary>
    [RequireComponent(typeof(OrbitCameraController))]
    public class BattleCameraDirector : MonoBehaviour
    {
        [Tooltip("Seconds after the player last moved the camera before auto-framing resumes.")]
        [SerializeField] private float manualControlGrace = 3f;

        [Tooltip("Extra room around the action, as a multiplier on the fitted distance. Small — the " +
                 "fit already leaves margin, and padding compounds with the focus radius.")]
        [Range(1f, 2f)]
        [SerializeField] private float framingPadding = 1.1f;

        [Tooltip("Smallest radius considered, so a single creature is not zoomed into uncomfortably.")]
        [SerializeField] private float minimumFocusRadius = 4f;

        [Tooltip("Largest radius framed, in world units. Sized against the creatures: a T-Rex is 5 " +
                 "units, so a radius of 10 puts it at roughly a fifth of the screen height. At 22 " +
                 "with heavier padding the camera sat ~59 units out and the fighters were specks.")]
        [SerializeField] private float maximumFocusRadius = 10f;

        [Tooltip("How quickly the framing target itself moves. The rig smooths on top of this.")]
        [SerializeField] private float retargetSmoothing = 2.5f;

        [Tooltip("View of the whole arena while the player is still placing creatures.")]
        [SerializeField] private float placementDistance = 46f;

        private OrbitCameraController rig;
        private BattleManager battleManager;
        private Vector3 smoothedCenter;
        private float smoothedRadius;
        private bool hasFraming;

        private void Awake()
        {
            rig = GetComponent<OrbitCameraController>();
        }

        private void Start()
        {
            battleManager = BattleManager.Instance;
        }

        private void LateUpdate()
        {
            if (battleManager == null) battleManager = BattleManager.Instance;
            if (battleManager == null) return;

            if (Time.unscaledTime - rig.LastManualInputTime < manualControlGrace) return;

            switch (battleManager.Phase)
            {
                case BattlePhase.Placement:
                    // Nothing is fighting yet; show the arena so the player can see where to drop.
                    rig.FocusOn(Vector3.zero, placementDistance);
                    hasFraming = false;
                    return;

                case BattlePhase.Fighting:
                case BattlePhase.Finished:
                    FrameCombatants();
                    return;
            }
        }

        private void FrameCombatants()
        {
            if (!TryGetCombatBounds(out Vector3 center, out float radius))
            {
                hasFraming = false;
                return;
            }

            radius = Mathf.Clamp(radius, minimumFocusRadius, maximumFocusRadius);

            if (!hasFraming)
            {
                smoothedCenter = center;
                smoothedRadius = radius;
                hasFraming = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-retargetSmoothing * Time.unscaledDeltaTime);
                smoothedCenter = Vector3.Lerp(smoothedCenter, center, t);
                smoothedRadius = Mathf.Lerp(smoothedRadius, radius, t);
            }

            rig.FocusOn(smoothedCenter, DistanceToFit(smoothedRadius));
        }

        /// <summary>
        /// Centre and radius of the sphere containing every living fighter, on the ground plane.
        /// Two passes: the centroid first, then the furthest distance from it.
        /// </summary>
        private bool TryGetCombatBounds(out Vector3 center, out float radius)
        {
            center = Vector3.zero;
            radius = 0f;

            var red = UnitRegistry.AliveOf(Team.Red);
            var blue = UnitRegistry.AliveOf(Team.Blue);

            int count = 0;
            Vector3 sum = Vector3.zero;

            for (int i = 0; i < red.Count; i++)
            {
                if (red[i] == null || red[i].IsDead) continue;
                sum += red[i].transform.position;
                count++;
            }

            for (int i = 0; i < blue.Count; i++)
            {
                if (blue[i] == null || blue[i].IsDead) continue;
                sum += blue[i].transform.position;
                count++;
            }

            if (count == 0) return false;

            center = sum / count;
            center.y = 0f;

            for (int i = 0; i < red.Count; i++) radius = Mathf.Max(radius, PlanarDistance(red[i], center));
            for (int i = 0; i < blue.Count; i++) radius = Mathf.Max(radius, PlanarDistance(blue[i], center));

            return true;
        }

        private static float PlanarDistance(CreatureUnit unit, Vector3 center)
        {
            if (unit == null || unit.IsDead) return 0f;

            Vector3 offset = unit.transform.position - center;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>
        /// How far back the camera must sit for a sphere of <paramref name="radius"/> to fit.
        ///
        /// Standard bounding-sphere fit: distance = radius / sin(fov/2). The limiting axis is
        /// whichever field of view is narrower — on a wide landscape phone that is the vertical
        /// one, so fitting only to vertical FOV would crop the sides on tall displays.
        /// </summary>
        private float DistanceToFit(float radius)
        {
            float vertical = rig.VerticalFieldOfView * Mathf.Deg2Rad;
            float horizontal = 2f * Mathf.Atan(Mathf.Tan(vertical * 0.5f) * rig.Aspect);
            float limiting = Mathf.Min(vertical, horizontal);

            float fitted = radius / Mathf.Max(0.01f, Mathf.Sin(limiting * 0.5f));
            return Mathf.Clamp(fitted * framingPadding, rig.MinDistance, rig.MaxDistance);
        }
    }
}
