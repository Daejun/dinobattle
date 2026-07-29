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

        [Header("Result")]
        [Tooltip("Longest the result waits for swings already in the air before judging anyway. A " +
                 "backstop only — normally the wait ends as soon as the last swing resolves.")]
        [SerializeField] private float verdictGrace = 1.5f;

        private CreatureSpawner spawner;
        private GauntletDirector gauntlet;
        private readonly List<CreatureUnit> activeUnits = new();
        private int speedIndex;

        /// <summary>One side has been wiped out, but attacks already committed have not landed yet.</summary>
        private bool awaitingVerdict;
        private float verdictDeadline;

        /// <summary>
        /// Total health each side started the match with.
        ///
        /// The denominator has to be what the team STARTED with, not what its survivors are worth now.
        /// Measured against the living total, a team's bar would climb every time one of its own died,
        /// which is precisely backwards.
        /// </summary>
        private readonly Dictionary<Team, float> startingHealth = new();

        public BattlePhase Phase { get; private set; } = BattlePhase.Placement;

        /// <summary>
        /// Which mode the next match runs under. Only changeable during placement — switching arenas
        /// mid-fight would leave creatures standing on geometry that had just been switched off.
        /// </summary>
        public GameMode Mode { get; private set; } = GameMode.Versus;

        /// <summary>Raised when the player toggles the mode, so the HUD and arenas can follow.</summary>
        public event Action<GameMode> ModeChanged;

        /// <summary>
        /// Can the fight start with what is arranged now?
        ///
        /// Lives here rather than in the HUD because <see cref="StartBattle"/> already answers the
        /// same question to decide whether to run, and the two must not be able to disagree. They
        /// did: the button asked <c>Loadout.IsReadyToFight</c>, which requires a creature on BOTH
        /// sides, and a gauntlet only ever places one — the board supplies the opposition. So the
        /// start button was permanently dead in the new mode while StartBattle would have been happy
        /// to run.
        /// </summary>
        public bool CanStartBattle => Mode == GameMode.Gauntlet
            ? Loadout.CountFor(Team.Red) > 0
            : Loadout.IsReadyToFight;

        public void SetMode(GameMode mode)
        {
            if (Mode == mode || Phase != BattlePhase.Placement) return;

            Mode = mode;
            Loadout.Clear();

            if (gauntlet != null) gauntlet.EndRun();

            UnitRegistry.Clear();
            PackTactics.Clear();
            spawner.DespawnAll();

            ModeChanged?.Invoke(mode);
            UnitCountChanged?.Invoke();
        }

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
            gauntlet = GetComponent<GauntletDirector>();
            Loadout.BudgetPerTeam = budgetPerTeam;
            Loadout.Changed += HandleLoadoutChanged;
            speedIndex = Mathf.Clamp(defaultSpeedIndex, 0, speedSteps.Length - 1);
        }

        private void Start()
        {
            EnterPlacement();
        }

        /// <summary>
        /// Keep the arena's placement-phase models in step with the pending arrangement. Only during
        /// Placement — once the fight starts the real creatures are the ones on the field.
        /// </summary>
        private void HandleLoadoutChanged()
        {
            if (spawner == null) return;

            if (Phase == BattlePhase.Placement) spawner.ShowPreviews(Loadout.Placements);
            else spawner.ClearPreviews();
        }

        private void OnDestroy()
        {
            Loadout.Changed -= HandleLoadoutChanged;

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
            awaitingVerdict = false;

            DetachUnitEvents();
            activeUnits.Clear();
            UnitRegistry.Clear();
            PackTactics.Clear();

            // Before DespawnAll, which destroys the creatures the run is still holding references to.
            if (gauntlet != null) gauntlet.EndRun();

            spawner.DespawnAll();
            Loadout.Clear();

            SetPhase(BattlePhase.Placement);
            UnitCountChanged?.Invoke();
        }

        /// <summary>
        /// Fight the same match again, immediately.
        ///
        /// Distinct from <see cref="EnterPlacement"/>, which clears the arrangement and sends the
        /// player back to set up — the button for that was labelled "rematch" and did not do what
        /// anyone expected of the word. Watching the same two armies again is the natural thing to
        /// want after a close result, and the loadout is still sitting there; only the creatures on
        /// the field need replacing.
        /// </summary>
        public bool Replay()
        {
            if (Phase == BattlePhase.Placement) return StartBattle();

            Time.timeScale = 1f;
            Winner = Team.Neutral;
            awaitingVerdict = false;

            DetachUnitEvents();
            activeUnits.Clear();
            UnitRegistry.Clear();
            PackTactics.Clear();
            spawner.DespawnAll();

            // Back to Placement first so StartBattle's guard passes and every listener sees the
            // normal Placement -> Fighting transition rather than Finished -> Fighting.
            SetPhase(BattlePhase.Placement);

            return StartBattle();
        }

        /// <summary>Spawn the loadout and let the AI take over. Requires creatures on both sides.</summary>
        public bool StartBattle()
        {
            if (Phase != BattlePhase.Placement) return false;

            if (Mode == GameMode.Gauntlet) return StartGauntlet();

            if (!Loadout.IsReadyToFight)
            {
                Debug.Log("[BattleManager] Both teams need at least one creature before the fight can start.");
                return false;
            }

            UnitRegistry.Clear();
            PackTactics.Clear();
            activeUnits.Clear();

            // The previews stood in for these creatures; the real ones take over now.
            spawner.ClearPreviews();

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

            RecordStartingHealth();

            SetPhase(BattlePhase.Fighting);
            ApplySimulationSpeed();
            UnitCountChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Start a climb. Only the player's side is placed — the board supplies the opposition.
        ///
        /// Deliberately does not go through the versus path: that requires a creature on both teams
        /// (there is nobody to place on the monsters' side, they are already standing on the board),
        /// spawns from the loadout's arena positions (which belong to a round arena on the other side
        /// of the world), and records a starting-health total for a pair of armies that do not exist
        /// here.
        /// </summary>
        private bool StartGauntlet()
        {
            if (gauntlet == null)
            {
                Debug.LogError("[BattleManager] Gauntlet mode selected but no GauntletDirector — rebuild the scene.");
                return false;
            }

            if (Loadout.CountFor(Team.Red) == 0)
            {
                Debug.Log("[BattleManager] Pick at least one creature before setting off.");
                return false;
            }

            UnitRegistry.Clear();
            PackTactics.Clear();
            activeUnits.Clear();
            spawner.ClearPreviews();

            gauntlet.BeginRun();

            // Phase first: SendWave spawns creatures whose CreatureImpact only runs during Fighting.
            SetPhase(BattlePhase.Fighting);
            ApplySimulationSpeed();

            if (!gauntlet.SendWave())
            {
                // Tear the board back down before going back to setup. BeginRun has already put
                // fifty-eight monsters on the tiers by this point, and leaving them there meant a
                // failed start banked a whole board that the next attempt would spawn on top of.
                gauntlet.EndRun();
                SetPhase(BattlePhase.Placement);
                return false;
            }

            UnitCountChanged?.Invoke();
            return true;
        }

        /// <summary>Send the next wave up the board. Returns false if it cannot be afforded.</summary>
        public bool SendGauntletWave()
        {
            if (Mode != GameMode.Gauntlet || gauntlet == null) return false;
            if (!gauntlet.SendWave()) return false;

            UnitCountChanged?.Invoke();
            return true;
        }

        /// <summary>Send one hero up the board. Returns false while the hero is still on cooldown.</summary>
        public bool SendGauntletHero()
        {
            if (Mode != GameMode.Gauntlet || gauntlet == null) return false;
            if (!gauntlet.SendHero()) return false;

            UnitCountChanged?.Invoke();
            return true;
        }

        public GauntletDirector Gauntlet => gauntlet;

        /// <summary>
        /// End a climb the way a match ends: the player won.
        ///
        /// Asked for: "클리어됐을때 춤추면서 축하하는 모션나오도록". A gauntlet deliberately stays in
        /// <see cref="BattlePhase.Fighting"/> from the first wave to the boss, so every system that
        /// celebrates a win — the dance, the result panel, the music, the camera — was waiting on a
        /// phase change that a climb never made. Killing the boss simply stopped the run.
        ///
        /// Rather than teach each of those about gauntlet states, the run reports its win through the
        /// same door a match does. <see cref="VictoryDance"/> then needs no knowledge of this mode at
        /// all: it looks for Finished and a winning team, and both are now true.
        ///
        /// It cannot reuse <see cref="DeclareResult"/>, which counts heads to decide the winner and
        /// stands survivors down through <c>activeUnits</c> — a list a gauntlet never fills, because
        /// the director owns its creatures. Here the winner is known and the registry is the roll.
        /// </summary>
        public void DeclareGauntletCleared()
        {
            if (Phase != BattlePhase.Fighting) return;

            Winner = Team.Red;

            StandDown(Team.Red);
            StandDown(Team.Blue);

            Time.timeScale = 1f;
            SetPhase(BattlePhase.Finished);
            BattleEnded?.Invoke(Winner);
        }

        /// <summary>Switch off the AI for one team's survivors, so nothing keeps fighting a won board.</summary>
        private static void StandDown(Team team)
        {
            // AliveOf hands back the registry's own list, not a copy. Setting CombatEnabled is a
            // plain property assignment and cannot remove anything from it — but iterating backwards
            // costs nothing and means a future side effect on that setter cannot turn this into an
            // index-skipping bug that only shows up on a won board.
            var survivors = UnitRegistry.AliveOf(team);

            for (int i = survivors.Count - 1; i >= 0; i--)
            {
                var unit = survivors[i];
                if (unit == null) continue;

                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.CombatEnabled = false;
            }
        }

        private void RecordStartingHealth()
        {
            startingHealth.Clear();

            for (int i = 0; i < activeUnits.Count; i++)
            {
                var unit = activeUnits[i];
                if (unit == null || unit.Health == null) continue;

                startingHealth.TryGetValue(unit.Team, out float total);
                startingHealth[unit.Team] = total + unit.Health.Max;
            }
        }

        /// <summary>
        /// How much of a team's original strength is still standing, 0 to 1.
        ///
        /// Survivor counts alone hide the shape of a battle: three creatures on their last legs and
        /// three untouched read identically, and they are opposite situations. Summed health is what
        /// tells the spectator who is actually winning.
        /// </summary>
        public float TeamHealthFraction(Team team)
        {
            if (!startingHealth.TryGetValue(team, out float total) || total <= 0f) return 0f;

            float current = 0f;
            var alive = UnitRegistry.AliveOf(team);

            for (int i = 0; i < alive.Count; i++)
            {
                var unit = alive[i];
                if (unit == null || unit.Health == null) continue;

                current += Mathf.Max(0f, unit.Health.Current);
            }

            return Mathf.Clamp01(current / total);
        }

        private void HandleUnitDied(CreatureUnit unit)
        {
            UnitCountChanged?.Invoke();

            // A gauntlet does not end because one side is momentarily empty. Clearing a tier is
            // progress, and losing a wave is a prompt to send another — GauntletDirector owns both
            // judgements. Without this the run would be declared over the instant the first tier
            // fell.
            if (Mode == GameMode.Gauntlet) return;

            if (Phase != BattlePhase.Fighting || awaitingVerdict) return;

            if (UnitRegistry.AliveCount(Team.Red) > 0 && UnitRegistry.AliveCount(Team.Blue) > 0) return;

            // Do not call it yet. A bite lands after a windup, so at the moment the last defender
            // falls its own killing blow may still be in the air — and judging here awarded the match
            // to whoever happened to resolve first, turning a mutual kill into a clean win. Wait for
            // the swings that were already committed, then look at who is actually left.
            awaitingVerdict = true;
            verdictDeadline = Time.time + verdictGrace;
        }

        private void Update()
        {
            if (!awaitingVerdict) return;

            if (Time.time < verdictDeadline && AnySwingInFlight()) return;

            awaitingVerdict = false;
            DeclareResult();
        }

        /// <summary>
        /// Is any creature — living or freshly killed — part way through a swing?
        ///
        /// Dead attackers count. Death does not disable <see cref="MeleeAttack"/>, so a creature that
        /// dies mid-windup still lands the blow, and that blow is exactly the one this is waiting for.
        /// </summary>
        private bool AnySwingInFlight()
        {
            for (int i = 0; i < activeUnits.Count; i++)
            {
                var unit = activeUnits[i];
                if (unit == null || unit.Attack == null) continue;
                if (unit.Attack.IsSwinging) return true;
            }

            return false;
        }

        private void DeclareResult()
        {
            int red = UnitRegistry.AliveCount(Team.Red);
            int blue = UnitRegistry.AliveCount(Team.Blue);

            // Neutral is a real outcome, not a fallback: both sides can genuinely wipe each other out
            // on the same exchange, and the HUD already words that as a draw.
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
