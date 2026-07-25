using System.Collections.Generic;
using DinoBattle.Core;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Hands out attack turns so a pack harasses one at a time instead of all at once.
    ///
    /// Without this, hit-and-run scales badly: five raptors on one T-Rex all dive in together, all
    /// retreat together, and the whole pack is out of contact for the same second. It looks like a
    /// flock startling rather than animals hunting, and it hands the big creature a free window.
    ///
    /// Taking turns inverts that. One raptor is committed at any moment while the rest circle at a
    /// distance, so there is always something at the target's flank and the pressure never lifts.
    /// It also makes the big creature's problem the intended one: whatever it locks onto is already
    /// leaving by the time the swing lands, and the next attacker is coming from somewhere else.
    ///
    /// A turn expires on its own as well as being handed back. A holder that dies mid-lunge, or gets
    /// knocked out of range, must not be able to stall the pack by never releasing.
    /// </summary>
    public static class PackTactics
    {
        private struct Claim
        {
            public CreatureBrain Holder;
            public float ExpiresAt;
        }

        private static readonly Dictionary<CreatureUnit, Claim> claims = new();

        /// <summary>
        /// How many living creatures on <paramref name="team"/> are currently working on
        /// <paramref name="target"/>. Walks the team list rather than keeping a registration count:
        /// packs are small, the list is already there, and a count maintained on the side is one
        /// more thing that can drift out of sync with reality.
        /// </summary>
        public static int AttackersOn(CreatureUnit target, Team team)
        {
            if (target == null) return 0;

            var mates = UnitRegistry.AliveOf(team);
            int count = 0;

            for (int i = 0; i < mates.Count; i++)
            {
                var mate = mates[i];
                if (mate == null || mate.IsDead) continue;

                var brain = mate.Brain;
                if (brain != null && brain.Target == target) count++;
            }

            return count;
        }

        /// <summary>
        /// True if <paramref name="claimant"/> may commit to an attack run right now. Claims the turn
        /// when it is free, so the first caller of a frame takes it and the rest wait.
        /// </summary>
        public static bool TryTakeTurn(CreatureUnit target, CreatureBrain claimant, float turnSeconds)
        {
            if (target == null || claimant == null) return true;

            if (claims.TryGetValue(target, out var claim))
            {
                if (claim.Holder == claimant) return true;

                bool stale = claim.Holder == null
                             || Time.time >= claim.ExpiresAt
                             || claim.Holder.Current == CreatureBrain.State.Dead;

                if (!stale) return false;
            }

            claims[target] = new Claim { Holder = claimant, ExpiresAt = Time.time + turnSeconds };
            return true;
        }

        /// <summary>Give the turn back after a strike, so the next attacker can go in.</summary>
        public static void EndTurn(CreatureUnit target, CreatureBrain holder)
        {
            if (target == null) return;
            if (claims.TryGetValue(target, out var claim) && claim.Holder == holder) claims.Remove(target);
        }

        /// <summary>
        /// Wipe all turns. Static state outlives scene loads exactly like <see cref="UnitRegistry"/>,
        /// so a new match must start with nothing held by creatures that no longer exist.
        /// </summary>
        public static void Clear() => claims.Clear();
    }
}
