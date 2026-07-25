using System.Collections.Generic;
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

        [Tooltip("How quickly the framing target itself moves. The rig smooths on top of this, so " +
                 "this can be brisk without the shot feeling jerky. At 2.5 the target lagged far " +
                 "enough behind a moving fight that the camera regularly sat on empty ground.")]
        [SerializeField] private float retargetSmoothing = 8f;

        [Tooltip("Snap rather than ease when the action jumps further than this in one step — a " +
                 "creature dying can move the centroid a long way instantly, and easing across that " +
                 "gap sweeps the camera through everything in between.")]
        [SerializeField] private float snapDistance = 12f;

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

            // A big jump means the fight relocated — a kill removing one side of the centroid, say.
            // Easing across it drags the shot over everything between the old and new positions.
            bool jumped = hasFraming && Vector3.Distance(smoothedCenter, center) > snapDistance;

            if (!hasFraming || jumped)
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
        /// Centre and radius of the action, on the ground plane.
        ///
        /// Prefers creatures that are actually fighting. Averaging every survivor means that when
        /// two stragglers end up on opposite sides of the arena the camera settles on the empty
        /// ground between them — technically the centroid, useless to watch. Engaged creatures are
        /// where the fight is, so they win; the full roster is only the fallback while both sides
        /// are still closing.
        /// </summary>
        private bool TryGetCombatBounds(out Vector3 center, out float radius)
        {
            center = Vector3.zero;
            radius = 0f;

            var red = UnitRegistry.AliveOf(Team.Red);
            var blue = UnitRegistry.AliveOf(Team.Blue);

            // Centre on the fighting, but size the shot around EVERYONE. Centring and sizing both on
            // the engaged pair framed one duel so tightly that the rest of the battle sat off-screen
            // — the player could not see what was happening anywhere else. Splitting the two means
            // the camera looks where the action is while the wider battle stays in view.
            if (!Accumulate(red, blue, engagedOnly: true, out center) &&
                !Accumulate(red, blue, engagedOnly: false, out center))
            {
                return false;
            }

            radius = Mathf.Max(
                FurthestFrom(red, center, engagedOnly: false),
                FurthestFrom(blue, center, engagedOnly: false));

            return true;
        }

        private static bool Accumulate(
            IReadOnlyList<CreatureUnit> red, IReadOnlyList<CreatureUnit> blue, bool engagedOnly, out Vector3 center)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            AddTeam(red, engagedOnly, ref sum, ref count);
            AddTeam(blue, engagedOnly, ref sum, ref count);

            center = count > 0 ? sum / count : Vector3.zero;
            center.y = 0f;
            return count > 0;
        }

        private static void AddTeam(
            IReadOnlyList<CreatureUnit> team, bool engagedOnly, ref Vector3 sum, ref int count)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var unit = team[i];
                if (unit == null || unit.IsDead) continue;
                if (engagedOnly && !IsEngaged(unit)) continue;

                sum += unit.transform.position;
                count++;
            }
        }

        /// <summary>In contact with an enemy, rather than still walking toward one.</summary>
        private static bool IsEngaged(CreatureUnit unit)
        {
            var brain = unit.GetComponent<CreatureBrain>();
            return brain != null && brain.Current == CreatureBrain.State.Attack;
        }

        private static bool HasEngaged(IReadOnlyList<CreatureUnit> team)
        {
            for (int i = 0; i < team.Count; i++)
            {
                if (team[i] != null && !team[i].IsDead && IsEngaged(team[i])) return true;
            }

            return false;
        }

        private static float FurthestFrom(IReadOnlyList<CreatureUnit> team, Vector3 center, bool engagedOnly)
        {
            float furthest = 0f;

            for (int i = 0; i < team.Count; i++)
            {
                var unit = team[i];
                if (unit == null || unit.IsDead) continue;
                if (engagedOnly && !IsEngaged(unit)) continue;

                furthest = Mathf.Max(furthest, PlanarDistance(unit, center));
            }

            return furthest;
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
