using UnityEngine;

namespace DroneVR.Experiment
{
    public class NavigationTarget : MonoBehaviour
    {
        [SerializeField] private string targetId = "Target";
        [SerializeField] private string prompt = "Navigate to this location";
        [SerializeField] private float reachRadius = 3f;
        [SerializeField] private GameObject markerVisual;
        [SerializeField] private Color gizmoColor = new Color(0.2f, 0.9f, 0.4f, 0.85f);

        public string TargetId => string.IsNullOrWhiteSpace(targetId) ? gameObject.name : targetId;
        public string Prompt => string.IsNullOrWhiteSpace(prompt) ? "Navigate to this location" : prompt;
        public float ReachRadius => reachRadius;

        public bool IsReachedBy(Vector3 position, float? overrideRadius = null)
        {
            float threshold = overrideRadius ?? reachRadius;
            return Vector3.Distance(transform.position, position) <= threshold;
        }

        public void SetMarkerVisible(bool isVisible)
        {
            if (markerVisual != null)
            {
                markerVisual.SetActive(isVisible);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(transform.position, 0.35f);
            Gizmos.DrawWireSphere(transform.position, reachRadius);
        }
    }
}
