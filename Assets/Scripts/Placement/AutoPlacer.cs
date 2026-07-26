using System.Collections.Generic;
using DinoBattle.Core;
using DinoBattle.Data;
using UnityEngine;

namespace DinoBattle.Placement
{
    /// <summary>
    /// Fills a team's budget with creatures, so a match can be set up in one tap instead of a dozen.
    ///
    /// Placement is the part of a spectator game the player is least interested in — the point is
    /// watching the fight, and hand-dropping twenty dinosaurs to get there is a chore. This picks a
    /// spend and a formation; the player can still place by hand when they want a specific matchup.
    /// </summary>
    public class AutoPlacer : MonoBehaviour
    {
        [SerializeField] private BattleManager battleManager;

        [Tooltip("Fraction of the arena radius each side forms up on, measured from the centre.")]
        [Range(0.3f, 0.95f)]
        [SerializeField] private float lineRadiusFactor = 0.72f;

        [Tooltip("How wide a team's formation arc is, in degrees.")]
        [SerializeField] private float formationArcDegrees = 70f;

        [Tooltip("Arena radius. Kept in sync with BattleSceneBuilder by the scene builder itself.")]
        [SerializeField] private float arenaRadius = 22f;

        [Tooltip("Stop trying once the cheapest remaining creature no longer fits.")]
        [SerializeField] private int maxPerTeam = 12;

        [Tooltip("Bosses the boss-battle button can pick from. Separate from the main roster so a " +
                 "boss can never turn up as an ordinary auto-fill pick.")]
        [SerializeField] private CreatureRoster bossRoster;

        [Tooltip("How many hunters face the boss. Ignores the budget on purpose — the mode is about " +
                 "the swarm, and a boss is not something you buy your way past.")]
        [Min(1)]
        [SerializeField] private int bossPackSize = 10;

        [Tooltip("Radius of the hunters' ring, as a fraction of the arena. Wide enough to clear the " +
                 "boss's own footprint at the centre, tight enough that the encirclement is obvious.")]
        [Range(0.3f, 0.95f)]
        [SerializeField] private float bossRingFactor = 0.62f;

        private void Awake()
        {
            if (battleManager == null) battleManager = BattleManager.Instance;
        }

        /// <summary>Fill both teams with a random spend, replacing whatever is already placed.</summary>
        public void FillBothTeams()
        {
            if (battleManager == null) battleManager = BattleManager.Instance;
            if (battleManager == null || battleManager.Phase != BattlePhase.Placement) return;

            battleManager.Loadout.Clear();

            // Same headcount on both sides, different armies.
            //
            // Each side used to spend its budget independently, which is fair on points and looks
            // rigged on the field: 4 against 3 is the first thing anyone notices, and equal cost is
            // no comfort when you can see the other team has an extra dinosaur. Blue is built to
            // match Red's count, and if the budget cannot stretch that far both sides level down.
            var red = ChooseArmy(null);
            var blue = ChooseArmy(red.Count);

            int size = Mathf.Min(red.Count, blue.Count);
            red.RemoveRange(size, red.Count - size);
            blue.RemoveRange(size, blue.Count - size);

            PlaceArmy(red, Team.Red, 180f);
            PlaceArmy(blue, Team.Blue, 0f);
        }

        /// <summary>
        /// Give both sides the identical army, so the result reflects positioning and luck rather
        /// than the roster. Useful for sanity-checking balance changes.
        /// </summary>
        public void MirrorMatch()
        {
            if (battleManager == null) battleManager = BattleManager.Instance;
            if (battleManager == null || battleManager.Phase != BattlePhase.Placement) return;

            battleManager.Loadout.Clear();

            var picks = ChooseArmy(null);
            PlaceArmy(picks, Team.Red, 180f);
            PlaceArmy(picks, Team.Blue, 0f);
        }

