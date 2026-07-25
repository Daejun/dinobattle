using System.Collections.Generic;
using DinoBattle.Core;
using UnityEngine;

namespace DinoBattle.Data
{
    /// <summary>One creature the player has dropped onto the arena, before the fight starts.</summary>
    public struct PlacedCreature
    {
        public CreatureDefinition Definition;
        public Team Team;
        public Vector3 Position;
        public float YawDegrees;
    }

    /// <summary>
    /// The pending arrangement for a match. Built up during <see cref="BattlePhase.Placement"/>,
    /// consumed by the battle manager when the player presses Start.
    /// </summary>
    public class BattleLoadout
    {
        private readonly List<PlacedCreature> placements = new();

        public IReadOnlyList<PlacedCreature> Placements => placements;

        public int BudgetPerTeam { get; set; } = 1000;

        /// <summary>
        /// Raised whenever the arrangement changes. Anything that mutates the loadout goes through
        /// this class, so a single event here is enough for the arena to keep its preview creatures
        /// in step without anyone having to remember to notify it.
        /// </summary>
        public event System.Action Changed;

        public void Add(PlacedCreature placement)
        {
            placements.Add(placement);
            Changed?.Invoke();
        }

        public void RemoveLast(Team team)
        {
            for (int i = placements.Count - 1; i >= 0; i--)
            {
                if (placements[i].Team != team) continue;

                placements.RemoveAt(i);
                Changed?.Invoke();
                return;
            }
        }

        public void Clear()
        {
            placements.Clear();
            Changed?.Invoke();
        }

        public int SpentBy(Team team)
        {
            int total = 0;
            foreach (var p in placements)
            {
                if (p.Team == team && p.Definition != null) total += p.Definition.cost;
            }
            return total;
        }

        public int RemainingFor(Team team) => BudgetPerTeam - SpentBy(team);

        public int CountFor(Team team)
        {
            int n = 0;
            foreach (var p in placements)
            {
                if (p.Team == team) n++;
            }
            return n;
        }

        public bool CanAfford(Team team, CreatureDefinition definition) =>
            definition != null && definition.cost <= RemainingFor(team);

        /// <summary>True once both sides have at least one creature, so a fight is winnable.</summary>
        public bool IsReadyToFight => CountFor(Team.Red) > 0 && CountFor(Team.Blue) > 0;

        /// <summary>Is this spot far enough from every already-placed creature?</summary>
        public bool IsSpotFree(Vector3 position, float radius)
        {
            foreach (var p in placements)
            {
                float minDistance = radius + (p.Definition != null ? p.Definition.footprintRadius : 1f);
                Vector3 delta = p.Position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude < minDistance * minDistance) return false;
            }
            return true;
        }
    }
}
