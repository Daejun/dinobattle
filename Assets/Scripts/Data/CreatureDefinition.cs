using UnityEngine;

namespace DinoBattle.Data
{
    /// <summary>
    /// Authoring data for one creature type. Everything the game needs to spawn and balance a
    /// dinosaur lives here so designers never have to touch prefabs to retune a matchup.
    /// </summary>
    [CreateAssetMenu(menuName = "Dino Battle/Creature Definition", fileName = "Creature_New")]
    public class CreatureDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Creature";
        [TextArea(2, 4)] public string description;
        public Sprite icon;

        [Tooltip("Prefab spawned for this creature. Must have a CreatureUnit component at its root.")]
        public GameObject prefab;

        [Header("Placement")]
        [Min(0)] public int cost = 100;
        [Tooltip("Radius used to keep creatures from being placed inside each other.")]
        [Min(0.1f)] public float footprintRadius = 1.5f;

        [Header("Vitals")]
        [Min(1f)] public float maxHealth = 1000f;
        [Tooltip("Flat damage subtracted from every incoming hit. A hit always deals at least 1.")]
        [Min(0f)] public float armor;

        [Header("Movement")]
        [Min(0f)] public float moveSpeed = 6f;
        [Min(1f)] public float turnSpeedDegrees = 180f;
        [Tooltip("Rigidbody mass. Heavier creatures shove lighter ones around on contact.")]
        [Min(1f)] public float mass = 800f;

        [Header("Combat")]
        [Min(0f)] public float attackDamage = 120f;
        [Tooltip("Seconds between the start of one attack and the start of the next.")]
        [Min(0.05f)] public float attackInterval = 1.4f;
        [Tooltip("Distance from this creature's aim point to the target's aim point to swing.")]
        [Min(0.1f)] public float attackRange = 3.5f;
        [Tooltip("Delay between the attack animation starting and damage landing.")]
        [Min(0f)] public float attackWindup = 0.35f;
        [Tooltip("How far this creature will look for an enemy to chase.")]
        [Min(1f)] public float aggroRange = 80f;

        /// <summary>Sustained damage per second, ignoring windup. Handy for balance spreadsheets.</summary>
        public float DamagePerSecond => attackInterval <= 0f ? 0f : attackDamage / attackInterval;

        /// <summary>Rough power score. Used by the auto-balance helper and the roster UI sort.</summary>
        public float PowerScore => Mathf.Sqrt(Mathf.Max(1f, maxHealth + armor * 20f) * Mathf.Max(1f, DamagePerSecond));

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
            if (attackWindup > attackInterval) attackWindup = attackInterval;
        }
    }
}