        /// <summary>
        /// One enormous creature against a whole pack.
        ///
        /// The boss is placed alone at the far side and the players' side is filled with as many
        /// bodies as the roster allows rather than the usual budget, because the point of the mode is
        /// the swarm. It also happens to be where the pack AI is at its best: turn-taking, flanking
        /// and desperation were all written for exactly this shape of fight and rarely all fire at
        /// once in an even match.
        /// </summary>
        public void BossBattle() => BossBattle(null);

        /// <summary>
        /// Set up the boss fight with a specific boss, or a random one when <paramref name="boss"/>
        /// is null.
        ///
        /// The explicit overload exists for the balance probe. With one boss a random pick and a
        /// per-boss pick were the same thing; with four, twelve random battles give three each,
        /// which is far too few to tell a knife-edge matchup from a lopsided one.
        /// </summary>
        public void BossBattle(CreatureDefinition boss)
        {
            if (battleManager == null) battleManager = BattleManager.Instance;
            if (battleManager == null || battleManager.Phase != BattlePhase.Placement) return;
            if (bossRoster == null || bossRoster.Creatures.Count == 0)
            {
                Debug.LogWarning("[AutoPlacer] No boss roster assigned; run 'Dino Battle > 1. Generate Sample Content'.");
                return;
            }

            battleManager.Loadout.Clear();

            // Boss in the middle, hunters in a ring facing inward.
            //
            // Not the normal two-sides-of-a-field layout, and not by accident. Reusing that put the
            // boss on one bearing and the pack on another 90 degrees away — neither surrounded nor
            // opposed, just two groups standing oddly apart. A ring states the situation the moment
            // the screen appears: one thing in the middle, everything else closing on it. It also
            // gives the pack AI what it was written for, since the attackers already start spread
            // across every side rather than having to work their way around.
            boss ??= bossRoster.Creatures[Random.Range(0, bossRoster.Creatures.Count)];

            battleManager.Loadout.Add(new PlacedCreature
            {
                Definition = boss,
                Team = Team.Blue,
                Position = Vector3.zero,
                YawDegrees = 0f,
            });

            // The hunters ignore the budget: a boss is not something you buy your way past.
            var roster = battleManager.Roster;
            if (roster == null || roster.Creatures.Count == 0) return;

            float ring = arenaRadius * bossRingFactor;

            for (int i = 0; i < bossPackSize; i++)
            {
                float angle = i / (float)bossPackSize * Mathf.PI * 2f;
                Vector3 position = new(Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);

                battleManager.Loadout.Add(new PlacedCreature
                {
                    Definition = roster.Creatures[Random.Range(0, roster.Creatures.Count)],
                    Team = Team.Red,

                    // Every hunter looks at the middle, so the ring reads as a closing circle rather
                    // than as creatures that happen to be standing around one.
                    Position = position,
                    YawDegrees = Quaternion.LookRotation(-position.normalized, Vector3.up).eulerAngles.y,
                });
            }
        }

        /// <summary>Clear one team and refill only it, leaving the opponent alone.</summary>
        public void FillTeam(Team team, float facingDegrees)
        {
            if (battleManager == null || battleManager.Phase != BattlePhase.Placement) return;

            PlaceArmy(ChooseArmy(null), team, facingDegrees);
        }

