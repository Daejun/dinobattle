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
        private const int Battles = 12;

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

        private static readonly List<string> Results = new();

        private static int battlesRun;
        private static float battleStartTime;
        private static bool running;

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
            placer.BossBattle();
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

            if (++battlesRun >= Battles)
            {
                Report();
                Finish(null);
                return;
            }

            StartOne();
        }

        private static void Record(BattleManager manager, bool timedOut)
        {
            if (timedOut)
            {
                Results.Add("STALEMATE");
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

                Results.Add($"BOSS on {fraction * 100f:F0}%");
                return;
            }

            Results.Add($"PACK with {hunters} left");
        }

        private static void Report()
        {
            int bossWins = 0, packWins = 0, stalemates = 0;
            int closeCalls = 0;

            foreach (string result in Results)
            {
                if (result.StartsWith("STALEMATE")) stalemates++;
                else if (result.StartsWith("BOSS")) bossWins++;
                else packWins++;

                // A boss finishing under a third, or a pack finishing on two or fewer, is the shape
                // of fight worth having.
                if (result.StartsWith("BOSS on"))
                {
                    string digits = result.Substring(8).TrimEnd('%');
                    if (float.TryParse(digits, out float percent) && percent <= 33f) closeCalls++;
                }
                else if (result.StartsWith("PACK with"))
                {
                    string digits = result.Substring(10).Split(' ')[0];
                    if (int.TryParse(digits, out int left) && left <= 2) closeCalls++;
                }
            }

            Debug.Log($"[BossBalanceProbe] {Results.Count} battles: " +
                      $"boss {bossWins}, pack {packWins}, stalemate {stalemates}. " +
                      $"Close finishes: {closeCalls}/{Results.Count}.\n  " +
                      string.Join("\n  ", Results));
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
