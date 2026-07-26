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

        /// <summary>The timeScale the current cap was computed for.</summary>
        private float appliedTimeScale = float.NaN;

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
            if (!Mathf.Approximately(Time.timeScale, appliedTimeScale)) ApplyCatchUpCap();
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

            // Paused runs no fixed steps at all, so the value is moot — the floor only stops a
            // division by zero from producing an infinite budget the moment play resumes.
            float scale = Mathf.Max(0.01f, appliedTimeScale);
            Time.maximumDeltaTime = maximumCatchUpSteps * Time.fixedDeltaTime / scale;
        }
    }
}