        /// <summary>
        /// Spend the budget greedily on random affordable picks. Not an optimiser — a varied army is
        /// more interesting to watch than the mathematically best one, which would be the same every
        /// time.
        /// </summary>
        /// <param name="matchCount">
        /// Headcount to hit exactly, or null to simply spend the budget. When set, each pick leaves
        /// enough budget behind for the slots still to be filled — without that reservation a greedy
        /// first pick of something expensive makes the target count unreachable.
        /// </param>
        private List<CreatureDefinition> ChooseArmy(int? matchCount)
        {
            var picks = new List<CreatureDefinition>();
            var roster = battleManager.Roster;
            if (roster == null || roster.Creatures.Count == 0) return picks;

            int budget = battleManager.Loadout.BudgetPerTeam;
            int spent = 0;
            int limit = matchCount.HasValue ? Mathf.Min(maxPerTeam, matchCount.Value) : maxPerTeam;

            int cheapest = int.MaxValue;
            foreach (var candidate in roster.Creatures)
            {
                if (candidate != null) cheapest = Mathf.Min(cheapest, candidate.cost);
            }

            var affordable = new List<CreatureDefinition>();

            while (picks.Count < limit)
            {
                int reserve = matchCount.HasValue ? (limit - picks.Count - 1) * cheapest : 0;

                affordable.Clear();
                foreach (var candidate in roster.Creatures)
                {
                    if (candidate != null && spent + candidate.cost + reserve <= budget) affordable.Add(candidate);
                }

                if (affordable.Count == 0) break;

                var chosen = affordable[Random.Range(0, affordable.Count)];
                picks.Add(chosen);
                spent += chosen.cost;
            }

            return picks;
        }

        /// <summary>
        /// Lay the army out along an arc facing the centre, biggest at the front.
        ///
        /// Ordering by footprint keeps the heavy creatures between the enemy and the fragile ones,
        /// which is both how you would actually deploy and what stops a pack of raptors being
        /// deleted before the bruisers arrive.
        /// </summary>
        /// <summary>
        /// A clear spot on the given bearing, searched outward and inward from the ideal radius.
        ///
        /// Outward first: the deployment line faces the enemy, so pushing a crowded creature back
        /// deepens the formation, while pulling it forward would shove it out in front alone.
        /// </summary>
        private bool TryFindFreeSpot(float angle, float idealRadius, float footprint, out Vector3 position)
        {
            Vector3 ray = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float step = footprint * 0.9f + 0.4f;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                // 0, +1, -1, +2, -2 ... in steps along the ray. Integer division gives the pair index
                // and the sign alternates, so the search fans out evenly from the ideal spot.
                float offset = (attempt + 1) / 2 * step * (attempt % 2 == 1 ? 1f : -1f);

                float radius = idealRadius + offset;
                if (radius < footprint || radius > arenaRadius - footprint) continue;

                position = ray * radius;
                if (battleManager.Loadout.IsSpotFree(position, footprint)) return true;
            }

            position = ray * idealRadius;
            return false;
        }

        private void PlaceArmy(List<CreatureDefinition> army, Team team, float facingDegrees)
        {
            if (army.Count == 0) return;

            army.Sort((a, b) => b.footprintRadius.CompareTo(a.footprintRadius));

            float baseAngle = facingDegrees * Mathf.Deg2Rad;
            float arc = formationArcDegrees * Mathf.Deg2Rad;
            float lineRadius = arenaRadius * lineRadiusFactor;

            for (int i = 0; i < army.Count; i++)
            {
                var definition = army[i];

                // Spread across the arc; a single creature sits dead centre rather than at one end.
                float t = army.Count == 1 ? 0.5f : i / (float)(army.Count - 1);
                float angle = baseAngle + (t - 0.5f) * arc;

                // Alternate front and back rank so a wide army does not become one thin line.
                float rank = i % 2 == 0 ? 1f : 0.82f;
                float radius = lineRadius * rank;

                Vector3 position = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                // Shuffle along the ray rather than dropping the creature.
                //
                // This used to skip any pick whose slot was taken, which quietly deleted units the
                // budget had already been spent on. It also defeated the equal-headcount rule: the
                // two armies have different footprints, so they collided a different number of times
                // and 5-a-side came out as 4 against 5 on the field.
                if (!TryFindFreeSpot(angle, radius, definition.footprintRadius, out position)) continue;

                battleManager.Loadout.Add(new PlacedCreature
                {
                    Definition = definition,
                    Team = team,
                    Position = position,
                    YawDegrees = Quaternion.LookRotation(-position.normalized, Vector3.up).eulerAngles.y,
                });
            }
        }
    }
}
