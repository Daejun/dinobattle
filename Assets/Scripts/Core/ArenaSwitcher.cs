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

        [Header("Atmosphere")]
        [Tooltip("Jungle haze for the round arena — the values the scene builder sets up.")]
        [SerializeField] private Color versusFog = new(0.58f, 0.66f, 0.56f);
        [SerializeField] private Color versusSky = new(0.50f, 0.58f, 0.52f);
        [SerializeField] private Color versusEquator = new(0.36f, 0.42f, 0.33f);
        [SerializeField] private Color versusGround = new(0.20f, 0.23f, 0.17f);
        [SerializeField] private Vector2 versusFogRange = new(39.6f, 114.4f);

        [Tooltip("Sea air. Cooler, brighter and much further out — a green jungle haze over open " +
                 "water looks like a rendering fault rather than weather, and the board is long " +
                 "enough that the versus fog range would bury its far end in soup.")]
        [SerializeField] private Color gauntletFog = new(0.55f, 0.72f, 0.80f);
        [SerializeField] private Color gauntletSky = new(0.55f, 0.70f, 0.82f);
        [SerializeField] private Color gauntletEquator = new(0.40f, 0.55f, 0.65f);
        [SerializeField] private Color gauntletGround = new(0.10f, 0.24f, 0.32f);
        [Tooltip("Ends short of the gauntlet far clip (700) so the board fades out rather than " +
                 "vanishing at a plane, and starts far enough back that the near tiers are clear.")]
        [SerializeField] private Vector2 gauntletFogRange = new(220f, 640f);

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

            ApplyAtmosphere(gauntlet);

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

        /// <summary>
        /// Swap the lighting and haze with the arena.
        ///
        /// RenderSettings is scene-global, so the two boards cannot each carry their own — one has
        /// to write them on the way in. The fog RANGE matters as much as the colour: the round arena
        /// is 44 units across and the board is 336 long, so the jungle's fog distances would swallow
        /// everything past the third tier.
        /// </summary>
        private void ApplyAtmosphere(bool gauntlet)
        {
            // The far clip is the one that actually broke the view. It is 200, which comfortably
            // covers a 44-unit arena and cuts the 336-unit board off after its first platform — the
            // rest of the climb was not fogged out, it was never drawn. Fog then has to end before
            // the clip plane, or geometry pops out of existence while still visibly solid.
            var camera = cameraRig != null ? cameraRig.GetComponent<Camera>() : Camera.main;
            if (camera != null)
            {
                camera.farClipPlane = gauntlet ? 700f : 200f;

                // Clear colour matched to the fog, so the horizon is a fade rather than a hard line
                // between two unrelated colours. Left on the jungle green it read as a grey-green
                // sky meeting a blue sea at a seam.
                camera.backgroundColor = gauntlet ? gauntletFog : versusFog;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = gauntlet ? gauntletSky : versusSky;
            RenderSettings.ambientEquatorColor = gauntlet ? gauntletEquator : versusEquator;
            RenderSettings.ambientGroundColor = gauntlet ? gauntletGround : versusGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = gauntlet ? gauntletFog : versusFog;

            Vector2 range = gauntlet ? gauntletFogRange : versusFogRange;
            RenderSettings.fogStartDistance = range.x;
            RenderSettings.fogEndDistance = range.y;
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
