using System.Collections.Generic;
using UnityEngine;

namespace DinoBattle.Core
{
    /// <summary>
    /// The board. Holds the tiers in climb order and the platform the player's creatures start on.
    ///
    /// Lives alongside the versus arena in the same scene and is switched off unless the gauntlet is
    /// selected. Two arenas resident beats loading a second scene: inactive renderers cost nothing,
    /// and it keeps both layouts inside one generated artefact a diff can review.
    ///
    /// <see cref="GauntletTier"/> is in its own file, and has to be — Unity only binds a MonoBehaviour
    /// to a script asset when the file name matches the class name.
    /// </summary>
    public class GauntletArena : MonoBehaviour
    {
        [SerializeField] private List<GauntletTier> tiers = new();
        [SerializeField] private Transform startPlatform;

        public IReadOnlyList<GauntletTier> Tiers => tiers;
        public Transform StartPlatform => startPlatform;
        public int TierCount => tiers.Count;

        public GauntletTier Tier(int index) =>
            index >= 0 && index < tiers.Count ? tiers[index] : null;

        /// <summary>Editor-only wiring, called by the scene builder.</summary>
        public void Configure(List<GauntletTier> orderedTiers, Transform start)
        {
            tiers = orderedTiers;
            startPlatform = start;
        }
    }
}
