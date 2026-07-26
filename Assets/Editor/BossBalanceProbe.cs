using System.Collections.Generic;
using DinoBattle.Core;
using DinoBattle.Placement;
using DinoBattle.Units;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Runs boss battles back to back in play mode and reports how they went.
    ///
    /// Balance is not something you can read off the stat block. Whether ten hunters beat one boss
    /// depends on the pack AI's turn-taking, on how often the boss's slow turn lets an attacker get
    /// behind it, and on the desperation rule flipping survivors aggressive near the end — none of
    /// which are in the numbers. The only honest way to set the boss's health and damage is to run
    /// the fight repeatedly and look at the distribution of outcomes.
    ///
    /// "Close" is defined here as the loser getting the winner down near the wire: a boss that wins
    /// on a sliver of health, or a pack that wins with one or two left standing. A run where every
    /// match ends the same way with the winner barely scratched is a boss that is mis-set, whichever
    /// side is winning.
    ///
    /// Editor-only and never referenced by the game.
    /// </summary>
    public static class BossBalanceProbe
    {
        /// <summary>
        /// Battles per boss, not in total. Every boss is probed the same number of times, because a
        /// random pick across four of them buries a lopsided matchup in the noise of the other three.
        /// </summary>
        private const int BattlesPerBoss = 8;

        /// <summary>
        /// Fast enough to get a sample in reasonable time, slow enough that physics stays sane.
        /// Above roughly 20 the fixed timestep stops keeping up and creatures tunnel.
        /// </summary>
        private const float Speed = 16f;

        /// <summary>
        /// Abandon a match that has not resolved, measured in GAME seconds. A stalemate is itself a
        /// balance finding, but only if the clock measuring it is real: the first version accumulated
        /// Time.unscaledDeltaTime once per EditorApplication.update, which fires many times per game
        /// frame, so it summed the same frame's delta repeatedly and every battle "timed out" in
        /// seconds. It reported 12/12 stalemates for a fight that was resolving normally.
        /// </summary>
        private const float TimeoutGameSeconds = 240f;

        /// <summary>
        /// One finished battle.
        ///
        /// A record rather than a formatted string. The previous version stored sentences and read
        /// the numbers back out with Substring offsets, which broke the moment the line gained a
        /// boss-name prefix — and would have broken silently, reporting zero close finishes rather
        /// than failing.
        /// </summary>
        private readonly struct Outcome
        {
            public readonly string Boss;
            public readonly bool Stalemate;
            public readonly bool BossWon;

            /// <summary>Boss health left when the boss won, 0-1.</summary>
            public readonly float BossHealth;

            /// <summary>Hunters left when the pack won.</summary>
            public readonly int HuntersLeft;

            private Outcome(string boss, bool stalemate, bool bossWon, float bossHealth, int huntersLeft)
            {
                Boss = boss;
                Stalemate = stalemate;
                BossWon = bossWon;
                BossHealth = bossHealth;
                HuntersLeft = huntersLeft;
            }

            public static Outcome Stalled(string boss) => new(boss, true, false, 0f, 0);
            public static Outcome BossWin(string boss, float health) => new(boss, false, true, health, 0);
            public static Outcome PackWin(string boss, int left) => new(boss, false, false, 0f, left);

            /// <summary>Did the loser take the winner close to the wire?</summary>
            public bool Close => !Stalemate && (BossWon ? BossHealth <= 0.33f : HuntersLeft <= 2);

            public override string ToString() =>
                Stalemate ? $"{Boss}: STALEMATE"
                : BossWon ? $"{Boss}: BOSS on {BossHealth * 100f:F0}%"
                : $"{Boss}: PACK with {HuntersLeft} left";
        }

        private static readonly List<Outcome> Results = new();

        private static int battlesRun;
        private static float battleStartTime;
        private static bool running;

        /// <summary>Bosses to cycle through, and which one the current battle is using.</summary>
        private static List<Data.CreatureDefinition> bosses = new();
        private static int bossIndex;

        [MenuItem("Dino Battle/Advanced/Probe Boss Balance", priority = 220)]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[BossBalanceProbe] Enter play mode first — this measures a running game.");
                return;
            }

            if (running)
            {
                Debug.LogWarning("[BossBalanceProbe] Already running.");
                return;
            }

            var roster = AssetDatabase.LoadAssetAtPath<Data.CreatureRoster>(
                SampleContentBuilder.BossRosterPath);
            if (roster == null || roster.Creatures.Count == 0)
            {
                Debug.LogError("[BossBalanceProbe] No boss roster — run 'Dino Battle > 1. Generate Sample Content'.");
                return;
            }

            bosses = new List<Data.CreatureDefinition>(roster.Creatures);
            bossIndex = 0;

            Results.Clear();
            battlesRun = 0;
            running = true;

            EditorApplication.update += Tick;
            StartOne();
        }

        private static void StartOne()
        {
            var manager = BattleManager.Instance;
            var placer = Object.FindAnyObjectByType<AutoPlacer>();
            if (manager == null || placer == null)
            {
                Finish("no BattleManager or AutoPlacer in the scene");
                return;
            }

            manager.EnterPlacement();
            placer.BossBattle(CurrentBoss);
            manager.StartBattle();

            // Set after StartBattle: it applies the simulation speed itself, which would undo this.
            Time.timeScale = Speed;
            battleStartTime = Time.time;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                Finish("left play mode");
                return;
            }

            var manager = BattleManager.Instance;
            if (manager == null)
            {
                Finish("BattleManager went away");
                return;
            }

            if (manager.Phase == BattlePhase.Fighting && Time.time - battleStartTime < TimeoutGameSeconds)
            {
                Time.timeScale = Speed;
                return;
            }

            Record(manager, timedOut: manager.Phase == BattlePhase.Fighting);

            battlesRun++;

            // Finish all of one boss's battles before moving to the next, so the log reads as a
            // block per boss rather than as an interleaved shuffle.
            if (battlesRun % BattlesPerBoss == 0) bossIndex++;

            if (bossIndex >= bosses.Count)
            {
                Report();
                Finish(null);
                return;
            }

            StartOne();
        }

        /// <summary>The boss this battle is being fought against, or null before the run starts.</summary>
        private static Data.CreatureDefinition CurrentBoss =>
            bossIndex >= 0 && bossIndex < bosses.Count ? bosses[bossIndex] : null;

        private static string BossName =>
            CurrentBoss != null ? CurrentBoss.displayName : "?";

        private static void Record(BattleManager manager, bool timedOut)
        {
            if (timedOut)
            {
                Results.Add(Outcome.Stalled(BossName));
                return;
            }

            int hunters = UnitRegistry.AliveCount(Team.Red);
            int boss = UnitRegistry.AliveCount(Team.Blue);

            if (boss > 0)
            {
                // Boss won: how close did the pack get to killing it?
                float fraction = 0f;
                foreach (var unit in UnitRegistry.AliveOf(Team.Blue))
                {
                    if (unit?.Health == null || unit.Health.Max <= 0f) continue;
                    fraction = unit.Health.Current / unit.Health.Max;
                }

                Results.Add(Outcome.BossWin(BossName, fraction));
                return;
            }

            Results.Add(Outcome.PackWin(BossName, hunters));
        }

        private static void Report()
        {
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"[BossBalanceProbe] {Results.Count} battles, {BattlesPerBoss} per boss.");

            foreach (var boss in bosses)
            {
                if (boss == null) continue;

                int wins = 0, losses = 0, stalls = 0, close = 0;
                var detail = new List<string>();

                foreach (var outcome in Results)
                {
                    if (outcome.Boss != boss.displayName) continue;

                    if (outcome.Stalemate) stalls++;
                    else if (outcome.BossWon) wins++;
                    else losses++;

                    if (outcome.Close) close++;
                    detail.Add(outcome.Stalemate ? "stalemate"
                        : outcome.BossWon ? $"boss {outcome.BossHealth * 100f:F0}%"
                        : $"pack {outcome.HuntersLeft} left");
                }

                int played = wins + losses + stalls;
                if (played == 0) continue;

                lines.AppendLine($"  {boss.displayName,-15} boss {wins} / pack {losses}" +
                                 (stalls > 0 ? $" / stalemate {stalls}" : "") +
                                 $"  — close {close}/{played}");
                lines.AppendLine($"      {string.Join(", ", detail)}");
            }

            Debug.Log(lines.ToString());
        }

        private static void Finish(string reason)
        {
            running = false;
            EditorApplication.update -= Tick;
            Time.timeScale = 1f;

            if (reason != null) Debug.LogWarning($"[BossBalanceProbe] Stopped: {reason}.");
        }
    }
}
