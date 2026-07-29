using System.Collections.Generic;
using System.Text;
using DinoBattle.Core;
using DinoBattle.Placement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Plays a whole gauntlet at speed and reports whether the climb ever stops making progress.
    ///
    /// It exists because "the run stalls" is the one class of bug in this mode that reading the code
    /// does not reliably catch. GauntletDirector is a state machine driven by death events, and every
    /// stall so far has been the same shape: a transition that had to be NOTICED at one instant, an
    /// ordinary race that missed the instant, and a state with no way out and no message. Three of
    /// those have now been found by playing rather than by reading.
    ///
    /// Deliberately not a balance probe. It presses "더 보내기" the moment it is offered and never
    /// judges whether the wave should have won — the only question is whether the run keeps moving.
    /// A stall is defined as no state change, no tier change and no send opportunity for
    /// <see cref="StallSeconds"/> of game time.
    ///
    /// Runs headless:
    ///   Unity.exe -batchmode -projectPath &lt;project&gt; \
    ///             -executeMethod DinoBattle.EditorTools.GauntletRunProbe.RunHeadless
    /// No -quit: play mode needs the editor loop to keep turning, so the probe exits the process
    /// itself with the verdict as the exit code.
    /// </summary>
    public static class GauntletRunProbe
    {
        /// <summary>
        /// How long the run may sit in <see cref="GauntletState.Engaging"/> against a tier with no
        /// living defenders before it counts as deadlocked.
        ///
        /// Generous next to the 1.2s advance delay, so a tier that has legitimately just been cleared
        /// has ample time to move on, and still short enough that a real deadlock — which lasts
        /// forever — is unmistakable.
        /// </summary>
        private const float DeadTierGrace = 6f;

        /// <summary>
        /// Coarse backstop: no tier change at all for this long. Sized well past the slowest real
        /// tier observed (tier nine took about 50s of game time at 8x), because its job is to catch a
        /// run that has stopped entirely, not to judge pace.
        /// </summary>
        private const float StallSeconds = 180f;

        private const float BudgetSeconds = 1200f;
        private const float SimulationSpeed = 8f;

        /// <summary>
        /// Entering play mode reloads the domain, which wipes every static in this class AND the
        /// <see cref="EditorApplication.update"/> subscription with them. The first headless attempt
        /// entered play mode and then sat there forever with nothing driving it, because the
        /// subscription registered before EnterPlaymode no longer existed after it.
        ///
        /// SessionState survives a domain reload (unlike a static) and dies with the editor process
        /// (unlike EditorPrefs), which is exactly the lifetime a run of this probe has.
        /// </summary>
        private const string ActiveKey = "DinoBattle.GauntletRunProbe.Active";
        private const string HeadlessKey = "DinoBattle.GauntletRunProbe.Headless";
        private const string PatientKey = "DinoBattle.GauntletRunProbe.Patient";

        private static bool running;
        private static bool headless;
        private static bool started;

        /// <summary>
        /// Only send when everything is dead — the way the mode played before mid-fight reinforcement
        /// existed, and the way the owner was playing when they hit the stall.
        ///
        /// This is not a nostalgia setting, it is the only way to test the wipe path at all.
        /// Reinforcing on cooldown means the wave is essentially never wiped, so the run never
        /// passes through WaveWiped, so the race that strands a tier never gets a chance to happen —
        /// the impatient probe passed twice with the bug deliberately reinstated for exactly that
        /// reason. A test that cannot reach the state it is testing proves nothing.
        /// </summary>
        private static bool patient;

        private static int shotsTaken;
        private static float nextShot;

        private static float lastProgress;
        private static float deadline;

        /// <summary>When the current dead-tier deadlock started, or -1 if there is not one.</summary>
        private static float deadTierSince = -1f;

        private static GauntletState lastState;
        private static int lastTier = -1;
        private static int wavesPressed;

        /// <summary>Waves spent as of the last tier change, to tell a wall from a stall.</summary>
        private static int wavesAtTierChange;

        /// <summary>Most spiders the boss had alive at once. Must never exceed the cap.</summary>
        private static int peakBrood;

        /// <summary>Worst-case creature count seen. The number Docs/performance.md cares about.</summary>
        private static int peakOnBoard;

        /// <summary>Next time the hero colour is sampled.</summary>
        private static float nextColourSample;

        /// <summary>Game time the boss tier was first engaged, for reporting how long the boss lasts.</summary>
        private static float bossFightStart = -1f;

        /// <summary>Shots taken during the boss fight, budgeted separately from the climb's.</summary>
        private static int bossShots;
        private static readonly List<string> log = new();

        [MenuItem("Dino Battle/Advanced/Probe Gauntlet Run", priority = 224)]
        public static void Run() => Begin(false, false);

        [MenuItem("Dino Battle/Advanced/Probe Gauntlet Run (wipe path only)", priority = 225)]
        public static void RunPatient() => Begin(false, true);

        /// <summary>Batch-mode entry. Opens the scene, enters play mode, and exits the process.</summary>
        public static void RunHeadless() => Begin(true, false);

        /// <summary>Batch-mode entry that only reinforces after a wipe. See <see cref="patient"/>.</summary>
        public static void RunHeadlessPatient() => Begin(true, true);

        private static void Begin(bool exitWhenDone, bool waitForWipes)
        {
            if (running) return;

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(HeadlessKey, exitWhenDone);
            SessionState.SetBool(PatientKey, waitForWipes);

            if (!EditorApplication.isPlaying)
            {
                // In batch mode the open scene is whatever Unity felt like loading, which is usually
                // an empty untitled one.
                if (EditorSceneManager.GetActiveScene().path != BattleSceneBuilder.ScenePath)
                    EditorSceneManager.OpenScene(BattleSceneBuilder.ScenePath);

                // Everything after this is picked up by Attach, on the far side of the reload.
                EditorApplication.EnterPlaymode();
                return;
            }

            Attach();
        }

        /// <summary>Re-arm after the play-mode domain reload, or arm directly when already playing.</summary>
        [InitializeOnLoadMethod]
        private static void Attach()
        {
            if (running) return;
            if (!SessionState.GetBool(ActiveKey, false)) return;

            running = true;
            headless = SessionState.GetBool(HeadlessKey, false);
            patient = SessionState.GetBool(PatientKey, false);
            started = false;
            wavesPressed = 0;
            wavesAtTierChange = 0;
            lastTier = -1;
            deadTierSince = -1f;
            peakBrood = 0;
            peakOnBoard = 0;
            nextColourSample = 0f;
            bossFightStart = -1f;
            bossShots = 0;
            shotsTaken = 0;
            nextShot = 0f;
            log.Clear();

            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;

            var manager = BattleManager.Instance;
            if (manager == null) return;

            var run = manager.Gauntlet;
            if (run == null) { Finish(false, "no GauntletDirector in the scene"); return; }

            if (!started)
            {
                // Without this the world freezes the moment the editor loses focus, and every
                // measurement below is taken on a stopped clock.
                Application.runInBackground = true;

                // The catch-up cap is timeScale-aware, so asking for 8x without raising it gets a
                // real 2.4x. See Docs/performance.md.
                MobilePerformance.OfflineCatchUpSteps = 64;

                manager.SetMode(GameMode.Gauntlet);

                var placer = Object.FindAnyObjectByType<AutoPlacer>();
                if (placer == null) { Finish(false, "no AutoPlacer in the scene"); return; }

                placer.FillGauntletWave();

                if (!manager.StartBattle()) { Finish(false, "StartBattle refused"); return; }

                started = true;
                lastState = run.State;
                lastProgress = Time.time;
                deadline = Time.time + BudgetSeconds;
                Note(run, "run started");
            }

            Time.timeScale = SimulationSpeed;

            if (run.CurrentTier != lastTier)
            {
                lastTier = run.CurrentTier;
                lastProgress = Time.time;
                wavesAtTierChange = wavesPressed;
            }

            if (run.State != lastState)
            {
                lastState = run.State;
                Note(run, "progress");

                // Frames of the fights worth looking at, because several of the things being built
                // here are visual and a picture is the only honest check: the tier number painted on
                // the deck the creatures stand on, the gold on a hero, and the boss's spiders.
                //
                // The boss tier gets its own shots — that is where the brood and, by then, a hero or
                // two will be on screen together.
                bool bossFight = run.CurrentTier >= run.TierCount - 1;

                // How long the boss lasts is the number that says whether its brood is worth
                // anything — a boss that dies in fourteen seconds cannot use five spiders.
                if (bossFight && run.State == GauntletState.Engaging && bossFightStart < 0f)
                    bossFightStart = Time.time;

                if (run.State == GauntletState.Engaging && (shotsTaken < 2 || bossFight))
                {
                    // The boss gets its own budget. Sharing one counter, the early tiers spent all
                    // eight shots before the climb was a third done and the boss fight — the only
                    // place the brood exists — went unphotographed.
                    if (bossFight) bossShots = 0;
                    nextShot = Time.time + 1.5f;
                }
            }

            if (nextShot > 0f && Time.time >= nextShot)
            {
                bool bossFight = run.CurrentTier >= run.TierCount - 1;

                nextShot = 0f;
                Capture($"tier-{run.CurrentTier + 1}-{shotsTaken}");
                if (bossFight) bossShots++;

                // A short burst rather than one frame: a fight moves, and a single sample lands as
                // often as not on a moment where nothing of interest is facing the camera.
                bool more = bossFight ? bossShots < 5 : shotsTaken < 4;
                if (more) nextShot = Time.time + (bossFight ? 2.5f : 3f);
            }

            // The boss's brood, on the tier it belongs to. Recorded rather than asserted: how many
            // are out at any moment is a fight detail, but "did the cap ever break" is not.
            if (run.State == GauntletState.Engaging && run.BroodAlive > peakBrood)
            {
                peakBrood = run.BroodAlive;
                log.Add($"  brood peaked at {peakBrood} on tier {run.CurrentTier + 1}");
            }

            // How many creatures are actually on the board at the worst moment.
            //
            // This is the number Docs/performance.md is about, and it is the one thing here that
            // cannot be judged by watching: a screenshot showed a hundred-odd creatures at the boss
            // and that was the first anyone knew the three-second cooldown had no ceiling. Measured
            // every tick, it is a fact rather than an impression.
            int onBoard = manager.AliveCount(Team.Red) + manager.AliveCount(Team.Blue);
            if (onBoard > peakOnBoard)
            {
                peakOnBoard = onBoard;
                if (peakOnBoard % 10 == 0)
                    log.Add($"  {peakOnBoard} creatures on the board (tier {run.CurrentTier + 1})");
            }

            if (run.State == GauntletState.Cleared)
            {
                // The celebration is the point of clearing, and it hangs off the match ending
                // properly rather than the run simply stopping — so the phase is what to check.
                string ending = manager.Phase == BattlePhase.Finished && manager.Winner == Team.Red
                    ? "match declared won (dance and result screen fire)"
                    : $"BUT the match did not end — phase {manager.Phase}, winner {manager.Winner}";

                Finish(manager.Phase == BattlePhase.Finished,
                    $"reached the top in {wavesPressed} extra wave(s) and {run.HeroesSent} hero(es); " +
                    $"peak brood {peakBrood}; peak {peakOnBoard} creatures on the board; " +
                    $"boss lasted {(bossFightStart < 0f ? 0f : Time.time - bossFightStart):0.0}s; {ending}");
                return;
            }

            // THE deadlock, asserted directly rather than inferred from a run that looks stuck.
            //
            // Engaging means "there is a tier in front of you to fight"; zero defenders means there
            // is not. The run is waiting on deaths that have already happened, and nothing will ever
            // move it on.
            //
            // The first version of this probe watched for "no progress" instead, and passed happily
            // with the bug reinstated — because it counted the send button being available as
            // progress, and mid-fight reinforcement makes that permanently true. A deadlocked run
            // that lets you keep pouring creatures into an empty tier looks, to that test, exactly
            // like a healthy one.
            if (run.State == GauntletState.Engaging && run.DefendersAlive == 0)
            {
                if (deadTierSince < 0f) deadTierSince = Time.time;

                if (Time.time - deadTierSince > DeadTierGrace)
                {
                    Finish(false, $"DEADLOCK on tier {run.CurrentTier + 1} — Engaging a tier with " +
                                  $"no living defenders for {DeadTierGrace}s. The run is waiting on " +
                                  "deaths that have already happened.");
                    return;
                }
            }
            else
            {
                deadTierSince = -1f;
            }

            SampleHeroColour(run);

            // Heroes are always pressed, in both modes. They are on their own cooldown and cost
            // nothing else, so there is no version of playing well that leaves the button alone —
            // and it means every run exercises MarkAsHero and the gold-and-larger path.
            if (run.CanSendHero && manager.SendGauntletHero()) return;

            // Press it the instant it is offered. A player would too, and a run that only survives
            // because the tester hesitated is not a run that survives.
            bool wiped = run.State is GauntletState.Ready or GauntletState.WaveWiped;
            if ((!patient || wiped) && run.CanSendWave && manager.SendGauntletWave())
            {
                wavesPressed++;
                return;
            }

            if (Time.time - lastProgress > StallSeconds)
            {
                // A run that has stopped advancing is not necessarily a run that is BROKEN, and the
                // difference decides whether this is a bug report or a balance note.
                //
                // If waves are still being spent, the machinery is working perfectly — the player is
                // simply losing, over and over, to a tier they cannot beat. That is a wall, and a
                // wall is a number in GauntletLadder, not a defect in GauntletDirector. Failing the
                // build for it would make this probe useless as a gate: nobody keeps running a check
                // that goes red for a design decision.
                int spentHere = wavesPressed - wavesAtTierChange;
                if (spentHere >= 5)
                {
                    Finish(true, $"WALL on tier {run.CurrentTier + 1} — {spentHere} waves spent " +
                                 "there with no progress. The run is not stuck, it is losing: every " +
                                 "wave dies. This is a ladder-tuning number, not a state-machine " +
                                 "fault. (Reached with waves sent only after a wipe.)");
                    return;
                }

                Finish(false, $"STALLED on tier {run.CurrentTier + 1} in state {run.State} — " +
                              $"{StallSeconds}s without reaching a new tier, and only {spentHere} " +
                              "wave(s) spent. Nothing is consuming the run, so it is not losing — " +
                              "it has stopped.");
                return;
            }

            if (Time.time > deadline)
            {
                Finish(false, $"ran out of budget on tier {run.CurrentTier + 1} in state {run.State}");
            }
        }

        /// <summary>
        /// Render one frame of the live game camera to a PNG beside the log.
        ///
        /// Camera.Render into a RenderTexture rather than ScreenCapture, because in batch mode there
        /// is no screen to capture — but there is a graphics device (as long as -nographics is NOT
        /// passed), and an explicit render works.
        ///
        /// The camera is wherever BattleCameraDirector has put it, which is the point: the shot is
        /// the shot the player would be looking at.
        /// </summary>
        private static void Capture(string label)
        {
            var camera = Camera.main;
            if (camera == null) return;

            const int width = 1280;
            const int height = 720;

            var target = new RenderTexture(width, height, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;

            RenderTexture.active = target;
            var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            shot.Apply();
            RenderTexture.active = previousActive;

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"gauntlet-{label}.png");
            System.IO.File.WriteAllBytes(path, shot.EncodeToPNG());

            Object.DestroyImmediate(shot);
            target.Release();
            Object.DestroyImmediate(target);

            shotsTaken++;
            log.Add($"  shot -> {path}");
        }

        /// <summary>
        /// Read the colour a hero is ACTUALLY being drawn with, and keep reading it.
        ///
        /// Reported: "영웅 금색이 왜 없어지지". Judging this from a screenshot is how the question
        /// arose and is no way to answer it — the roster has naturally yellow-green dinosaurs, so a
        /// hero that was never tinted at all and one that was tinted perfectly can look the same at
        /// thumbnail size, and I had already read one frame as "distinctly gold" on that basis.
        ///
        /// The property block is the ground truth. A hero is found by its scale rather than by
        /// asking the director, so this measures what the renderer got rather than what the code
        /// intended, and sampling repeatedly is what distinguishes "never applied" from "applied and
        /// then lost".
        /// </summary>
        private static void SampleHeroColour(GauntletDirector run)
        {
            if (Time.time < nextColourSample) return;

            nextColourSample = Time.time + 2f;

            var block = new UnityEngine.MaterialPropertyBlock();
            int id = Shader.PropertyToID("_Color");

            foreach (var unit in Object.FindObjectsByType<Units.CreatureUnit>(FindObjectsSortMode.None))
            {
                if (unit == null || unit.IsDead || unit.Team != Team.Red) continue;

                // By species, not by scale. Heroes used to be the only thing on the board bigger than
                // 1x, but the hero is now SHRUNK — Malformed Rex is drawn at two and a half times a
                // T-Rex, so standing a fifth over the roster means scaling it to about 0.5. A scale
                // test would quietly match nothing and this sampler would report an empty run as a
                // clean one.
                if (unit.Definition == null || unit.Definition.displayName != "Malformed Rex") continue;

                var model = unit.transform.Find("Visual_Model");
                if (model == null)
                {
                    log.Add("  HERO has no Visual_Model child — MarkAsHero returns before tinting");
                    return;
                }

                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.GetPropertyBlock(block, 0);

                    Color c = block.HasColor(id) ? block.GetColor(id)
                            : renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(id)
                                ? renderer.sharedMaterial.GetColor(id)
                                : Color.magenta;

                    string source = block.HasColor(id) ? "block" : "material (NO BLOCK SET)";
                    log.Add($"  hero t={Time.time:0} scale={unit.transform.localScale.x:0.000} " +
                            $"{renderer.name} = ({c.r:0.00},{c.g:0.00},{c.b:0.00}) from {source}");
                    return;
                }
            }
        }

        private static void Note(GauntletDirector run, string what)
        {
            log.Add($"  t={Time.time:0.0}  tier {run.CurrentTier + 1}/{run.TierCount}  " +
                    $"{run.State}  waves={run.WavesSent}  ({what})");
        }

        private static void Finish(bool passed, string verdict)
        {
            EditorApplication.update -= Tick;
            running = false;
            SessionState.SetBool(ActiveKey, false);

            var report = new StringBuilder();
            report.AppendLine($"[GauntletRunProbe] {(passed ? "PASS" : "FAIL")} — {verdict}");
            foreach (string line in log) report.AppendLine(line);

            if (passed) Debug.Log(report.ToString());
            else Debug.LogError(report.ToString());

            Time.timeScale = 1f;
            MobilePerformance.OfflineCatchUpSteps = null;

            if (!headless)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            // Batch mode was launched without -quit so play mode could actually run; the probe owns
            // the exit, and the exit code carries the verdict to the shell.
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
