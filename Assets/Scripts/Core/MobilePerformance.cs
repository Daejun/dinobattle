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

        private void Awake()
        {
            // vSyncCount must be 0 for targetFrameRate to have any effect.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;

            Time.fixedDeltaTime = 1f / Mathf.Max(20f, physicsHz);

            // Cap catch-up work so a hitch cannot snowball into a physics death spiral.
            Time.maximumDeltaTime = Time.fixedDeltaTime * 4f;

            if (neverSleep) Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
