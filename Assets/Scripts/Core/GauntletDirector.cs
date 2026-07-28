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

        /// <summary>
        /// Where each monster was posted, so it can go back after it has seen off a wave.
        ///
        /// Reported: "전투중 사다리에 있는 적이 이긴 후에 막 달려다닐때가 있음." A fight scatters them
        /// across the platform — they chase, they get shoved, they end up wherever the last kill
        /// happened — and with nothing left to fight they simply stopped there. The next wave then
        /// arrived to find the tier's defenders milling around the ramp mouth instead of waiting in
        /// formation, which looks like the AI has lost the plot.
        /// </summary>
        private readonly Dictionary<CreatureUnit, Vector3> posts = new();

        private readonly List<CreatureUnit> wave = new();
        private float advanceTimer;

        public GauntletState State { get; private set; } = GauntletState.Ready;
        public int CurrentTier { get; private set; }
        public int TierCount => ladder != null ? ladder.TierCount : 0;
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
                    posts[unit] = position;

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

            // Keyed on units that are about to be destroyed, so it has to go with them. Left behind
            // it would grow by a full board's worth of dead references on every run.
            posts.Clear();
        }

        // ---------------------------------------------------------------- waves

        /// <summary>
        /// Send what the player has arranged. Unlimited — a run ends by reaching the boss, not by
        /// running out of anything.
        /// </summary>
        public bool SendWave()
        {
            if (!CanSendWave || battleManager == null) return false;

            var loadout = battleManager.Loadout;
            if (loadout.CountFor(Team.Red) == 0) return false;

            // Waves are unlimited. The design argued for a run budget on the grounds that a mode you
            // cannot fail has no tension, and the owner's call was that the tension should come from
            // the ladder rather than from being cut off — a climb you can keep attempting is a
            // different, gentler game, and that is the one being built.
            //
            // What remains as the record of a run is how far up you got and how many waves it took.
            WavesSent++;

            wave.Clear();
            Vector3 start = ReinforcementPoint();

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

        /// <summary>
        /// Where a reinforcing wave comes in: the tier below the one being fought over.
        ///
        /// Reported: "추가 공룡 보낼때 너무 멀리서 보내지말고 바로 이전 층에서 보낼수있도록하자."
        /// Sending every wave from the foot of the board meant that losing on tier seven cost a walk
        /// past six cleared, empty platforms before anything happened — the punishment for dying was
        /// boredom rather than difficulty, and it got worse the better the player was doing.
        ///
        /// One tier back rather than the contested tier itself, so a wave still arrives from below
        /// and climbs into the fight. Spawning directly onto the tier under attack would drop
        /// reinforcements into the middle of the defenders with no approach at all.
        /// </summary>
        private Vector3 ReinforcementPoint()
        {
            var previous = arena.Tier(CurrentTier - 1);
            if (previous != null) return previous.ObjectivePosition;

            return arena.StartPlatform != null ? arena.StartPlatform.position : Vector3.zero;
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

            // Send the defenders back to their posts. They have just chased a wave all over the
            // platform and would otherwise stand wherever the last one died — usually clustered at
            // the ramp mouth, which both looks like confusion and ambushes the next wave at the
            // exact moment it is most helpless.
            //
            // A march order, not a teleport: they walk back, and because MarchTarget is only a
            // fallback for having no target, the moment the next wave arrives they abandon it and
            // fight. Arrive stops them at the post rather than orbiting it.
            ReturnToPosts();

            // Everything the player sent is dead. Not a defeat unless they cannot afford to try
            // again — that judgement needs the loadout, which the HUD rebuilds, so it is made when
            // the button is pressed rather than guessed at here.
            SetState(GauntletState.WaveWiped);
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

        /// <summary>
        /// Order every surviving monster back to where it was posted.
        ///
        /// Applied to all tiers, not just the current one. A tier behind the front can still have
        /// survivors — the wave is allowed to lose creatures and press on — and those should also be
        /// standing where they were put rather than at whatever spot they last fought on.
        /// </summary>
        private void ReturnToPosts()
        {
            foreach (var tier in tierMonsters)
            {
                foreach (var unit in tier)
                {
                    if (unit == null || unit.IsDead) continue;
                    if (!unit.gameObject.activeInHierarchy) continue;
                    if (!posts.TryGetValue(unit, out Vector3 post)) continue;

                    foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                        brain.MarchTarget = post;
                }
            }
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
