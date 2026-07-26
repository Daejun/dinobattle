using DinoBattle.Units;
using UnityEngine;

namespace DinoBattle.UI
{
    /// <summary>
    /// World-space health bar that faces the camera. Attach to a child of a creature prefab with a
    /// quad or a UI Image whose local X scale represents the fill.
    /// </summary>
    public class HealthBarBillboard : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private Transform fill;
        [SerializeField] private Renderer fillRenderer;

        [Tooltip("Hide the bar entirely while the creature is at full health.")]
        [SerializeField] private bool hideWhenFull = true;

        [Tooltip("Fill colour at full health. Near-primary on purpose: the arena is green and hazy, " +
                 "and a muted green bar on green ground is the one thing a status readout must not be.")]
        [SerializeField] private Color healthyColor = new(0.10f, 1f, 0.20f);

        [Tooltip("Fill colour at half health. A bar that simply crossfades green to red spends the " +
                 "middle of its range as olive-brown, which is both hard to see and hard to read a " +
                 "value from. Passing through yellow keeps every point on the range distinct.")]
        [SerializeField] private Color warningColor = new(1f, 0.92f, 0.05f);

        [Tooltip("Fill colour as health approaches zero.")]
        [SerializeField] private Color criticalColor = new(1f, 0.10f, 0.05f);

        private Camera activeCamera;
        private Vector3 fillBaseScale = Vector3.one;

        private void Awake()
        {
            if (health == null) health = GetComponentInParent<Health>();
            if (fill != null) fillBaseScale = fill.localScale;
            if (fillRenderer == null && fill != null) fillRenderer = fill.GetComponent<Renderer>();
        }

        private void LateUpdate()
        {
            if (health == null) return;

            if (activeCamera == null) activeCamera = Camera.main;
            if (activeCamera != null)
            {
                // Copy the camera's rotation outright, rather than aiming at its position.
                //
                // LookRotation at the camera keeps the quad's normal pointing at the lens but leaves
                // its roll to be resolved against world up, so the bar picks up an apparent tilt that
                // varies with where it sits in the frame and with any roll on the rig. On screen that
                // read as bars lying at odd angles rather than as UI. Matching the camera's rotation
                // makes every bar exactly parallel to the near plane: always flat, always level.
                transform.rotation = activeCamera.transform.rotation;
            }

            float normalized = health.Normalized;
            bool visible = !health.IsDead && (!hideWhenFull || normalized < 0.999f);

            foreach (Transform child in transform)
            {
                if (child.gameObject.activeSelf != visible) child.gameObject.SetActive(visible);
            }

            if (!visible || fill == null) return;

            fill.localScale = new Vector3(fillBaseScale.x * normalized, fillBaseScale.y, fillBaseScale.z);

            if (fillRenderer == null) return;

            // Two plain colours rather than a Gradient. A serialized Gradient defaults to two WHITE
            // keys, so "has any colour keys" was always true and every bar evaluated to white — the
            // fill looked unpainted no matter what the prefab builder set.
            //
            // Three stops rather than two, meeting at yellow in the middle. A straight green-to-red
            // crossfade turns olive-brown around half health, which is both the hardest colour to
            // pick out against this arena and the hardest to read a value from.
            Color tint = normalized < 0.5f
                ? Color.Lerp(criticalColor, warningColor, normalized * 2f)
                : Color.Lerp(warningColor, healthyColor, (normalized - 0.5f) * 2f);

            if (fillRenderer.material.HasProperty("_BaseColor")) fillRenderer.material.SetColor("_BaseColor", tint);
            else if (fillRenderer.material.HasProperty("_Color")) fillRenderer.material.SetColor("_Color", tint);
        }
    }
}
