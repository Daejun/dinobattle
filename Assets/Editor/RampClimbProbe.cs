using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DinoBattle.Core;
using DinoBattle.Data;
using DinoBattle.Units;
using UnityEditor;
using UnityEngine;

namespace DinoBattle.EditorTools
{
    /// <summary>
    /// Step 1 gate for the gauntlet mode: can the shipping locomotion actually climb a ramp?
    ///
    /// Design and the reasoning behind every threshold here: <c>Docs/gauntlet-step1-ramp-probe.md</c>.
    /// This file is the instrument, not the argument — if a number below looks arbitrary, it is
    /// derived in that document and changing it here without changing it there makes the report lie.
    ///
    /// The hypothesis under test is that <see cref="CreatureLocomotion"/> — whose steering force has
    /// no vertical component at all and which stops steering entirely when its ground probe misses —
    /// still carries creatures up a shallow incline, because a horizontal push gets redirected by the
    /// surface normal. This probe exists to try to falsify that, not to confirm it.
    ///
    /// Two things about the shape of this class matter:
    ///
    /// The orchestrator runs on <see cref="EditorApplication.update"/> and reads <see cref="Time.time"/>
    /// only. It never accumulates deltas there — that editor callback does not fire once per game
    /// frame, and accumulating in it is what once made BossBalanceProbe report every normal fight as
    /// a draw.
    ///
    /// The sampling runs in <see cref="RampClimbSampler"/>, a MonoBehaviour, in FixedUpdate, at
    /// execution order 1000 so it lands after locomotion has already moved the body. Sampling from
    /// the editor tick cannot see a 40 ms steering dropout or a one-step oscillation, which are two
    /// of the failure modes this exists to catch.
    /// </summary>
    public static class RampClimbProbe
    {
        // ------------------------------------------------------------------ tunables from the design

        /// <summary>Flat run-up before the first gate, long enough to reach steady speed (§5.4).</summary>
        private const float RunupLength = 12f;

        /// <summary>Tier rise. Physics depends on angle, not rise — this only sets ramp length (§3.2).</summary>
        private const float TierRise = 2f;

        private const float PlatformDepth = 20f;
        private const float DefaultWidth = 10f;
        private const float CourseX = 500f;

        /// <summary>Gate offsets either side of the ramp, clear of Arrive's slowing radius (§5.4).</summary>
        private const float GatePad = 2f;
        private const float SlowingRadius = 6f;
        private const float MarchLead = SlowingRadius + 4f;

        /// <summary>Stall detector (§2.2). A window this long cannot be a 0.44 s stagger.</summary>
        internal const float StallWindow = 1.0f;
        internal const float StallFraction = 0.15f;

        /// <summary>Gate thresholds G2/G4/G5/G6/G7 (§2.3).</summary>
        private const float MaxBackslide = 0.5f;
        private const float MaxTimeRatio = 1.30f;
        private const int MaxUngroundedRun = 10;
        private const float MaxAirborne = 0.2f;
        private const float MaxPeakClearance = 0.3f;

        /// <summary>Clearance above the standing baseline that counts as airborne (§5.2).</summary>
        internal const float AirborneMargin = 0.15f;

        /// <summary>Arrive's slowing radius, mirrored so the stall detector can exclude braking.</summary>
        internal const float ArriveSlowing = SlowingRadius;

        private const float TrialTimeout = 60f;

        /// <summary>Assumed friction. Measured three ways by C4 rather than trusted (§2.4).</summary>
        private const float AssumedFriction = 0.6f;

        // ------------------------------------------------------------------ menu

        [MenuItem("Dino Battle/Advanced/Probe Ramp Climb", priority = 222)]
        private static void RunFull() => Begin(quick: false);

        [MenuItem("Dino Battle/Advanced/Probe Ramp Climb (quick)", priority = 223)]
        private static void RunQuick() => Begin(quick: true);

        [MenuItem("Dino Battle/Advanced/Probe Ramp Climb", true)]
        [MenuItem("Dino Battle/Advanced/Probe Ramp Climb (quick)", true)]
        private static bool RunValidate() => EditorApplication.isPlaying && !running;

        // ------------------------------------------------------------------ cells

        /// <summary>What varies in one measurement cell. Everything else is held (§3.2).</summary>
        private struct Cell
        {
            public string Block;
            public string Species;
            public float AngleDeg;

            /// <summary>&gt; 0 builds a vertical step course instead of a ramp — control C3.</summary>
            public float StepHeight;

            public int Ramps;
            public int Count;
            public int Trials;
            public float Width;

            /// <summary>Pass the march point to Steer, or let it gate on facing (§3.1).</summary>
            public bool FaceTarget;

            /// <summary>Expected verdict, for the controls that must fail (§2.4).</summary>
            public bool ExpectFailure;
        }

        private sealed class TrialResult
        {
            public int Arrived;
            public int Total;
            public float SlowestGateTime;
            public float MedianGateTime;
            public float WorstBackslide;
            public int Stalls;
            public float LongestStall;
            public int LongestUngroundedRun;
            public float LongestAirborne;
            public float PeakClearance;
            public float WorstLateral;
            public float WorstPenetration;
        }

