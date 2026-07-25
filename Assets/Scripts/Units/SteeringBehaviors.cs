using System.Collections.Generic;
using DinoBattle.Core;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>
    /// Craig Reynolds' steering behaviours (1999), the standard toolkit for autonomous agent movement
    /// in games. Each function returns a desired velocity; callers blend them and hand the result to
    /// <see cref="CreatureLocomotion"/>.
    ///
    /// Why these three in particular:
    ///   Arrive     — decelerates into the goal. Plain Seek runs at full speed until it overshoots,
    ///                then overshoots back, which is what made creatures jitter once they closed.
    ///   Pursue     — aims at where the target will be, not where it is. Chasing a stale position
    ///                leaves an attacker permanently trailing anything that moves.
    ///   Separation — pushes neighbours apart. Without it a pack collapses onto one point and the
    ///                fight becomes a single pile; with it they spread and surround.
    ///
    /// Pure math, no component state, so it stays testable and cheap to call per creature per frame.
    /// </summary>
    public static class SteeringBehaviors
    {
        /// <summary>Full-speed run straight at a point.</summary>
        public static Vector3 Seek(Vector3 position, Vector3 target, float maxSpeed)
        {
            Vector3 offset = target - position;
            offset.y = 0f;

            return offset.sqrMagnitude < 0.0001f ? Vector3.zero : offset.normalized * maxSpeed;
        }

        /// <summary>
        /// Seek's opposite: full-speed run directly away from a point.
        ///
        /// Used by the light creatures' hit-and-run, where backing off after a bite is half the
        /// behaviour. Note this steers away without turning away — the caller decides which way the
        /// creature looks, so a raptor can retreat while still watching what it just bit.
        /// </summary>
        public static Vector3 Flee(Vector3 position, Vector3 threat, float maxSpeed)
        {
            Vector3 offset = position - threat;
            offset.y = 0f;

            return offset.sqrMagnitude < 0.0001f ? Vector3.zero : offset.normalized * maxSpeed;
        }

        /// <summary>
        /// Seek that eases off inside <paramref name="slowingRadius"/> and stops inside
        /// <paramref name="arriveRadius"/>.
        /// </summary>
        public static Vector3 Arrive(Vector3 position, Vector3 target, float maxSpeed,
            float slowingRadius, float arriveRadius = 0.2f)
        {
            Vector3 offset = target - position;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (distance <= arriveRadius) return Vector3.zero;

            float speed = distance >= slowingRadius
                ? maxSpeed
                : maxSpeed * (distance - arriveRadius) / Mathf.Max(0.0001f, slowingRadius - arriveRadius);

            return offset / distance * Mathf.Max(0f, speed);
        }

        /// <summary>
        /// Arrive at where the target is heading. Prediction time scales with distance so a far-off
        /// target is led generously while a neighbour is not over-anticipated.
        /// </summary>
        public static Vector3 Pursue(Vector3 position, Vector3 targetPosition, Vector3 targetVelocity,
            float maxSpeed, float slowingRadius, float maxPredictionSeconds = 0.6f)
        {
            Vector3 offset = targetPosition - position;
            offset.y = 0f;

            float prediction = maxSpeed > 0.0001f
                ? Mathf.Min(maxPredictionSeconds, offset.magnitude / maxSpeed)
                : 0f;

            Vector3 predicted = targetPosition + targetVelocity * prediction;
            predicted.y = targetPosition.y;

            return Arrive(position, predicted, maxSpeed, slowingRadius);
        }

        /// <summary>
        /// Repulsion from nearby units, weighted by 1/distance so contact pushes hard and distant
        /// neighbours barely register. Returns a velocity, not a normalized direction, so it can be
        /// summed with the other behaviours directly.
        /// </summary>
        /// <param name="ignore">
        /// The creature currently being attacked, which must NOT push back. Separation is there to
        /// stop a pack collapsing onto one point; applied to your own target it becomes a force field
        /// around the thing you are trying to reach. With a 3.5 radius that is exactly what happened —
        /// attackers closed to about 3.2 and were held there, which on screen looks like an invisible
        /// wall between the two creatures.
        /// </param>
        public static Vector3 Separation(CreatureUnit self, float radius, float maxSpeed, CreatureUnit ignore = null)
        {
            if (self == null || radius <= 0f) return Vector3.zero;

            Vector3 push = Vector3.zero;
            int counted = 0;

            AccumulateSeparation(self, UnitRegistry.AliveOf(Team.Red), radius, ignore, ref push, ref counted);
            AccumulateSeparation(self, UnitRegistry.AliveOf(Team.Blue), radius, ignore, ref push, ref counted);

            if (counted == 0) return Vector3.zero;

            push /= counted;
            return push.sqrMagnitude < 0.0001f ? Vector3.zero : push.normalized * maxSpeed;
        }

        private static void AccumulateSeparation(CreatureUnit self, IReadOnlyList<CreatureUnit> others,
            float radius, CreatureUnit ignore, ref Vector3 push, ref int counted)
        {
            Vector3 origin = self.transform.position;
            float radiusSqr = radius * radius;

            for (int i = 0; i < others.Count; i++)
            {
                var other = others[i];
                if (other == null || other == self || other == ignore || other.IsDead) continue;

                Vector3 away = origin - other.transform.position;
                away.y = 0f;

                float distanceSqr = away.sqrMagnitude;
                if (distanceSqr >= radiusSqr || distanceSqr < 0.0001f) continue;

                push += away.normalized / Mathf.Sqrt(distanceSqr);
                counted++;
            }
        }

        /// <summary>
        /// Blend steering outputs and clamp to <paramref name="maxSpeed"/>. Weighted truncated sum —
        /// the simplest combination Reynolds describes, and sufficient when the behaviours rarely
        /// contradict each other.
        /// </summary>
        public static Vector3 Blend(float maxSpeed, params (Vector3 velocity, float weight)[] parts)
        {
            Vector3 total = Vector3.zero;
            foreach (var (velocity, weight) in parts) total += velocity * weight;

            total.y = 0f;
            return Vector3.ClampMagnitude(total, maxSpeed);
        }
    }
}
