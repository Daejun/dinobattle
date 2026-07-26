using DinoBattle.Core;
using DinoBattle.Units;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DinoBattle.CameraRig
{
    /// <summary>
    /// Tap a creature and the camera stays on it. Tap the ground and it lets go.
    ///
    /// From a four-year-old's playtest: "내가 티라노 보고 있는데 화면이 저쪽으로 가버려. 내가 보고
    /// 싶은 애를 계속 보게 해줘." The director frames wherever the fighting is densest, which is the
    /// right default and the wrong answer when the player has picked someone to care about.
    ///
    /// Deliberately the ONLY thing a tap does. The same session asked for creatures to be pushed,
    /// thrown and told what to attack, and all of that would stop this being a spectator simulator.
    /// Choosing what to watch is not controlling the fight — it is the one request that gives the
    /// player something to do without taking the fight away from the AI.
    ///
    /// Hit detection does not use the physics colliders. Those are deliberately about a quarter of
    /// the visible body — see the note in SampleContentBuilder — so a child aiming at a dinosaur
    /// would miss it three times out of four. This casts against the rendered bounds instead, which
    /// is what the player is actually pointing at.
    /// </summary>
    [RequireComponent(typeof(OrbitCameraController))]
    public class CreatureFocusPicker : MonoBehaviour
    {
        [Tooltip("How far a press may travel and still count as a tap rather than a camera drag, " +
                 "as a fraction of screen height. Small hands wobble; a strict pixel threshold made " +
                 "every attempted tap register as an orbit.")]
        [Range(0.005f, 0.1f)]
        [SerializeField] private float tapSlack = 0.03f;

        [Tooltip("Longest a press can last and still be a tap. Beyond this the player is holding, " +
                 "which means they are moving the camera.")]
        [SerializeField] private float tapDuration = 0.4f;

        [Tooltip("Extra room around a creature's rendered bounds when testing a tap, in world units. " +
                 "Aiming at a Velociraptor on a phone is genuinely hard.")]
        [SerializeField] private float aimForgiveness = 0.6f;

        private BattleCameraDirector director;
        private Camera attachedCamera;

        private Vector2 pressPosition;
        private float pressTime;
        private bool pressActive;
        private bool pressStartedOverUI;

        private void Awake()
        {
            director = GetComponent<BattleCameraDirector>();
            attachedCamera = GetComponent<Camera>();
        }

        private void Update()
        {
            if (director == null || attachedCamera == null) return;

            if (!TryReadTap(out Vector2 screenPosition)) return;

            // A tap on a creature follows it; a tap on anything else lets go. Releasing on empty
            // ground is how the player gets the automatic framing back, and it needs no button.
            director.Follow(FindCreatureAt(screenPosition));
        }

        /// <summary>
        /// Was this frame the end of a short, stationary press? Out is the screen position.
        ///
        /// Written against the legacy Input Manager to match the rest of the project — see the input
        /// note in CLAUDE.md. Touch first, mouse as the editor fallback.
        /// </summary>
        private bool TryReadTap(out Vector2 screenPosition)
        {
            screenPosition = default;

            bool began, ended;
            Vector2 position;

            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                position = touch.position;
                began = touch.phase == TouchPhase.Began;
                ended = touch.phase == TouchPhase.Ended;

                // Two fingers is a pinch or a pan, never a tap.
                if (Input.touchCount > 1) pressActive = false;
            }
            else if (Application.isMobilePlatform)
            {
                return false;
            }
            else
            {
                position = Input.mousePosition;
                began = Input.GetMouseButtonDown(0);
                ended = Input.GetMouseButtonUp(0);
            }

            if (began)
            {
                pressActive = true;
                pressPosition = position;
                pressTime = Time.unscaledTime;

                // Checked at press time, not release. By release the finger may have slid off the
                // button, and a press that started on the HUD belongs to the HUD.
                pressStartedOverUI = IsOverUI(position);
            }

            if (!ended || !pressActive) return false;

            pressActive = false;
            if (pressStartedOverUI) return false;

            if (Time.unscaledTime - pressTime > tapDuration) return false;
            if ((position - pressPosition).magnitude > Screen.height * tapSlack) return false;

            screenPosition = position;
            return true;
        }

        private static bool IsOverUI(Vector2 position)
        {
            if (EventSystem.current == null) return false;

            if (Input.touchCount > 0)
                return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);

            return EventSystem.current.IsPointerOverGameObject();
        }

        /// <summary>
        /// The living creature nearest the camera under <paramref name="screenPosition"/>, or null.
        ///
        /// Walks the registry and tests the ray against each creature's renderer bounds rather than
        /// asking the physics system, because the physics capsules are far smaller than the animals.
        /// Ties are broken by distance to the camera so tapping an overlapping scrum picks the one in
        /// front, which is the one the player can see.
        /// </summary>
        private CreatureUnit FindCreatureAt(Vector2 screenPosition)
        {
            Ray ray = attachedCamera.ScreenPointToRay(screenPosition);

            CreatureUnit best = null;
            float nearest = float.MaxValue;

            foreach (Team team in new[] { Team.Red, Team.Blue })
            {
                var units = UnitRegistry.AliveOf(team);

                for (int i = 0; i < units.Count; i++)
                {
                    var unit = units[i];
                    if (unit == null || unit.IsDead) continue;

                    if (!TryGetAimBounds(unit, out Bounds bounds)) continue;
                    if (!bounds.IntersectRay(ray, out float distance)) continue;
                    if (distance >= nearest) continue;

                    nearest = distance;
                    best = unit;
                }
            }

            return best;
        }

        private bool TryGetAimBounds(CreatureUnit unit, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (var renderer in unit.GetComponentsInChildren<Renderer>())
            {
                // Markers, not the animal: the health bar floats above it and the team ring is a wide
                // flat disc on the ground, and including either makes the tap target the wrong shape.
                if (renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                if (renderer.GetComponentInParent<UI.HealthBarBillboard>() != null) continue;
                if (renderer.gameObject.name.Contains(CreatureRig.TeamRing)) continue;

                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (any) bounds.Expand(aimForgiveness);
            return any;
        }
    }
}