        // ------------------------------------------------------------------ state

        private static bool running;
        private static bool quickRun;
        private static List<Cell> cells;
        private static int cellIndex;
        private static int trialIndex;
        private static readonly List<TrialResult> cellTrials = new();
        private static readonly List<string> report = new();
        private static readonly List<string> csv = new();

        private static GameObject course;
        private static readonly List<Collider> courseColliders = new();
        private static readonly List<RampClimbSampler> samplers = new();
        private static float trialStart;
        private static float entryGateS;
        private static float exitGateS;
        private static float courseEndS;
        private static Vector3 marchPoint;

        /// <summary>Flat top speed per species, measured by C1 and used as the ratio denominator.</summary>
        private static readonly Dictionary<string, float> flatSpeed = new();

        /// <summary>Serialized locomotion values, read off a live instance and printed (§3.3).</summary>
        private static string prefabValues = "(not read)";

        private static float restoreTimeScale = 1f;
        private static float restoreFixedDelta = 0.02f;
        private static int restoreTargetFrameRate = 60;

        // ------------------------------------------------------------------ lifecycle

        private static void Begin(bool quick)
        {
            if (running) return;

            var manager = BattleManager.Instance;
            var roster = manager != null ? manager.Roster : null;
            if (manager == null || roster == null)
            {
                Debug.LogError("[RampClimbProbe] Enter play mode in Arena.unity first — no BattleManager or roster.");
                return;
            }

            // Without this the world stops the instant the editor loses focus and every sample below
            // is taken on a frozen simulation. Logged rather than assumed: the player settings flag
            // and the runtime flag are different things.
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;

            restoreTimeScale = Time.timeScale;
            restoreFixedDelta = Time.fixedDeltaTime;
            restoreTargetFrameRate = Application.targetFrameRate;

            // Uncapping the frame rate speeds the sweep without touching physics fidelity — the
            // control-loop rate is set by the catch-up budget, not by how often we render.
            Application.targetFrameRate = -1;

            // Take back the throughput the shipping spiral guard costs an offline run. Restored in
            // Finish on every exit path.
            MobilePerformance.OfflineCatchUpSteps = 16;

            quickRun = quick;
            cells = BuildCells(quick);
            cellIndex = 0;
            trialIndex = 0;
            cellTrials.Clear();
            report.Clear();
            csv.Clear();
            flatSpeed.Clear();
            running = true;

            csv.Add("block,species,angleDeg,stepHeight,count,trial,arrived,gateTime,backslide,stalls," +
                    "longestStall,ungroundedRun,airborne,peakClearance,lateral,penetration");

            Debug.Log($"[RampClimbProbe] {(quick ? "quick" : "full")} run: {cells.Count} cells, " +
                      $"{CountTrials()} trials. runInBackground={Application.runInBackground}");

            EditorApplication.update += Tick;
            StartTrial();
        }

        private static int CountTrials()
        {
            int n = 0;
            foreach (var c in cells) n += c.Trials;
            return n;
        }

        /// <summary>
        /// Controls first, always.
        ///
        /// A probe that has not shown it can see failure has proved nothing by passing, so C1 (flat
        /// must behave exactly as the model says) and C2 (65 degrees must be impossible for
        /// everything) run before any real measurement. If C2 passes, something other than the ramp
        /// normal is pushing creatures uphill and every later verdict is void.
        /// </summary>
        private static List<Cell> BuildCells(bool quick)
        {
            // Three species, not a sample: each owns a different axis of the worst case (§2.1a).
            // Velociraptor is the FASTEST and has the smallest collider; Malformed Rex is slowest,
            // heaviest and largest; Triceratops is the wide blunt middle.
            string[] species = { "Velociraptor", "Triceratops", "Malformed Rex" };
            var list = new List<Cell>();

            // C1 — flat. Establishes the ratio denominator and must match moveSpeed - g*mu/accel.
            foreach (var s in species)
                list.Add(new Cell { Block = "C1", Species = s, AngleDeg = 0f, Ramps = 1, Count = 1, Trials = 3, Width = DefaultWidth });

            // C2 — must fail. 65 degrees is past the climb-impossible angle for mu = 0.6.
            foreach (var s in species)
                list.Add(new Cell { Block = "C2", Species = s, AngleDeg = 65f, Ramps = 1, Count = 1, Trials = 2, Width = DefaultWidth, ExpectFailure = true });

            // C3 — step sweep. Finds the climbable ledge height, which is the single most reusable
            // number this probe can produce: it becomes the constraint on every seam and kerb the
            // gauntlet geometry is allowed to have.
            foreach (var s in species)
                foreach (float h in new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.6f, 1.0f })
                    list.Add(new Cell { Block = "C3", Species = s, StepHeight = h, Ramps = 1, Count = 1, Trials = 2, Width = DefaultWidth });

            // Block A — the angle sweep. The angle is an OUTPUT: we are looking for the largest one
            // that passes, not confirming a value someone picked.
            foreach (float angle in new[] { 8f, 12f, 15f, 20f, 25f })
                foreach (var s in species)
                    list.Add(new Cell { Block = "A", Species = s, AngleDeg = angle, Ramps = 1, Count = 1, Trials = 5, Width = DefaultWidth });

