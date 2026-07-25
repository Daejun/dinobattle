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

            FillTeam(Team.Red, 180f);
            FillTeam(Team.Blue, 0f);
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

            var picks = ChooseArmy();
            PlaceArmy(picks, Team.Red, 180f);
            PlaceArmy(picks, Team.Blue, 0f);
        }

        /// <summary>Clear one team and refill only it, leaving the opponent alone.</summary>
        public void FillTeam(Team team, float facingDegrees)
        {
            if (battleManager == null || battleManager.Phase != BattlePhase.Placement) return;

            PlaceArmy(ChooseArmy(), team, facingDegrees);
        }

        /// <summary>
        /// Spend the budget greedily on random affordable picks. Not an optimiser — a varied army is
        /// more interesting to watch than the mathematically best one, which would be the same every
        /// time.
        /// </summary>
        private List<CreatureDefinition> ChooseArmy()
        {
            var picks = new List<CreatureDefinition>();
            var roster = battleManager.Roster;
            if (roster == null || roster.Creatures.Count == 0) return picks;

            int budget = battleManager.Loadout.BudgetPerTeam;
            int spent = 0;

            var affordable = new List<CreatureDefinition>();

            while (picks.Count < maxPerTeam)
            {
                affordable.Clear();
                foreach (var candidate in roster.Creatures)
                {
                    if (candidate != null && spent + candidate.cost <= budget) affordable.Add(candidate);
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

                if (!battleManager.Loadout.IsSpotFree(position, definition.footprintRadius)) continue;

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
