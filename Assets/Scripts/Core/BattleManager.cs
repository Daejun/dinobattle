using System;
using System.Collections.Generic;
using DinoBattle.Data;
using DinoBattle.Units;
using UnityEngine;

namespace DinoBattle.Core
{
    /// <summary>
    /// Owns the match lifecycle: placement, the fight, and the result. Everything else in the game
    /// reacts to <see cref="PhaseChanged"/> rather than polling.
    /// </summary>
    [RequireComponent(typeof(CreatureSpawner))]
    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance { get; private set; }

        [Header("Setup")]
        [SerializeField] private CreatureRoster roster;
        [SerializeField] private int budgetPerTeam = 1000;

        [Header("Simulation speed")]
        [Tooltip("Speed multipliers the player can cycle through while watching.")]
        [SerializeField] private float[] speedSteps = { 0.25f, 0.5f, 1f, 2f, 4f };
        [SerializeField] private int defaultSpeedIndex = 2;

        private CreatureSpawner spawner;
        private readonly List<CreatureUnit> activeUnits = new();
        private int speedIndex;

        public BattlePhase Phase { get; private set; } = BattlePhase.Placement;

        /// <summary>
        /// Built at field-initialization rather than in Awake. Awake order between GameObjects is
        /// undefined, so a HUD whose OnEnable ran first used to hit a null Loadout and throw. The
        /// budget is applied in Awake once the serialized value is available.
        /// </summary>
        public BattleLoadout Loadout { get; } = new();
        public CreatureRoster Roster => roster;
        public Team Winner { get; private set; } = Team.Neutral;
        public float SimulationSpeed => speedSteps[Mathf.Clamp(speedIndex, 0, speedSteps.Length - 1)];

        /// <summary>Raised on every phase transition, including the initial move into Placement.</summary>
        public event Action<BattlePhase> PhaseChanged;

        /// <summary>Raised when the fight ends, carrying the winning team (Neutral on a mutual wipe).</summary>
        public event Action<Team> BattleEnded;

        /// <summary>Raised whenever a creature dies, so the HUD can refresh its team counters.</summary>
        public event Action UnitCountChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[BattleManager] A second instance was created; destroying the duplicate.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            spawner = GetComponent<CreatureSpawner>();
            Loadout.BudgetPerTeam = budgetPerTeam;
            speedIndex = Mathf.Clamp(defaultSpeedIndex, 0, speedSteps.Length - 1);
        }

        private void Start()
        {
            EnterPlacement();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // Never leave a slowed or fast-forwarded clock behind for the next scene.
            Time.timeScale = 1f;
        }

        // ---------------------------------------------------------------- phases

        /// <summary>Clear the arena and go back to dropping creatures.</summary>
        public void EnterPlacement()
        {
            Time.timeScale = 1f;
            Winner = Team.Neutral;

            DetachUnitEvents();
            activeUnits.Clear();
            UnitRegistry.Clear();
            PackTactics.Clear();
            spawner.DespawnAll();
            Loadout.Clear();

            SetPhase(BattlePhase.Placement);
            UnitCountChanged?.Invoke();
        }

        /// <summary>Spawn the loadout and let the AI take over. Requires creatures on both sides.</summary>
        public bool StartBattle()
        {
            if (Phase != BattlePhase.Placement) return false;

            if (!Loadout.IsReadyToFight)
            {
                Debug.Log("[BattleManager] Both teams need at least one creature before the fight can start.");
                return false;
            }

            UnitRegistry.Clear();
            PackTactics.Clear();
            activeUnits.Clear();

            foreach (var placement in Loadout.Placements)
            {
                var unit = spawner.Spawn(placement);
                if (unit == null) continue;

                unit.Died += HandleUnitDied;
                activeUnits.Add(unit);
            }

            foreach (var unit in activeUnits)
            {
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>()) brain.CombatEnabled = true;
            }

            SetPhase(BattlePhase.Fighting);
            ApplySimulationSpeed();
            UnitCountChanged?.Invoke();
            return true;
        }

        private void HandleUnitDied(CreatureUnit unit)
        {
            UnitCountChanged?.Invoke();

            if (Phase != BattlePhase.Fighting) return;

            int red = UnitRegistry.AliveCount(Team.Red);
            int blue = UnitRegistry.AliveCount(Team.Blue);
            if (red > 0 && blue > 0) return;

            Winner = red > 0 ? Team.Red : blue > 0 ? Team.Blue : Team.Neutral;

            // Stand the survivors down. StartBattle switches combat on but nothing ever switched it
            // back off, so the winning side kept running its full AI tick — and kept roaring — over a
            // result screen with nothing left to fight.
            foreach (var survivor in activeUnits)
            {
                if (survivor == null) continue;
                foreach (var brain in survivor.GetComponentsInChildren<CreatureBrain>()) brain.CombatEnabled = false;
            }

            Time.timeScale = 1f;
            SetPhase(BattlePhase.Finished);
            BattleEnded?.Invoke(Winner);
        }

        private void SetPhase(BattlePhase next)
        {
            Phase = next;
            PhaseChanged?.Invoke(next);
        }

        private void DetachUnitEvents()
        {
            foreach (var unit in activeUnits)
            {
                if (unit != null) unit.Died -= HandleUnitDied;
            }
        }

        // ---------------------------------------------------------------- speed

        public void CycleSpeed()
        {
            speedIndex = (speedIndex + 1) % speedSteps.Length;
            ApplySimulationSpeed();
        }

        public void SetPaused(bool paused)
        {
            Time.timeScale = paused ? 0f : SimulationSpeed;
        }

        private void ApplySimulationSpeed()
        {
            if (Phase != BattlePhase.Fighting) return;
            Time.timeScale = SimulationSpeed;
        }

        public int AliveCount(Team team) => UnitRegistry.AliveCount(team);
    }
}