            if (quick) return list;

            // Block B — the whole board. Block A sees one ramp; nine of them in a row is where
            // accumulated drift and nine repeated crests show up.
            foreach (float angle in new[] { 12f, 20f })
                foreach (var s in species)
                    list.Add(new Cell { Block = "B", Species = s, AngleDeg = angle, Ramps = 9, Count = 1, Trials = 5, Width = DefaultWidth });

            // Block C — crowds. A leader always gets through cleanly; what jams is behind it, and
            // that is exactly what an average erases. Verdicts here read the WORST creature.
            foreach (float angle in new[] { 12f, 20f })
                list.Add(new Cell { Block = "C", Species = "Velociraptor", AngleDeg = angle, Ramps = 9, Count = 12, Trials = 12, Width = DefaultWidth });

            // C-width — exploratory, not a gate. The parent design never specified a board width.
            foreach (float w in new[] { 6f, 16f })
                list.Add(new Cell { Block = "Cw", Species = "Velociraptor", AngleDeg = 20f, Ramps = 9, Count = 12, Trials = 6, Width = w });

            // D — rig validation. Fast-forward coarsens the control loop about fivefold, so any
            // verdict has to survive being reproduced at timeScale 1.
            foreach (var s in species)
                list.Add(new Cell { Block = "D", Species = s, AngleDeg = 20f, Ramps = 1, Count = 1, Trials = 5, Width = DefaultWidth, FaceTarget = true });

            return list;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) { Finish("left play mode"); return; }
            if (BattleManager.Instance == null) { Finish("BattleManager went away"); return; }

            // Rewritten every tick: BattleManager writes timeScale from five places and StartBattle
            // is one of them.
            Time.timeScale = 8f;

            bool timedOut = Time.time - trialStart > TrialTimeout;
            bool allDone = true;
            for (int i = 0; i < samplers.Count; i++)
                if (samplers[i] != null && !samplers[i].Finished) allDone = false;

            if (!allDone && !timedOut) return;

            RecordTrial();

            trialIndex++;
            if (trialIndex >= cells[cellIndex].Trials)
            {
                SummariseCell();
                cellIndex++;
                trialIndex = 0;
                cellTrials.Clear();
            }

