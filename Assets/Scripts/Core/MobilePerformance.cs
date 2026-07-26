using UnityEngine;

namespace DinoBattle.Core
{
    /// <summary>
    /// Runtime device tuning. A physics-heavy spectator sim reads far better at a stable 60 than a
    /// stuttering uncapped frame rate, and the fixed timestep matters more than resolution here.
    /// Drop this on the BattleManager object.
    /// </summary>
    public class MobilePerformance : MonoBehaviour
    {
        [SerializeField] private int targetFrameRate = 60;

        [Tooltip("Physics steps per second. 50 is the Unity default; lower it if big fights drop frames.")]
        [SerializeField] private float physicsHz = 50f;

        [Tooltip("Keep the screen awake — the player is watching, not touching.")]
        [SerializeField] private bool neverSleep = true;

        [Tooltip("Most physics steps one frame may catch up on after a hitch, at ANY simulation " +
                 "speed. Four is roughly one frame's worth of slack at 60 fps.")]
        [Range(1, 12)]
        [SerializeField] private int maximumCatchUpSteps = 4;

        /// <summary>The timeScale and step budget the current cap was computed for.</summary>
        private float appliedTimeScale = float.NaN;
        private int appliedSteps = -1;

        /// <summary>
        /// Step budget for an offline measurement run, or null for the serialized value.
        ///
        /// The cap exists to stop a hitch snowballing into a physics death spiral on a player's
        /// phone. An editor probe has no player to protect and the opposite priority: it wants to
        /// burn simulation as fast as the machine allows.
        ///
        /// Pinning the budget at four steps per frame cost real throughput the moment it landed.
        /// BossBalanceProbe asks for 16x and got a true 16x before the cap became timeScale-aware;
        /// afterwards the same constant delivers 4.8x at 60 fps, so balance runs take 3.3 times as
        /// long in wall clock. The results were never wrong — the simulation is identical, only
        /// slower — but a probe should not be paying for a safety margin that protects nobody.
        ///
        /// A probe sets this on the way in and clears it on the way out. Static because it is a
        /// property of the RUN rather than of any one component, and because it must survive the
        /// scene teardown a probe does between trials. It resets to null on domain reload, so a
        /// probe that dies mid-run cannot leave the shipping cap raised.
        ///
        /// Never set this from gameplay code.
        /// </summary>
        public static int? OfflineCatchUpSteps { get; set; }

        /// <summary>Steps per frame currently in force, whether from the probe or the inspector.</summary>
        public int EffectiveCatchUpSteps => OfflineCatchUpSteps ?? maximumCatchUpSteps;

        private void Awake()
        {
            // vSyncCount must be 0 for targetFrameRate to have any effect.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;

            Time.fixedDeltaTime = 1f / Mathf.Max(20f, physicsHz);
            ApplyCatchUpCap();

            if (neverSleep) Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        private void Update()
        {
            // Watch rather than be told. BattleManager writes Time.timeScale from five places —
            // entering placement, replaying, declaring a result, pausing, and cycling speed — and a
            // cap that depends on it has to follow every one of them. One float compare a frame is
            // cheaper than a rule someone has to remember at five call sites.
            //
            // The step budget is watched the same way and for the same reason: a probe raising it
            // has no way to know when this component last recomputed, and should not have to.
            if (!Mathf.Approximately(Time.timeScale, appliedTimeScale) ||
                EffectiveCatchUpSteps != appliedSteps)
            {
                ApplyCatchUpCap();
            }
        }

        /// <summary>
        /// Bound how much simulation one frame may catch up on, in FIXED STEPS rather than seconds.
        ///
        /// This used to be a flat <c>fixedDeltaTime * 4</c>, which reads like "at most four physics
        /// steps per frame" and is only that at normal speed. Unity advances the fixed clock by the
        /// SCALED delta, so the real step count is
        /// <c>min(delta, maximumDeltaTime) * timeScale / fixedDeltaTime</c> — and this game offers a
        /// 4x fast-forward. At 4x the same constant permitted sixteen steps in a single frame: a
        /// hitch would queue sixteen rounds of physics plus every creature's FixedUpdate, which takes
        /// longer than a frame, which queues more catch-up. Precisely the death spiral the cap exists
        /// to prevent, held open by the cap itself.
        ///
        /// Dividing by timeScale keeps the promise the name makes at every speed.
        /// </summary>
        private void ApplyCatchUpCap()
        {
            appliedTimeScale = Time.timeScale;
            appliedSteps = EffectiveCatchUpSteps;

            // Paused runs no fixed steps at all, so the value is moot — the floor only stops a
            // division by zero from producing an infinite budget the moment play resumes.
            float scale = Mathf.Max(0.01f, appliedTimeScale);
            Time.maximumDeltaTime = appliedSteps * Time.fixedDeltaTime / scale;
        }
    }
}
