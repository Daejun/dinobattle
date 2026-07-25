using UnityEngine;

namespace DinoBattle.CameraRig
{
    /// <summary>
    /// Spectator camera: one-finger drag orbits, two-finger pinch zooms, two-finger drag pans.
    /// Mouse fallback (drag / wheel / middle-drag) keeps it usable in the editor.
    /// </summary>
    public class OrbitCameraController : MonoBehaviour
    {
        [Header("Pivot")]
        [Tooltip("Point the camera orbits. Usually the center of the arena.")]
        [SerializeField] private Vector3 pivot = Vector3.zero;
        [SerializeField] private float panLimit = 80f;

        [Header("Orbit")]
        [SerializeField] private float yaw = 45f;
        [SerializeField] private float pitch = 35f;
        [SerializeField] private float orbitSensitivity = 0.2f;
        [SerializeField] private float minPitch = 8f;
        [SerializeField] private float maxPitch = 85f;

        [Header("Zoom")]
        [SerializeField] private float distance = 60f;
        [SerializeField] private float minDistance = 8f;
        [SerializeField] private float maxDistance = 200f;
        [SerializeField] private float pinchSensitivity = 0.12f;
        [SerializeField] private float wheelSensitivity = 8f;

        [Header("Feel")]
        [Tooltip("Higher is snappier. The camera eases toward its target transform each frame.")]
        [SerializeField] private float smoothing = 12f;

        private Vector3 targetPivot;
        private float targetYaw;
        private float targetPitch;
        private float targetDistance;
        private Vector2 lastPointer;
        private float lastPinchDistance;

        private void Awake()
        {
            targetPivot = pivot;
            targetYaw = yaw;
            targetPitch = pitch;
            targetDistance = distance;
        }

        /// <summary>Re-center the orbit, e.g. on the creature that just won.</summary>
        public void FocusOn(Vector3 worldPoint, float? newDistance = null)
        {
            targetPivot = ClampToArena(worldPoint);
            if (newDistance.HasValue) targetDistance = Mathf.Clamp(newDistance.Value, minDistance, maxDistance);
        }

        private void Update()
        {
            if (Input.touchCount > 0) HandleTouch();
            else HandleMouse();

            targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
        }

        private void LateUpdate()
        {
            // unscaledDeltaTime so camera control still feels right at 0.25x or while paused.
            float t = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);

            pivot = Vector3.Lerp(pivot, targetPivot, t);
            yaw = Mathf.LerpAngle(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            distance = Mathf.Lerp(distance, targetDistance, t);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
        }

        private void HandleTouch()
        {
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Moved) Orbit(touch.deltaPosition);
                lastPinchDistance = 0f;
                return;
            }

            Touch a = Input.GetTouch(0);
            Touch b = Input.GetTouch(1);
            float pinch = Vector2.Distance(a.position, b.position);

            if (lastPinchDistance > 0f) Zoom((lastPinchDistance - pinch) * pinchSensitivity);
            lastPinchDistance = pinch;

            Vector2 averageDelta = (a.deltaPosition + b.deltaPosition) * 0.5f;
            if (averageDelta.sqrMagnitude > 1f) Pan(averageDelta);
        }

        private void HandleMouse()
        {
            lastPinchDistance = 0f;

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2))
            {
                lastPointer = Input.mousePosition;
            }
            else if (Input.GetMouseButton(0) || Input.GetMouseButton(2))
            {
                Vector2 current = Input.mousePosition;
                Vector2 delta = current - lastPointer;
                lastPointer = current;

                if (Input.GetMouseButton(2)) Pan(delta);
                else Orbit(delta);
            }

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) Zoom(-wheel * wheelSensitivity);
        }

        private void Orbit(Vector2 delta)
        {
            targetYaw += delta.x * orbitSensitivity;
            targetPitch -= delta.y * orbitSensitivity;
        }

        private void Zoom(float amount)
        {
            targetDistance += amount * Mathf.Max(1f, targetDistance * 0.05f);
        }

        private void Pan(Vector2 delta)
        {
            // Pan in the camera's ground plane, scaled by distance so it feels the same at any zoom.
            Quaternion flat = Quaternion.Euler(0f, targetYaw, 0f);
            Vector3 move = flat * new Vector3(-delta.x, 0f, -delta.y) * (targetDistance * 0.0015f);
            targetPivot = ClampToArena(targetPivot + move);
        }

        private Vector3 ClampToArena(Vector3 point) => new(
            Mathf.Clamp(point.x, -panLimit, panLimit),
            point.y,
            Mathf.Clamp(point.z, -panLimit, panLimit));
    }
}
