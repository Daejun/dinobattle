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

        [Tooltip("How much more a creature in contact counts toward the framing centre than one still " +
                 "closing. Higher points the camera harder at the fighting; too high and it becomes " +
                 "the old membership-jump problem again, because one creature dominates the average.")]
        [Range(1f, 10f)]
        [SerializeField] private float engagedWeight = 3f;

        [Tooltip("Snap rather than ease when the action jumps further than this in one step — a " +
                 "creature dying can move the centroid a long way instantly, and easing across that " +
                 "gap sweeps the camera through everything in between.")]
        [SerializeField] private float snapDistance = 12f;

        [Tooltip("View of the whole arena while the player is still placing creatures. Close enough " +
                 "that the creatures already placed are recognisable — at 46 they were specks, which " +
                 "rather wasted the point of showing them at all.")]
        [SerializeField] private float placementDistance = 34f;

        [Tooltip("Fraction of the living creatures the shot must contain. Below 1 so a straggler still " +
                 "walking in does not hold the camera at arm's length while the fight is already on.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float framingCoverage = 0.8f;

        [Tooltip("Framing radius for a creature the player tapped, as a multiple of its footprint. " +
                 "Larger than the victory shot's, because a creature in a fight needs its opponent in " +
                 "frame too.")]
        [Range(1f, 8f)]
        [SerializeField] private float followFramingFactor = 4f;

        [Header("Victory shot")]
        [Tooltip("Closest the victory shot will frame, whatever the survivor's size. Stops a raptor " +
                 "from filling the screen with one thigh.")]
        [SerializeField] private float victoryMinimumRadius = 2.5f;

        [Tooltip("Victory framing radius as a multiple of the survivor's own footprint, so the shot " +
                 "sits the same distance off a raptor as off a boss in body-lengths rather than metres.")]
        [Range(1f, 5f)]
        [SerializeField] private float victoryFramingFactor = 2.2f;

        private OrbitCameraController rig;
        private BattleManager battleManager;
        private Vector3 smoothedCenter;
        private float smoothedRadius;
        private bool hasFraming;

        /// <summary>
        /// The survivor the victory shot is on. Held rather than re-picked each frame: the choice
        /// depends on health, and re-running it every frame would let the shot hop between survivors
        /// as the health bars settle.
        /// </summary>
        private CreatureUnit victor;

        private BattlePhase lastPhase = BattlePhase.Placement;

        /// <summary>Reused so the per-frame percentile sort does not allocate.</summary>
        private readonly List<float> distanceScratch = new();

        /// <summary>Measured creature heights. Renderer bounds are not cheap enough to query per frame.</summary>
        private readonly Dictionary<CreatureUnit, float> heightCache = new();

        /// <summary>
        /// A creature the player tapped and wants kept on screen, or null for automatic framing.
        /// Set by <see cref="CreatureFocusPicker"/>.
        /// </summary>
        private CreatureUnit followed;

        private void Awake()
        {
            rig = GetComponent<OrbitCameraController>();
        }

        /// <summary>
        /// Keep <paramref name="unit"/> on screen until told otherwise. Null returns to auto-framing.
        ///
        /// The automatic framing points at wherever the fighting is thickest, which is the right
        /// default and the wrong answer once the player has picked someone to care about — a
        /// four-year-old testing this said "내가 티라노 보고 있는데 화면이 저쪽으로 가버려".
        /// </summary>
        public void Follow(CreatureUnit unit)
        {
            followed = unit;

            // Snap rather than sweep. The chosen creature can be right across the arena, and easing
            // there drags the shot through everything in between — the same reason a kill that moves
            // the centroid a long way snaps instead of gliding.
            if (unit != null) hasFraming = false;
        }

        /// <summary>The creature the player asked to watch, or null. Exposed so the HUD can show it.</summary>
        public CreatureUnit Followed => followed != null && !followed.IsDead ? followed : null;

        /// <summary>
        /// The survivor this camera is framing after a win, or null before one is chosen.
        ///
        /// Exposed so the victory celebration bursts over the creature that is actually on screen.
        /// The two used to choose independently — the camera takes the survivor with the LEAST health
        /// because the animal that nearly died is the one worth looking at, while the confetti took
        /// the one with the MOST — and the result was 220 flakes thrown over a creature outside the
        /// frame. Measured: 220 alive, 0 inside the viewport. One definition, one subject.
        /// </summary>
        public CreatureUnit Victor => victor != null && !victor.IsDead ? victor : null;

        private void Start()
        {
            battleManager = BattleManager.Instance;
        }

        private void LateUpdate()
        {
            if (battleManager == null) battleManager = BattleManager.Instance;
            if (battleManager == null) return;

            // A new match invalidates the previous winner, so the next victory shot picks afresh.
            if (battleManager.Phase != lastPhase)
            {
                lastPhase = battleManager.Phase;
                if (lastPhase != BattlePhase.Finished) victor = null;

                // A new match has new creatures; the old pick is a dead reference either way.
                followed = null;

                // And so is every key in the height cache. Nothing ever emptied it, so each match
                // left an entry per creature behind, keyed on a destroyed object that could never be
                // looked up again. Harmless per match and unbounded across a session.
                heightCache.Clear();
            }

            if (Time.unscaledTime - rig.LastManualInputTime < manualControlGrace) return;

            switch (battleManager.Phase)
            {
                case BattlePhase.Placement:
                    // Nothing is fighting yet; show the arena so the player can see where to drop.
                    rig.FocusOn(Vector3.zero, placementDistance);
                    hasFraming = false;
                    return;

                case BattlePhase.Fighting:
                    // A creature the player chose outranks the automatic framing, but only while it
                    // is alive — following a corpse would strand the camera on the one thing that has
                    // stopped being interesting, so death hands the shot back to the fight.
                    if (Followed != null) FrameFollowed(Followed);
                    else FrameCombatants();
                    return;

                case BattlePhase.Finished:
                    FrameVictor();
                    return;
            }
        }

        /// <summary>
        /// Hold the shot on one creature the player picked, sized to that creature.
        /// </summary>
        private void FrameFollowed(CreatureUnit unit)
        {
            if (!heightCache.TryGetValue(unit, out float height))
            {
                height = MeasureHeight(unit);
                heightCache[unit] = height;
            }

            Vector3 center = unit.transform.position;
            center.y = height * 0.5f;

            // Wider than the victory shot on purpose: this is a creature in a fight, and framing it
            // as tightly as a winner standing alone would crop out whatever it is fighting.
            float footprint = unit.Definition != null ? unit.Definition.footprintRadius : 1f;
            float radius = Mathf.Max(footprint * followFramingFactor, height * 0.7f);

            ApplyFraming(center, radius, minimumFocusRadius);
        }

        /// <summary>
        /// Close in on whoever is left standing.
        ///
        /// The wide framing is right while there is a battle to follow and wrong the moment there is
        /// not: with one side gone it sizes the shot around the winners' spread, so a scattered
        /// winning team leaves the camera parked high over empty ground at the end of every match.
        /// A single survivor is the subject, so the shot should be on it.
        ///
        /// Goes through the same smoothing as combat framing rather than cutting, so the win reads as
        /// the camera settling on the victor rather than as an edit.
        /// </summary>
        private void FrameVictor()
        {
            if (victor == null || victor.IsDead) victor = ChooseVictor();

            // A draw — both sides wiped out. Nothing to close in on, so leave the shot where it is.
            if (victor == null)
            {
                FrameCombatants();
                return;
            }

            Vector3 center = victor.transform.position;

            if (!heightCache.TryGetValue(victor, out float height))
            {
                height = MeasureHeight(victor);
                heightCache[victor] = height;
            }

            // Same headroom rule as combat framing: aim at the middle of the animal, not its feet.
            center.y = height * 0.5f;

            float footprint = victor.Definition != null ? victor.Definition.footprintRadius : 1f;
            float radius = Mathf.Max(footprint * victoryFramingFactor, height * 0.6f);
            ApplyFraming(center, radius, victoryMinimumRadius);
        }

        /// <summary>
        /// The survivor worth looking at: the one that came closest to losing.
        ///
        /// Health rather than position, because the interesting animal at the end of a fight is the
        /// one that nearly died in it. On a clean sweep where nobody took damage this falls through
        /// to the tie-break and picks whoever is nearest the last of the action, which is where the
        /// camera already is.
        /// </summary>
        private CreatureUnit ChooseVictor()
        {
            CreatureUnit best = null;
            float bestHealth = float.MaxValue;
            float bestDistance = float.MaxValue;

            foreach (Team team in new[] { Team.Red, Team.Blue })
            {
                var units = UnitRegistry.AliveOf(team);

                for (int i = 0; i < units.Count; i++)
                {
                    var unit = units[i];
                    if (unit == null || unit.IsDead) continue;

                    float health = unit.Health != null && unit.Health.Max > 0f
                        ? unit.Health.Current / unit.Health.Max
                        : 1f;
                    float distance = PlanarDistance(unit, smoothedCenter);

                    bool better = health < bestHealth - 0.001f
                                  || (health < bestHealth + 0.001f && distance < bestDistance);
                    if (!better) continue;

                    best = unit;
                    bestHealth = health;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private void FrameCombatants()
        {
            if (!TryGetCombatBounds(out Vector3 center, out float radius))
            {
                hasFraming = false;
                return;
            }

            ApplyFraming(center, radius, minimumFocusRadius);
        }

        /// <summary>
        /// Ease the framing toward a new centre and radius, then point the rig at it.
        /// </summary>
        private void ApplyFraming(Vector3 center, float radius, float minimumRadius)
        {
            radius = Mathf.Clamp(radius, minimumRadius, maximumFocusRadius);

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
            //
            // Weighted rather than filtered. Averaging only the creatures currently in Attack made
            // the centre depend on set MEMBERSHIP, and hit-and-run flips creatures between Seek and
            // Attack several times a second — every flip teleported the centroid and the camera
            // twitched after it. Weighting keeps everyone in the average, so a creature entering or
            // leaving the fight slides the shot instead of jumping it.
            if (!AccumulateWeighted(red, blue, out center)) return false;

            // Cover most of the battle, not all of it.
            //
            // Sizing to the outermost creature let a single straggler dictate the shot. Measured from
            // the start of a fight: the armies closed from 24 units apart to 4.5 within two seconds
            // and the camera stayed at its maximum distance for four, because one creature was still
            // walking in. The fight was already happening and the camera was still framing the
            // approach. A coverage fraction keeps the intent — the wider battle stays in view — while
            // one late arrival no longer holds the whole shot open.
            radius = CoveringRadius(red, blue, center);

            // Tall creatures need headroom. The framing is worked out on the ground plane, so a boss
            // several times the height of anything else had its head cropped off the top of the
            // screen: the shot was correctly sized for its footprint and knew nothing about how far
            // up it went. Lifting the centre and counting the height into the radius frames the
            // animal rather than the ground it stands on.
            float tallest = TallestAmong(red, blue);
            if (tallest > 0f)
            {
                center.y = tallest * 0.5f;
                radius = Mathf.Max(radius, tallest * 0.6f);
            }

            return true;
        }

        /// <summary>
        /// The distance from <paramref name="center"/> that contains <see cref="framingCoverage"/> of
        /// the living creatures, so outliers do not set the scale on their own.
        /// </summary>
        private float CoveringRadius(
            IReadOnlyList<CreatureUnit> red, IReadOnlyList<CreatureUnit> blue, Vector3 center)
        {
            distanceScratch.Clear();
            CollectDistances(red, center);
            CollectDistances(blue, center);

            if (distanceScratch.Count == 0) return 0f;

            distanceScratch.Sort();

            int index = Mathf.Clamp(
                Mathf.CeilToInt(distanceScratch.Count * framingCoverage) - 1, 0, distanceScratch.Count - 1);

            return distanceScratch[index];
        }

        private void CollectDistances(IReadOnlyList<CreatureUnit> team, Vector3 center)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var unit = team[i];
                if (unit == null || unit.IsDead) continue;

                distanceScratch.Add(PlanarDistance(unit, center));
            }
        }

        /// <summary>
        /// Height of the tallest living creature, from its renderers. Cached per creature: the bounds
        /// query is not free and this runs every frame for everyone still on the field.
        /// </summary>
        private float TallestAmong(IReadOnlyList<CreatureUnit> red, IReadOnlyList<CreatureUnit> blue)
        {
            return Mathf.Max(TallestIn(red), TallestIn(blue));
        }

        private float TallestIn(IReadOnlyList<CreatureUnit> team)
        {
            float tallest = 0f;

            for (int i = 0; i < team.Count; i++)
            {
                var unit = team[i];
                if (unit == null || unit.IsDead) continue;

                if (!heightCache.TryGetValue(unit, out float height))
                {
                    height = MeasureHeight(unit);
                    heightCache[unit] = height;
                }

                tallest = Mathf.Max(tallest, height);
            }

            return tallest;
        }

        private static float MeasureHeight(CreatureUnit unit)
        {
            var renderers = unit.GetComponentsInChildren<Renderer>();
            float top = 0f;

            for (int i = 0; i < renderers.Length; i++)
            {
                // Skip the health bar and team ring: they are markers placed relative to the creature,
                // and the bar in particular already sits above it, so including them would compound.
                if (renderers[i] is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                if (renderers[i].GetComponentInParent<DinoBattle.UI.HealthBarBillboard>() != null) continue;

                top = Mathf.Max(top, renderers[i].bounds.max.y - unit.transform.position.y);
            }

            return top;
        }

        private bool AccumulateWeighted(
            IReadOnlyList<CreatureUnit> red, IReadOnlyList<CreatureUnit> blue, out Vector3 center)
        {
            Vector3 sum = Vector3.zero;
            float totalWeight = 0f;

            AddTeam(red, ref sum, ref totalWeight);
            AddTeam(blue, ref sum, ref totalWeight);

            center = totalWeight > 0f ? sum / totalWeight : Vector3.zero;
            center.y = 0f;
            return totalWeight > 0f;
        }

        private void AddTeam(IReadOnlyList<CreatureUnit> team, ref Vector3 sum, ref float totalWeight)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var unit = team[i];
                if (unit == null || unit.IsDead) continue;

                float weight = IsEngaged(unit) ? engagedWeight : 1f;

                sum += unit.transform.position * weight;
                totalWeight += weight;
            }
        }

        /// <summary>In contact with an enemy, rather than still walking toward one.</summary>
        private static bool IsEngaged(CreatureUnit unit)
        {
            // unit.Brain is cached on the creature. This runs for every survivor several times per
            // LateUpdate, and a GetComponent here made framing cost scale with the size of the battle.
            var brain = unit.Brain;
            return brain != null && brain.Current == CreatureBrain.State.Attack;
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
