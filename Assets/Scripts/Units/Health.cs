using System;
using UnityEngine;

namespace DinoBattle.Units
{
    /// <summary>Hit points for anything that can be attacked.</summary>
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 1000f;
        [SerializeField] private float armor;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public float Normalized => maxHealth <= 0f ? 0f : Mathf.Clamp01(Current / maxHealth);

        /// <summary>Raised with the damage actually applied after armor.</summary>
        public event Action<float> Damaged;

        /// <summary>Raised exactly once, the frame health first reaches zero.</summary>
        public event Action Died;

        private void Awake()
        {
            if (Current <= 0f) Current = maxHealth;
        }

        /// <summary>Called by the spawner so stats come from the creature definition, not the prefab.</summary>
        public void Configure(float newMax, float newArmor)
        {
            maxHealth = Mathf.Max(1f, newMax);
            armor = Mathf.Max(0f, newArmor);
            Current = maxHealth;
        }

        public void TakeDamage(float rawAmount)
        {
            if (IsDead || rawAmount <= 0f) return;

            // Armor never makes a creature fully immune; chip damage always gets through.
            float applied = Mathf.Max(1f, rawAmount - armor);
            Current = Mathf.Max(0f, Current - applied);
            Damaged?.Invoke(applied);

            if (Current <= 0f) Died?.Invoke();
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Min(maxHealth, Current + amount);
        }
    }
}
