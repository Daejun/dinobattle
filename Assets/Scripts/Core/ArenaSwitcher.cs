using System.Collections.Generic;
using DinoBattle.CameraRig;
using UnityEngine;

namespace DinoBattle.Core
{
    /// <summary>
    /// Swaps which arena is standing, and re-fits the camera to it.
    ///
    /// Both boards are built into the scene and only one is active. A separate component rather than
    /// a branch inside <see cref="BattleManager"/> because none of this is match logic — it is set
    /// dressing and camera limits, and the manager should not grow a list of GameObjects to switch on
    /// and off every time a mode is added.
    ///
    /// The camera part is not optional. The rig clamps its pivot to a box, and until the gauntlet
    /// existed that box was a square centred on the origin sized to the round arena — 24.2 units.
    /// The board is roughly 290 units long and starts 600 units away, so without rebinding, the
    /// gauntlet camera could not pan far enough to see one tier, let alone follow a climb.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaSwitcher : MonoBehaviour
    {
        [Tooltip("Roots that make up the round arena. Switched off in gauntlet mode.")]
        [SerializeField] private List<GameObject> versusRoots = new();

        [Tooltip("The gauntlet board. Switched off in versus mode.")]
        [SerializeField] private GameObject gauntletRoot;

        [SerializeField] private OrbitCameraController cameraRig;

        [Tooltip("Pan box for the round arena, as (minX, minZ) and (maxX, maxZ).")]
        [SerializeField] private Vector2 versusPanMin = new(-24.2f, -24.2f);
        [SerializeField] private Vector2 versusPanMax = new(24.2f, 24.2f);

        [Tooltip("Pan box for the gauntlet board. Generated from the board's real extents.")]
        [SerializeField] private Vector2 gauntletPanMin = new(560f, -20f);
        [SerializeField] private Vector2 gauntletPanMax = new(640f, 320f);

        private BattleManager battleManager;

        private void Update()
        {
            // Lazy bind, for the usual reason: Awake order between GameObjects is undefined and
            // BattleManager.Instance is routinely still null when this component's Awake runs.
            if (battleManager != null) return;

            battleManager = BattleManager.Instance;
            if (battleManager == null) return;

            battleManager.ModeChanged += Apply;
            Apply(battleManager.Mode);
        }

        private void OnDisable()
        {
            if (battleManager == null) return;

            battleManager.ModeChanged -= Apply;
            battleManager = null;
        }

        private void Apply(GameMode mode)
        {
            bool gauntlet = mode == GameMode.Gauntlet;

            foreach (var root in versusRoots)
                if (root != null && root.activeSelf == gauntlet) root.SetActive(!gauntlet);

            if (gauntletRoot != null && gauntletRoot.activeSelf != gauntlet)
                gauntletRoot.SetActive(gauntlet);

            if (cameraRig == null) return;

            if (gauntlet)
            {
                cameraRig.SetPanBounds(gauntletPanMin, gauntletPanMax);
                cameraRig.FocusOn(new Vector3((gauntletPanMin.x + gauntletPanMax.x) * 0.5f, 0f, 10f), 46f);
            }
            else
            {
                cameraRig.SetPanBounds(versusPanMin, versusPanMax);
                cameraRig.FocusOn(Vector3.zero, 34f);
            }
        }

        /// <summary>Editor-only wiring, called by the scene builder.</summary>
        public void Configure(List<GameObject> versus, GameObject gauntlet, OrbitCameraController rig,
                              Vector2 gauntletMin, Vector2 gauntletMax)
        {
            versusRoots = versus;
            gauntletRoot = gauntlet;
            cameraRig = rig;
            gauntletPanMin = gauntletMin;
            gauntletPanMax = gauntletMax;
        }
    }
}
