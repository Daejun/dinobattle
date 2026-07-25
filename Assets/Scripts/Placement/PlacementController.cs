using DinoBattle.Core;
using DinoBattle.Data;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DinoBattle.Placement
{
    /// <summary>
    /// Touch/mouse driven creature placement. Tap the ground to drop the selected creature for the
    /// selected team, spending from that team's budget. Uses the legacy Input Manager so the project
    /// compiles with zero extra packages; see Docs/setup.md for the Input System migration note.
    /// </summary>
    public class PlacementController : MonoBehaviour
    {
        [SerializeField] private Camera placementCamera;
        [SerializeField] private BattleManager battleManager;

        [Tooltip("Layers that count as valid arena floor.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Semi-transparent stand-in shown under the cursor before the tap lands.")]
        [SerializeField] private GameObject previewMarker;

        [Tooltip("Ignore taps that started on a UI element by this screen-edge margin, in pixels.")]
        [SerializeField] private float screenEdgeMargin = 8f;

        private Team activeTeam = Team.Red;
        private CreatureDefinition selected;

        /// <summary>True when the current press began on top of the HUD, so it must not place anything.</summary>
        private bool gestureStartedOverUI;

        /// <summary>Set on the frame the press is released, consumed once the tap has been resolved.</summary>
        private bool gestureEnded;

        public Team ActiveTeam => activeTeam;
        public CreatureDefinition Selected => selected;

        private void Awake()
        {
            if (placementCamera == null) placementCamera = Camera.main;
            if (battleManager == null) battleManager = BattleManager.Instance;
        }

        private void Update()
        {
            // Gesture bookkeeping runs every frame, even with nothing selected. The press that picks
            // a creature begins BEFORE anything is selected, and a Button fires on release — so if
            // these frames are skipped, that release arrives with a creature freshly selected, no
            // record that the press started on the HUD, and a dinosaur is dropped on the ground
            // behind the button the player just tapped.
            TrackGesture();

            bool placing = battleManager != null && battleManager.Phase == BattlePhase.Placement;

            if (!placing || selected == null)
            {
                ShowPreview(false, Vector3.zero);
                return;
            }

            if (!TryGetPointer(out Vector2 screenPosition, out bool tapped))
            {
                ShowPreview(false, Vector3.zero);
                return;
            }

            if (!TryGetGroundPoint(screenPosition, out Vector3 groundPoint))
            {
                ShowPreview(false, Vector3.zero);
                return;
            }

            bool valid = IsValidSpot(groundPoint);
            ShowPreview(true, groundPoint, valid);

            if (tapped && valid) Place(groundPoint);
        }

        // ---------------------------------------------------------------- public API for the HUD

        public void Select(CreatureDefinition definition) => selected = definition;

        public void SetActiveTeam(Team team) => activeTeam = team;

        public void ToggleActiveTeam() => activeTeam = activeTeam == Team.Red ? Team.Blue : Team.Red;

        public void UndoLast()
        {
            battleManager?.Loadout.RemoveLast(activeTeam);
        }

        // ---------------------------------------------------------------- internals

        private bool IsValidSpot(Vector3 point)
        {
            if (battleManager == null || selected == null) return false;
            if (!battleManager.Loadout.CanAfford(activeTeam, selected)) return false;
            return battleManager.Loadout.IsSpotFree(point, selected.footprintRadius);
        }

        private void Place(Vector3 point)
        {
            // Face the middle of the arena so the two sides start off looking at each other.
            Vector3 toCenter = -point;
            toCenter.y = 0f;
            float yaw = toCenter.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(toCenter, Vector3.up).eulerAngles.y
                : 0f;

            battleManager.Loadout.Add(new PlacedCreature
            {
                Definition = selected,
                Team = activeTeam,
                Position = point,
                YawDegrees = yaw
            });
        }

        private bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
        {
            point = Vector3.zero;
            if (placementCamera == null) return false;

            Ray ray = placementCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, groundMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            point = hit.point;
            return true;
        }

        /// <summary>
        /// Record whether the press currently in progress began on the HUD.
        ///
        /// Decided once, at the start of the gesture: by the time a finger lifts it is no longer over
        /// anything, so testing at TouchPhase.Ended always reports "not over UI".
        /// </summary>
        private void TrackGesture()
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);

                if (touch.phase == TouchPhase.Began) gestureStartedOverUI = IsPointerOverUI(touch.fingerId);
                else if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled) gestureEnded = true;

                return;
            }

            if (Input.GetMouseButtonDown(0)) gestureStartedOverUI = IsPointerOverUI(-1);
            if (Input.GetMouseButtonUp(0)) gestureEnded = true;
        }

        /// <summary>Resolve a pointer position and whether this frame is a "commit" tap.</summary>
        private bool TryGetPointer(out Vector2 screenPosition, out bool tapped)
        {
            screenPosition = default;
            tapped = false;

            bool blocked = gestureStartedOverUI;

            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                screenPosition = touch.position;
                tapped = touch.phase == TouchPhase.Ended;
            }
            else if (Application.isMobilePlatform)
            {
                return false;
            }
            else
            {
                screenPosition = Input.mousePosition;
                tapped = Input.GetMouseButtonUp(0);

                // With no button held there is no gesture to attribute, so fall back to a live test.
                // Keeps the ground preview from showing underneath the HUD while merely hovering.
                if (!Input.GetMouseButton(0) && !tapped && IsPointerOverUI(-1)) return false;
            }

            // Clear once the gesture is over, so the next press starts from a clean slate.
            if (gestureEnded)
            {
                gestureEnded = false;
                gestureStartedOverUI = false;
            }

            if (blocked) return false;

            return screenPosition.x >= screenEdgeMargin
                && screenPosition.y >= screenEdgeMargin
                && screenPosition.x <= Screen.width - screenEdgeMargin
                && screenPosition.y <= Screen.height - screenEdgeMargin;
        }

        /// <summary>
        /// Is the pointer over a HUD element? Placement raycasts against physics, which ignores the
        /// canvas entirely — without this, tapping a roster button also drops a creature on the ground
        /// behind it. Pass -1 for the mouse, or a touch's fingerId.
        /// </summary>
        private static bool IsPointerOverUI(int pointerId)
        {
            var events = EventSystem.current;
            if (events == null) return false;

            return pointerId < 0
                ? events.IsPointerOverGameObject()
                : events.IsPointerOverGameObject(pointerId);
        }

        private void ShowPreview(bool visible, Vector3 position, bool valid = true)
        {
            if (previewMarker == null) return;

            if (previewMarker.activeSelf != visible) previewMarker.SetActive(visible);
            if (!visible) return;

            previewMarker.transform.position = position;

            float radius = selected != null ? selected.footprintRadius : 1f;
            previewMarker.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);

            if (previewMarker.TryGetComponent<Renderer>(out var renderer))
            {
                Color tint = valid ? new Color(0.3f, 1f, 0.4f, 0.35f) : new Color(1f, 0.25f, 0.25f, 0.35f);
                if (renderer.material.HasProperty("_BaseColor")) renderer.material.SetColor("_BaseColor", tint);
                else if (renderer.material.HasProperty("_Color")) renderer.material.SetColor("_Color", tint);
            }
        }
    }
}
