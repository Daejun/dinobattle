using System.Collections.Generic;
using DinoBattle.Core;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Tracks every living creature by team so target lookup is a list walk instead of a
    /// physics overlap query every frame. Creatures register in OnEnable and drop out on death.
    /// </summary>
    public static class UnitRegistry
    {
        private static readonly Dictionary<Team, List<CreatureUnit>> alive = new()
        {
            { Team.Red, new List<CreatureUnit>() },
            { Team.Blue, new List<CreatureUnit>() },
            { Team.Neutral, new List<CreatureUnit>() }
        };

        public static void Register(CreatureUnit unit)
        {
            if (unit == null) return;
            var list = alive[unit.Team];
            if (!list.Contains(unit)) list.Add(unit);
        }

        public static void Unregister(CreatureUnit unit)
        {
            if (unit == null) return;
            alive[unit.Team].Remove(unit);
        }

        public static IReadOnlyList<CreatureUnit> AliveOf(Team team) => alive[team];

        /// <summary>
        /// Living units on <paramref name="team"/>. Counts entries rather than returning Count so a
        /// stale record — a unit destroyed without OnDisable running, say — cannot stall the win
        /// condition by making a wiped-out team look like it still has fighters.
        /// </summary>
        public static int AliveCount(Team team)
        {
            var list = alive[team];
            int count = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var unit = list[i];
                if (unit != null && !unit.IsDead) count++;
            }

            return count;
        }

        /// <summary>
        /// Nearest living enemy within <paramref name="maxRange"/>, or null. Squared distances only,
        /// so this is cheap enough for every creature to call on a retarget tick.
        /// </summary>
        public static CreatureUnit FindNearestEnemy(CreatureUnit self, float maxRange)
        {
            if (self == null) return null;

            var enemies = alive[self.Team.Opponent()];
            CreatureUnit best = null;
            float bestSqr = maxRange * maxRange;
            Vector3 origin = self.transform.position;

            for (int i = 0; i < enemies.Count; i++)
            {
                var candidate = enemies[i];
                if (candidate == null || candidate.IsDead) continue;

                float sqr = (candidate.transform.position - origin).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = candidate;
            }

            return best;
        }

        /// <summary>Wipe all tracking. Call before spawning a new match — static state outlives scene loads.</summary>
        public static void Clear()
        {
            foreach (var list in alive.Values) list.Clear();
        }
    }
}
