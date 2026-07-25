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

        [SerializeField] private Gradient colorByHealth;

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
                transform.rotation = Quaternion.LookRotation(
                    transform.position - activeCamera.transform.position, Vector3.up);
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

            Color tint = colorByHealth != null && colorByHealth.colorKeys.Length > 0
                ? colorByHealth.Evaluate(normalized)
                : Color.Lerp(Color.red, Color.green, normalized);

            if (fillRenderer.material.HasProperty("_BaseColor")) fillRenderer.material.SetColor("_BaseColor", tint);
            else if (fillRenderer.material.HasProperty("_Color")) fillRenderer.material.SetColor("_Color", tint);
        }
    }
}
