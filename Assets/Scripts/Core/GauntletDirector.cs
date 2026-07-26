using System;
using System.Collections.Generic;
using DinoBattle.Data;
using DinoBattle.Units;
using UnityEngine;

namespace DinoBattle.Core
{
    /// <summary>
    /// Runs a gauntlet: sends the player's creatures up a board of tiers, wakes each tier's monsters
    /// as they arrive, and asks for another wave when the ones already sent are dead.
    ///
    /// Owns <see cref="GauntletState"/>. It does NOT add cases to <see cref="BattlePhase"/> — a run
    /// stays in <see cref="BattlePhase.Fighting"/> from the first wave to the boss, so the HUD, the
    /// music, the camera director and the victory dance all keep working with no knowledge that this
    /// mode exists.
    ///
    /// Like everything else here, other systems subscribe rather than poll.
    /// </summary>
    [DisallowMultipleComponent]
    public class GauntletDirector : MonoBehaviour
    {
        [SerializeField] private GauntletLadder ladder;
        [SerializeField] private GauntletArena arena;

        [Tooltip("How close the leading creature must get to a tier's objective before that tier's " +
                 "monsters wake up. Generous: waking them late is a wave walking into a standing " +
                 "ambush, which reads better than monsters popping on in plain sight.")]
        [SerializeField] private float tierTriggerRadius = 14f;

        [Tooltip("Seconds after a tier is cleared before the wave is ordered onward, so a kill is " +
                 "not immediately followed by everyone turning their back on it.")]
        [SerializeField] private float advanceDelay = 1.2f;

        private CreatureSpawner spawner;
        private BattleManager battleManager;

        /// <summary>Monsters for every tier, spawned up front and switched on tier by tier.</summary>
        private readonly List<List<CreatureUnit>> tierMonsters = new();

        private readonly List<CreatureUnit> wave = new();
        private float advanceTimer;

        public GauntletState State { get; private set; } = GauntletState.Ready;
        public int CurrentTier { get; private set; }
        public int TierCount => ladder != null ? ladder.TierCount : 0;
        public int BudgetRemaining { get; private set; }
        public int WavesSent { get; private set; }

        public event Action<GauntletState> StateChanged;
        public event Action<int> TierChanged;

        /// <summary>True when the player may send another wave: everything they sent is dead.</summary>
        public bool CanSendWave =>
            State is GauntletState.Ready or GauntletState.WaveWiped;

        private void Awake()
        {
            spawner = GetComponent<CreatureSpawner>();
            battleManager = GetComponent<BattleManager>();
        }

        // ---------------------------------------------------------------- run lifecycle

        /// <summary>Tear down anything left over and set up a fresh climb.</summary>
        public void BeginRun()
        {
            if (ladder == null || arena == null)
            {
                Debug.LogError("[GauntletDirector] No ladder or arena assigned — rebuild the scene.");
                return;
            }

            ClearMonsters();
            wave.Clear();

            CurrentTier = 0;
            WavesSent = 0;
            BudgetRemaining = ladder.RunBudget;

            PreSpawnAllTiers();

            SetState(GauntletState.Ready);
            TierChanged?.Invoke(CurrentTier);
        }

        public void EndRun()
        {
            ClearMonsters();
            wave.Clear();
            SetState(GauntletState.Ready);
        }

        /// <summary>
        /// Create every tier's monsters now, switched off.
        ///
        /// Two reasons, and the second is the one that matters. Instantiating mid-fight costs a frame
        /// hitch, which is avoidable. But more importantly, <see cref="CreatureUnit"/> registers with
        /// <see cref="UnitRegistry"/> in OnEnable — so an inactive tier is simply not in the registry,
        /// and <c>FindNearestEnemy</c> cannot see it. That is the whole tier-scoping problem solved
        /// without touching a line of targeting code.
        ///
        /// It has to be solved: aggroRange on every generated creature is 90, and the board is
        /// roughly 290 units long. Left visible, a creature on tier one would pick a target on tier
        /// seven and walk the whole way to it.
        /// </summary>
        private void PreSpawnAllTiers()
        {
            for (int i = 0; i < ladder.TierCount; i++)
            {
                var spec = ladder.Tier(i);
                var tier = arena.Tier(i);
                var monsters = new List<CreatureUnit>();
                tierMonsters.Add(monsters);

                if (spec == null || tier == null || spec.species.Count == 0) continue;

                var points = tier.SpawnPoints;
                for (int n = 0; n < spec.count; n++)
                {
                    // Round-robin rather than random, so a mixed tier is actually mixed instead of
                    // occasionally being nine of the same thing.
                    var definition = spec.species[n % spec.species.Count];
                    if (definition == null) continue;

                    Vector3 position = points.Count > 0
                        ? points[n % points.Count].position
                        : tier.ObjectivePosition;

                    var unit = spawner.Spawn(new PlacedCreature
                    {
                        Definition = definition,
                        Team = Team.Blue,
                        Position = position,
                        YawDegrees = 180f,
                    }, spec.healthScale, spec.damageScale);

                    if (unit == null) continue;

                    unit.Died += HandleMonsterDied;
                    monsters.Add(unit);

                    // Off, and therefore out of the registry, until this tier is reached.
                    unit.gameObject.SetActive(false);
                }
            }
        }

        private void ClearMonsters()
        {
            foreach (var tier in tierMonsters)
                foreach (var unit in tier)
                    if (unit != null) unit.Died -= HandleMonsterDied;

            tierMonsters.Clear();
        }

        // ---------------------------------------------------------------- waves

