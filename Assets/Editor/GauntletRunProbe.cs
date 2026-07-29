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

                // One frame of the first real fight, so the board can be looked at rather than
                // reasoned about. The tier numbers are painted on the deck the creatures fight on,
                // which makes "does the number draw through them" a question only a picture answers.
                if (run.State == GauntletState.Engaging && shotsTaken < 3)
                    nextShot = Time.time + 1.5f;
            }

            if (nextShot > 0f && Time.time >= nextShot)
            {
                nextShot = 0f;
                Capture($"tier-{run.CurrentTier + 1}-{shotsTaken}");
                if (shotsTaken < 3) nextShot = Time.time + 2f;
            }

            if (run.State == GauntletState.Cleared)
            {
                Finish(true, $"reached the top in {wavesPressed} extra wave(s)");
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
