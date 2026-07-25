using System.Collections.Generic;
using UnityEngine;

namespace DinoBattle.Data
{
    /// <summary>The set of creatures the player can pick from during placement.</summary>
    [CreateAssetMenu(menuName = "Dino Battle/Creature Roster", fileName = "Roster_Default")]
    public class CreatureRoster : ScriptableObject
    {
        [SerializeField] private List<CreatureDefinition> creatures = new();

        public IReadOnlyList<CreatureDefinition> Creatures => creatures;

        public CreatureDefinition Get(int index) =>
            index >= 0 && index < creatures.Count ? creatures[index] : null;

        public CreatureDefinition FindByName(string displayName)
        {
            foreach (var c in creatures)
            {
                if (c != null && c.displayName == displayName) return c;
            }
            return null;
        }
    }
}