        /// <summary>
        /// Send what the player has arranged, spending its cost from the run budget.
        ///
        /// Returns false when it cannot be afforded, which is how a run is lost: no budget for
        /// another wave, and nothing of yours left alive.
        /// </summary>
        public bool SendWave()
        {
            if (!CanSendWave || battleManager == null) return false;

            var loadout = battleManager.Loadout;
            int cost = loadout.SpentBy(Team.Red);
            if (cost <= 0) return false;

            if (cost > BudgetRemaining)
            {
                Debug.Log($"[GauntletDirector] Wave costs {cost}, only {BudgetRemaining} left.");
                return false;
            }

            BudgetRemaining -= cost;
            WavesSent++;

            wave.Clear();
            Vector3 start = arena.StartPlatform != null ? arena.StartPlatform.position : Vector3.zero;

            int index = 0;
            foreach (var placement in loadout.Placements)
            {
                if (placement.Team != Team.Red) continue;

                // Spread the wave across the start platform rather than trusting wherever the player
                // tapped on the versus arena, which is a different piece of geometry entirely.
                var placed = placement;
                placed.Position = start + StartOffset(index++);

                var unit = spawner.Spawn(placed);
                if (unit == null) continue;

                unit.Died += HandleFighterDied;
                wave.Add(unit);

                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.CombatEnabled = true;
            }

            if (wave.Count == 0) return false;

            OrderAdvance();
            return true;
        }

        private static Vector3 StartOffset(int index)
        {
            int row = index / 4;
            int column = index % 4;
            return new Vector3((column - 1.5f) * 3f, 0f, -row * 3f);
        }

        private void HandleFighterDied(CreatureUnit unit)
        {
            if (unit != null) unit.Died -= HandleFighterDied;
            wave.Remove(unit);

            if (AnyAlive(wave)) return;

            // Everything the player sent is dead. Not a defeat unless they cannot afford to try
            // again — that judgement needs the loadout, which the HUD rebuilds, so it is made when
            // the button is pressed rather than guessed at here.
            SetState(BudgetRemaining > 0 ? GauntletState.WaveWiped : GauntletState.Defeated);
        }

        private void HandleMonsterDied(CreatureUnit unit)
        {
            if (unit != null) unit.Died -= HandleMonsterDied;
            if (State != GauntletState.Engaging) return;
            if (CurrentTier >= tierMonsters.Count) return;
            if (AnyAlive(tierMonsters[CurrentTier])) return;

            if (IsBossTier(CurrentTier))
            {
                SetState(GauntletState.Cleared);
                return;
            }

            // Cleared. Give the survivors a beat before turning them around.
            advanceTimer = advanceDelay;
        }

        private void Update()
        {
            if (State is GauntletState.Cleared or GauntletState.Defeated) return;

            if (advanceTimer > 0f)
            {
                advanceTimer -= Time.deltaTime;
                if (advanceTimer <= 0f)
                {
                    CurrentTier++;
                    TierChanged?.Invoke(CurrentTier);
                    OrderAdvance();
                }

                return;
            }

            if (State == GauntletState.Advancing) CheckTierReached();
        }

        /// <summary>Point the wave at the current tier and let them walk.</summary>
        private void OrderAdvance()
        {
            var tier = arena.Tier(CurrentTier);
            if (tier == null)
            {
                SetState(GauntletState.Cleared);
                return;
            }

            Vector3 objective = tier.ObjectivePosition;
            foreach (var unit in wave)
            {
                if (unit == null || unit.IsDead) continue;
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.MarchTarget = objective;
            }

            SetState(GauntletState.Advancing);
        }

        /// <summary>
        /// Wake the current tier once the leader is close enough.
        ///
        /// The LEADER, not the average: a wave strings out badly on a long climb, and waiting for the
        /// group's centre would let the front runner walk into the middle of a sleeping tier.
        /// </summary>
        private void CheckTierReached()
        {
            var tier = arena.Tier(CurrentTier);
            if (tier == null) return;

            Vector3 objective = tier.ObjectivePosition;
            bool leaderArrived = false;

            foreach (var unit in wave)
            {
                if (unit == null || unit.IsDead) continue;

                Vector3 offset = unit.transform.position - objective;
                offset.y = 0f;
                if (offset.sqrMagnitude <= tierTriggerRadius * tierTriggerRadius) { leaderArrived = true; break; }
            }

            if (!leaderArrived) return;

            WakeTier(CurrentTier);
        }

        private void WakeTier(int index)
        {
            if (index < 0 || index >= tierMonsters.Count) return;

            var monsters = tierMonsters[index];
            int woken = 0;

            foreach (var unit in monsters)
            {
                if (unit == null) continue;

                unit.gameObject.SetActive(true);
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.CombatEnabled = true;

                woken++;
            }

            // An empty tier is not a bug worth stalling on — walk on through.
            if (woken == 0)
            {
                advanceTimer = 0.01f;
                return;
            }

            // Drop the march order so the wave fights here instead of trying to walk past.
            foreach (var unit in wave)
            {
                if (unit == null || unit.IsDead) continue;
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.MarchTarget = null;
            }

            SetState(GauntletState.Engaging);
        }

        private bool IsBossTier(int index)
        {
            var spec = ladder != null ? ladder.Tier(index) : null;
            return spec != null ? spec.isBoss : index >= TierCount - 1;
        }

        private static bool AnyAlive(List<CreatureUnit> units)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i] != null && !units[i].IsDead) return true;

            return false;
        }

        private void SetState(GauntletState next)
        {
            if (State == next) return;

            State = next;
            StateChanged?.Invoke(next);
        }

        /// <summary>Human-readable label for the tier the HUD should be showing.</summary>
        public string CurrentTierLabel
        {
            get
            {
                var spec = ladder != null ? ladder.Tier(CurrentTier) : null;
                if (spec != null && !string.IsNullOrEmpty(spec.label)) return spec.label;
                return $"{CurrentTier + 1}층";
            }
        }
    }
}