            if (cellIndex >= cells.Count) { Finish(null); return; }
            StartTrial();
        }

        // ------------------------------------------------------------------ one trial

        private static void StartTrial()
        {
            var cell = cells[cellIndex];
            var manager = BattleManager.Instance;
            var definition = FindDefinition(manager, cell.Species);
            if (definition == null)
            {
                // Skip the whole cell rather than recording here — RecordTrial is Tick's job, and
                // calling it from inside StartTrial would have this trial counted twice.
                Debug.LogWarning($"[RampClimbProbe] No creature named '{cell.Species}' — skipping cell. " +
                                 "Roster display names come from CreatureBlueprints; check the spelling there.");
                cellIndex++;
                trialIndex = 0;
                cellTrials.Clear();

                if (cellIndex >= cells.Count) { Finish("ran out of cells"); return; }
                StartTrial();
                return;
            }

            BuildCourse(cell);

            manager.EnterPlacement();

            Vector3 start = new(CourseX, 0.5f, -RunupLength);
            for (int i = 0; i < cell.Count; i++)
            {
                // Spread the group across the width and back along the run-up so they arrive as a
                // column rather than a wall.
                float lane = cell.Count == 1 ? 0f : Mathf.Lerp(-cell.Width * 0.3f, cell.Width * 0.3f, i / (float)(cell.Count - 1));
                manager.Loadout.Add(new PlacedCreature
                {
                    Definition = definition,
                    Team = Team.Red,
                    Position = new Vector3(start.x + lane, start.y, start.z - i * 1.5f),
                    YawDegrees = 0f,
                });
            }

            // A sacrificial Blue, required and load-bearing. StartBattle refuses to run without one
            // creature on each side, and going through StartBattle is what puts the match into
            // Fighting — CreatureImpact does nothing outside that phase, so without this the body
            // collisions and staggers this probe is meant to test would be silently switched off.
            // Its brain is disabled below with everyone else's, so it just stands there.
            manager.Loadout.Add(new PlacedCreature
            {
                Definition = definition,
                Team = Team.Blue,
                Position = new Vector3(CourseX + cell.Width * 2f, 0.5f, -RunupLength),
                YawDegrees = 0f,
            });

            manager.StartBattle();

            samplers.Clear();
            foreach (var unit in UnitRegistry.AliveOf(Team.Red))
            {
                // Brains off. CreatureBrain.Update calls Steer too, and so does the sampler; with
                // undefined script order the two would race for commandedVelocity every frame. It
                // also removes targeting and the 90-unit aggro pull toward the sacrificial Blue.
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>()) brain.enabled = false;

                var sampler = unit.gameObject.AddComponent<RampClimbSampler>();
                sampler.Configure(marchPoint, entryGateS, exitGateS, courseEndS, courseColliders, cell.FaceTarget);
                samplers.Add(sampler);
            }

            foreach (var unit in UnitRegistry.AliveOf(Team.Blue))
                foreach (var brain in unit.GetComponentsInChildren<CreatureBrain>())
                    brain.enabled = false;

            if (prefabValues == "(not read)" && samplers.Count > 0) ReadPrefabValues(samplers[0]);

            trialStart = Time.time;
        }

        /// <summary>
        /// Look a species up across BOTH rosters.
        ///
        /// The bosses live in a separate asset, deliberately, so that one can never turn up as an
        /// ordinary auto-fill pick. That matters here because the slowest, heaviest creature with
        /// the largest collider — the Malformed Rex, which owns one of the three worst-case axes
        /// this probe has to cover — is a boss and is therefore absent from the main roster.
        /// </summary>
        private static CreatureDefinition FindDefinition(BattleManager manager, string species)
        {
            var found = manager.Roster != null ? manager.Roster.FindByName(species) : null;
            if (found != null) return found;

            var bosses = AssetDatabase.LoadAssetAtPath<CreatureRoster>(SampleContentBuilder.BossRosterPath);
            return bosses != null ? bosses.FindByName(species) : null;
        }

        /// <summary>
        /// Print the locomotion values that are actually in force.
        ///
        /// Configure overwrites only moveSpeed, turnSpeedDegrees and mass. acceleration, the ground
        /// probe, the masks and the smoothing all come from whatever is serialized in the prefab, so
        /// the C# defaults say nothing about what this run measured. Read via SerializedObject rather
        /// than by adding getters, because adding a public accessor to shipping code to make a gate
        /// pass is exactly the thing the gate is supposed to prevent.
        /// </summary>
        private static void ReadPrefabValues(Component sampleHost)
        {
            var loco = sampleHost.GetComponent<CreatureLocomotion>();
            if (loco == null) return;

            var so = new SerializedObject(loco);
            string Get(string field)
            {
                var p = so.FindProperty(field);
                if (p == null) return "?";
                return p.propertyType == SerializedPropertyType.Float
                    ? p.floatValue.ToString("0.##", CultureInfo.InvariantCulture)
                    : p.intValue.ToString();
            }

            prefabValues = $"accel={Get("acceleration")} probe={Get("groundProbeDistance")} " +
                           $"mask=0x{Get("groundMask")} smooth={Get("steeringSmoothing")} " +
                           $"brake={Get("brakeDamping")} faceTol={Get("moveFacingTolerance")}";
        }

        private static void RecordTrial()
        {
            var cell = cells[cellIndex];
            var result = new TrialResult { Total = samplers.Count };
            var times = new List<float>();

            foreach (var s in samplers)
            {
                if (s == null) continue;
                if (s.Arrived) result.Arrived++;

                // Only creatures that cleanly crossed both gates contribute a time. One that never
                // got there has no ratio to offer, and folding a timeout in as a large number would
                // make the median lie about the ones that did make it.
                if (!float.IsNaN(s.GateTime)) times.Add(s.GateTime);

                result.WorstBackslide = Mathf.Max(result.WorstBackslide, s.MaxBackslide);
                result.Stalls += s.StallCount;
                result.LongestStall = Mathf.Max(result.LongestStall, s.LongestStall);
                result.LongestUngroundedRun = Mathf.Max(result.LongestUngroundedRun, s.LongestUngroundedRun);
                result.LongestAirborne = Mathf.Max(result.LongestAirborne, s.LongestAirborne);
                result.PeakClearance = Mathf.Max(result.PeakClearance, s.PeakClearance);
                result.WorstLateral = Mathf.Max(result.WorstLateral, s.MaxLateral);
                result.WorstPenetration = Mathf.Max(result.WorstPenetration, s.MaxPenetration);

                csv.Add(string.Join(",", cell.Block, cell.Species,
                    F(cell.AngleDeg), F(cell.StepHeight), cell.Count, trialIndex,
                    s.Arrived ? 1 : 0, F(s.GateTime), F(s.MaxBackslide), s.StallCount,
                    F(s.LongestStall), s.LongestUngroundedRun, F(s.LongestAirborne),
                    F(s.PeakClearance), F(s.MaxLateral), F(s.MaxPenetration)));
            }

            // The crowd verdict reads the LAST arrival, not the leader's.
            times.Sort();
            result.MedianGateTime = times.Count > 0 ? times[times.Count / 2] : float.NaN;
            result.SlowestGateTime = times.Count > 0 ? times[times.Count - 1] : float.NaN;

            cellTrials.Add(result);
            TearDownTrial();
        }

        private static void TearDownTrial()
        {
            foreach (var s in samplers) if (s != null) Object.DestroyImmediate(s);
            samplers.Clear();

            if (BattleManager.Instance != null) BattleManager.Instance.EnterPlacement();

            if (course != null) Object.DestroyImmediate(course);
            course = null;
            courseColliders.Clear();
        }

        // ------------------------------------------------------------------ course

        /// <summary>
        /// Build the course at runtime, far from the arena.
        ///
        /// Not saved into Arena.unity: the no-hand-editing rule is about scene CONTENT, and this is
        /// laboratory apparatus rather than content. x = +500 keeps it clear of the ground plane
        /// (±88) and the boundary walls.
        /// </summary>
        private static void BuildCourse(Cell cell)
        {
            if (course != null) Object.DestroyImmediate(course);
            course = new GameObject("RampProbeCourse");
            course.transform.position = new Vector3(CourseX, 0f, 0f);
            courseColliders.Clear();

            float z = -RunupLength - 40f;
            float y = 0f;

            // Run-up: 12 units is enough to reach 95% of top speed at acceleration 20, plus room for
            // the half-unit spawn drop to settle, so the first gate is crossed at steady speed.
            AddSlab("Runup", new Vector3(0f, y - 0.5f, z + (RunupLength + 40f) * 0.5f),
                    new Vector3(cell.Width, 1f, RunupLength + 40f));
            z = 0f;

            AddSlab("Platform_0", new Vector3(0f, y - 0.5f, z + PlatformDepth * 0.5f),
                    new Vector3(cell.Width, 1f, PlatformDepth));
            z += PlatformDepth;

            // Entry gate sits before the first ramp; the exit gate is placed once the first ramp's
            // length is known. On flat ground there is no ramp to bracket, so the baseline uses a
            // fixed 20-unit span — a speed, not a time, is what carries over to the ramp cells.
            entryGateS = z - GatePad;
            exitGateS = float.NaN;

            for (int i = 0; i < cell.Ramps; i++)
            {
                if (cell.StepHeight > 0f)
                {
                    // Control C3: a vertical face rather than an incline. This is the geometry the
                    // hypothesis says will NOT work, and the height at which it stops working is the
                    // constraint every seam in the real arena has to respect.
                    y += cell.StepHeight;
                    AddSlab($"Step_{i}", new Vector3(0f, y - 0.5f, z + PlatformDepth * 0.5f),
                            new Vector3(cell.Width, 1f, PlatformDepth));
                    if (i == 0) exitGateS = z + PlatformDepth * 0.5f;
                    z += PlatformDepth;
                }
                else if (cell.AngleDeg <= 0.01f)
                {
                    // The baseline segment is twice as long as the measured span, so the exit gate
                    // lands well inside the course. It used to sit exactly two units PAST the finish
                    // line, so the creature stopped sampling before crossing it and every flat trial
                    // reported no time at all — which is why C1 could not produce a denominator.
                    const float FlatBaselineSpan = 20f;
                    AddSlab($"Flat_{i}", new Vector3(0f, y - 0.5f, z + FlatBaselineSpan),
                            new Vector3(cell.Width, 1f, FlatBaselineSpan * 2f));
                    if (i == 0) exitGateS = z + FlatBaselineSpan;
                    z += FlatBaselineSpan * 2f;
                }
                else
                {
                    float run = TierRise / Mathf.Tan(cell.AngleDeg * Mathf.Deg2Rad);
                    float length = Mathf.Sqrt(run * run + TierRise * TierRise);

                    var ramp = AddSlab($"Ramp_{i}",
                        new Vector3(0f, y + TierRise * 0.5f - 0.5f * Mathf.Cos(cell.AngleDeg * Mathf.Deg2Rad),
                                    z + run * 0.5f),
                        new Vector3(cell.Width, 1f, length));
                    ramp.transform.localRotation = Quaternion.Euler(-cell.AngleDeg, 0f, 0f);

                    z += run;
                    y += TierRise;

                    // Bracket the FIRST ramp only. The gate pair is the measured segment; a
                    // nine-ramp course keeps running past it so block B still walks the whole board.
                    if (i == 0) exitGateS = z + GatePad;

                    AddSlab($"Platform_{i + 1}", new Vector3(0f, y - 0.5f, z + PlatformDepth * 0.5f),
                            new Vector3(cell.Width, 1f, PlatformDepth));
                    z += PlatformDepth;
                }
            }

            if (float.IsNaN(exitGateS)) exitGateS = entryGateS + 20f;

            // The finish line is the last platform, short of the march point so a creature that
            // reaches the top counts as arrived without having to walk into Arrive's braking zone.
            courseEndS = z - GatePad;

            // Announce a course whose exit gate sits at or past the finish line, rather than
            // silently emitting NaN times. That is precisely how the flat baseline broke: the
            // creature stopped sampling two units before the gate it was supposed to cross, and the
            // only symptom was a dash in the ratio column.
            if (exitGateS >= courseEndS)
            {
                Debug.LogError($"[RampClimbProbe] Bad course: exit gate {exitGateS:0.0} is not before " +
                               $"the finish {courseEndS:0.0}. No time can be recorded for this cell.");
            }

            // The march point sits well past the last gate so the measured span is never inside
            // Arrive's slowing radius — otherwise this measures the braking curve, not the ramp.
            marchPoint = new Vector3(CourseX, y, z + MarchLead);
        }

        private static GameObject AddSlab(string slabName, Vector3 localPosition, Vector3 size)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = slabName;
            slab.transform.SetParent(course.transform, false);
            slab.transform.localPosition = localPosition;
            slab.transform.localScale = size;

            // Scenery rules apply here too, and a probe should not be measuring a shadow pass.
            var renderer = slab.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            courseColliders.Add(slab.GetComponent<Collider>());
            return slab;
        }

        // ------------------------------------------------------------------ reporting

        private static void SummariseCell()
        {
            var cell = cells[cellIndex];
            if (cellTrials.Count == 0) return;

            int arrived = 0, total = 0, stalls = 0;
            float backslide = 0f, longestStall = 0f, airborne = 0f, clearance = 0f, lateral = 0f, penetration = 0f;
            int ungrounded = 0;
            var medians = new List<float>();

            foreach (var t in cellTrials)
            {
                arrived += t.Arrived;
                total += t.Total;
                stalls += t.Stalls;
                backslide = Mathf.Max(backslide, t.WorstBackslide);
                longestStall = Mathf.Max(longestStall, t.LongestStall);
                ungrounded = Mathf.Max(ungrounded, t.LongestUngroundedRun);
                airborne = Mathf.Max(airborne, t.LongestAirborne);
                clearance = Mathf.Max(clearance, t.PeakClearance);
                lateral = Mathf.Max(lateral, t.WorstLateral);
                penetration = Mathf.Max(penetration, t.WorstPenetration);
                if (!float.IsNaN(t.SlowestGateTime)) medians.Add(t.SlowestGateTime);
            }

            medians.Sort();
            float medianTime = medians.Count > 0 ? medians[medians.Count / 2] : float.NaN;
            float worstTime = medians.Count > 0 ? medians[medians.Count - 1] : float.NaN;

            // C1 defines the flat baseline this species is measured against.
            if (cell.Block == "C1" && !float.IsNaN(medianTime) && medianTime > 0f)
                flatSpeed[cell.Species] = (exitGateS - entryGateS) / medianTime;

            float ratio = float.NaN, predicted = PredictedRatio(cell.Species, cell.AngleDeg);
            if (flatSpeed.TryGetValue(cell.Species, out float flat) && !float.IsNaN(medianTime) && flat > 0f)
                ratio = medianTime / ((exitGateS - entryGateS) / flat);

            var failures = new List<string>();
            if (arrived < total) failures.Add("G1");
            if (backslide > MaxBackslide) failures.Add("G2");
            if (stalls > 0) failures.Add("G3");
            if (!float.IsNaN(ratio) && ratio > MaxTimeRatio) failures.Add("G4");
            if (ungrounded > MaxUngroundedRun) failures.Add("G5");
            if (airborne > MaxAirborne || clearance > MaxPeakClearance) failures.Add("G6");
            if (lateral > cell.Width * 0.5f) failures.Add("G7");
            if (penetration > 0.05f) failures.Add("G8");

            bool failed = failures.Count > 0;

            // A control that is SUPPOSED to fail inverts the verdict. C2 passing would mean something
            // other than the ramp normal is lifting creatures, and every other number here is void.
            string verdict = cell.ExpectFailure
                ? (failed ? "PASS(expected fail)" : "*** CONTROL BROKEN — 65deg should be impossible ***")
                : (failed ? "FAIL " + string.Join(",", failures) : "PASS");

            string label = cell.StepHeight > 0f
                ? $"step {cell.StepHeight:0.0}"
                : $"{cell.AngleDeg:00}deg";

            report.Add($"  {cell.Block,-3} {label,-9} {cell.Species,-14} n={cell.Count,-2} " +
                       $"{arrived}/{total}  t={Fmt(medianTime)}/{Fmt(worstTime)}  " +
                       $"ratio={Fmt(ratio)} (pred {predicted:0.000})  back={backslide:0.00}  " +
                       $"stall={stalls}  !gnd={ungrounded}  air={airborne:0.00}/{clearance:0.00}  " +
                       $"lat={lateral:0.00}  {verdict}");
        }

        /// <summary>
        /// Steady-state prediction from the model in the design doc, section 1.2.
        ///
        /// Printed next to every measurement so G4's second clause is visible: a cell that passes but
        /// lands nowhere near the prediction has passed for a reason nobody understands, and that is
        /// not the same as passing.
        /// </summary>
        private static float PredictedRatio(string species, float angleDeg)
        {
            var manager = BattleManager.Instance;
            var definition = manager != null ? FindDefinition(manager, species) : null;
            if (definition == null) return float.NaN;

            float g = Mathf.Abs(Physics.gravity.y);
            float accel = 20f;   // serialized prefab value; the header prints the measured one
            float phi = Mathf.Atan(AssumedFriction);

            float vFlat = definition.moveSpeed - g * Mathf.Tan(phi) / accel;
            float vRamp = definition.moveSpeed - g * Mathf.Tan(angleDeg * Mathf.Deg2Rad + phi) / accel;

            return vRamp > 0.01f ? vFlat / vRamp : float.PositiveInfinity;
        }

        private static void Finish(string reason)
        {
            running = false;
            EditorApplication.update -= Tick;

            TearDownTrial();

            Time.timeScale = restoreTimeScale;
            Time.fixedDeltaTime = restoreFixedDelta;
            Application.targetFrameRate = restoreTargetFrameRate;

            // Always, including the error paths. A raised cap left behind would quietly weaken the
            // spiral guard for whatever runs next in this editor session.
            MobilePerformance.OfflineCatchUpSteps = null;

            var sb = new StringBuilder();
            sb.AppendLine($"[RampClimbProbe] {(quickRun ? "quick" : "full")} run complete" +
                          (reason != null ? $" (stopped: {reason})" : ""));
            sb.AppendLine($"  fixedDelta={Time.fixedDeltaTime:0.0000}  prefab: {prefabValues}");
            sb.AppendLine("  gates: G1 arrival 100% | G2 backslide<=0.5 | G3 stalls=0 | G4 ratio<=1.30 " +
                          "| G5 !grounded run<=10 | G6 air<=0.2s clear<=0.3 | G7 lateral in width | G8 no penetration");
            foreach (var line in report) sb.AppendLine(line);
            sb.AppendLine("  NOTE: crowd cells run n=12, which resolves an incidence of 22% or higher. " +
                          "Below that this is 'not seen', not 'clean'.");

            Debug.Log(sb.ToString());

            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Build", "Probe");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"ramp-{System.DateTime.Now:yyyyMMdd-HHmmss}.csv");
            System.IO.File.WriteAllLines(path, csv);
            Debug.Log($"[RampClimbProbe] per-creature rows: {path}");
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        private static string Fmt(float v) => float.IsNaN(v) ? "  -  " : v.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Per-creature sampler. One physics step, one sample.
    ///
    /// Execution order 1000 puts this after <see cref="CreatureLocomotion.FixedUpdate"/>, so
    /// IsGrounded and the body pose read as the values locomotion just finished producing rather
    /// than last step's. A one-step skew is not a rounding error here — it is exactly the size of
    /// the steering-dropout events this is looking for.
    ///
    /// It also issues the steering command, in the shape the gauntlet's march behaviour would, since
    /// the brains are disabled for the run.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class RampClimbSampler : MonoBehaviour
    {
        private CreatureUnit unit;
        private CreatureLocomotion locomotion;

        private Vector3 march;
        private float entryGate;
        private float exitGate;
        private float finishS;
        private bool faceTarget;
        private IReadOnlyList<Collider> courseColliders;

        private bool entered;
        private bool passedExit;
        private float entryTime;
        private Vector3 lastCommand;

        /// <summary>
        /// High-water mark of forward progress, seeded from the first sample rather than zero.
        ///
        /// Zero is not a neutral starting value: creatures spawn at the back of the run-up at
        /// s = -12, so a zero seed made the first sample look like a twelve-unit slide backwards and
        /// stamped an identical 12.00 on every trial in the run, flat ground included. The control
        /// row that should have read 0.00 is what exposed it.
        /// </summary>
        private float peakS = float.NegativeInfinity;

        /// <summary>
        /// Resting clearance, taken as the running minimum rather than a single standing reading.
        ///
        /// A creature is dropped half a unit at spawn and starts moving immediately, so waiting for
        /// a moment of stillness to calibrate can wait forever — and a baseline that never gets set
        /// makes every sample read as airborne, which would turn G6 into pure noise.
        /// </summary>
        private float restingClearance = float.PositiveInfinity;

        private readonly Queue<(float time, float s)> window = new();
        private float stallStart = -1f;
        private int ungroundedRun;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

        public bool Finished { get; private set; }
        public bool Arrived { get; private set; }
        public float GateTime { get; private set; } = float.NaN;
        public float MaxBackslide { get; private set; }
        public int StallCount { get; private set; }
        public float LongestStall { get; private set; }
        public int LongestUngroundedRun { get; private set; }
        public float LongestAirborne { get; private set; }
        public float PeakClearance { get; private set; }
        public float MaxLateral { get; private set; }
        public float MaxPenetration { get; private set; }

        private float airborneStart = -1f;

        public void Configure(Vector3 marchPoint, float entry, float exit, float finish,
                              IReadOnlyList<Collider> colliders, bool useFaceTarget)
        {
            unit = GetComponent<CreatureUnit>();
            locomotion = GetComponent<CreatureLocomotion>();
            march = marchPoint;
            entryGate = entry;
            exitGate = exit;
            finishS = finish;
            courseColliders = colliders;
            faceTarget = useFaceTarget;
        }

        private void FixedUpdate()
        {
            if (Finished || locomotion == null || unit == null) return;

            float maxSpeed = locomotion.MoveSpeed;

            // The same command shape the gauntlet's march fallback will produce, so the probe tests
            // the behaviour that will actually ship rather than a straight-line special case.
            Vector3 desired = SteeringBehaviors.Blend(maxSpeed,
                (SteeringBehaviors.Arrive(transform.position, march, maxSpeed, 6f), 1f),
                (SteeringBehaviors.Separation(unit, 3.5f, maxSpeed), 1.1f));

            locomotion.Steer(desired, faceTarget ? march : (Vector3?)null);
            lastCommand = desired;

            Sample();
        }

        private void Sample()
        {
            float s = transform.position.z;
            float lateral = Mathf.Abs(transform.position.x - march.x);
            MaxLateral = Mathf.Max(MaxLateral, lateral);

            // Backslide is measured against this creature's own high-water mark, not against the
            // start, so a creature that climbs then slips is caught even if it later recovers.
            if (float.IsNegativeInfinity(peakS)) peakS = s;
            peakS = Mathf.Max(peakS, s);
            MaxBackslide = Mathf.Max(MaxBackslide, peakS - s);

            float clearance = MeasureClearance();
            restingClearance = Mathf.Min(restingClearance, clearance);

            // Airborne is measured against a baseline that was OBSERVED, not computed. If the capsule
            // does not rest where the geometry says it should, that disagreement is itself a finding
            // — which is why the baseline comes from the rig and not from the model.
            //
            // Only from the entry gate onward: the spawn drop and its settle happen during the
            // run-up and would otherwise be logged as a launch.
            float baseline = float.IsPositiveInfinity(restingClearance) ? 0f : restingClearance;
            bool airborne = entered && clearance > baseline + RampClimbProbe.AirborneMargin;
            if (airborne)
            {
                if (airborneStart < 0f) airborneStart = Time.time;
                PeakClearance = Mathf.Max(PeakClearance, clearance - baseline);
            }
            else if (airborneStart >= 0f)
            {
                LongestAirborne = Mathf.Max(LongestAirborne, Time.time - airborneStart);
                airborneStart = -1f;
            }

            // Deliberately NOT trusting IsGrounded: its probe originates inside the creature's own
            // capsule, so if Unity ever reports that hit the flag is stuck true forever and this
            // metric would read zero for the wrong reason. The independent clearance ray above is
            // the cross-check.
            if (!locomotion.IsGrounded)
            {
                ungroundedRun++;
                LongestUngroundedRun = Mathf.Max(LongestUngroundedRun, ungroundedRun);
            }
            else ungroundedRun = 0;

            if (clearance < -0.05f) MaxPenetration = Mathf.Max(MaxPenetration, -clearance);

            if (entered) DetectStall(s);

            if (!entered && s >= entryGate) { entered = true; entryTime = Time.time; }

            // Two separate events. The gate pair times ONE ramp, cleanly, away from Arrive's braking
            // curve; the finish line is the top of the course. Collapsing them would end a nine-ramp
            // trial after the first ramp and quietly turn block B into block A.
            if (entered && !passedExit && s >= exitGate)
            {
                passedExit = true;
                GateTime = Time.time - entryTime;
            }

            if (s >= finishS)
            {
                Arrived = true;
                Finished = true;
            }
        }

        /// <summary>
        /// A stall is not the same as a stagger and not the same as arriving.
        ///
        /// All three clauses are required: barely moving, while being told to move hard, while still
        /// far enough from the target that Arrive is not braking. Drop the second and every stagger
        /// reads as a stall; drop the third and every normal arrival does.
        /// </summary>
        private void DetectStall(float s)
        {
            window.Enqueue((Time.time, s));
            while (window.Count > 0 && Time.time - window.Peek().time > RampClimbProbe.StallWindow)
                window.Dequeue();

            // Wait for the window to actually SPAN a second before judging it.
            //
            // The threshold is "15% of the distance commanded over one full second". Comparing that
            // against a window only a step or two wide means comparing a step's worth of travel to a
            // second's worth of budget, which is always a stall — and it was: every creature,
            // including on flat ground at full speed, logged one the moment it crossed the entry
            // gate. Another one the flat control caught.
            float span = window.Count > 0 ? Time.time - window.Peek().time : 0f;
            if (span < RampClimbProbe.StallWindow * 0.95f) return;

            float travelled = s - window.Peek().s;
            float commanded = lastCommand.magnitude;
            float remaining = Vector3.Distance(transform.position, march);

            bool barelyMoving = travelled < RampClimbProbe.StallFraction * locomotion.MoveSpeed * RampClimbProbe.StallWindow;
            bool toldToMove = commanded > 0.5f * locomotion.MoveSpeed;
            bool notArriving = remaining > RampClimbProbe.ArriveSlowing + 2f;

            if (barelyMoving && toldToMove && notArriving)
            {
                if (stallStart < 0f) { stallStart = Time.time; StallCount++; }
                LongestStall = Mathf.Max(LongestStall, Time.time - stallStart);
            }
            else stallStart = -1f;
        }

        /// <summary>
        /// Distance from the creature's feet to the course, using our own ray filtered to the course
        /// colliders. No new layer: TagManager.asset is project-wide state and a probe has no
        /// business editing it.
        /// </summary>
        private float MeasureClearance()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            int hits = Physics.RaycastNonAlloc(origin, Vector3.down, HitBuffer, 12f, ~0,
                                               QueryTriggerInteraction.Ignore);

            float best = float.PositiveInfinity;
            for (int i = 0; i < hits; i++)
            {
                bool onCourse = false;
                for (int c = 0; c < courseColliders.Count; c++)
                    if (courseColliders[c] == HitBuffer[i].collider) { onCourse = true; break; }

                if (onCourse) best = Mathf.Min(best, HitBuffer[i].distance - 0.5f);
            }

            return float.IsPositiveInfinity(best) ? 0f : best;
        }
    }
}
