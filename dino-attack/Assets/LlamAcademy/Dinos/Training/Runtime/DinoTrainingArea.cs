using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Training
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DinoTrainingArea : MonoBehaviour
    {
        [SerializeField] private BoxCollider PlacementBounds;
        [SerializeField] private LayerMask GroundLayers = ~0;
        [SerializeField] private LayerMask UnsafeLayers;
        [SerializeField, Min(0.01f)] private float PlacementRadius = 0.35f;
        [SerializeField, Min(0.1f)] private float RaycastHeight = 20f;
        [SerializeField, Min(0.1f)] private float NavMeshSampleRadius = 2f;

        public Bounds Bounds => PlacementBounds.bounds;

        private void Reset()
        {
            PlacementBounds = GetComponent<BoxCollider>();
            PlacementBounds.isTrigger = true;
        }

        public void Configure(BoxCollider bounds, LayerMask groundLayers, LayerMask unsafeLayers)
        {
            PlacementBounds = bounds;
            GroundLayers = groundLayers;
            UnsafeLayers = unsafeLayers;
            PlacementBounds.isTrigger = true;
        }

        public bool TryResolvePlacement(Vector2 normalizedPosition, out Vector3 position)
        {
            Bounds bounds = PlacementBounds.bounds;
            float x = Mathf.Lerp(bounds.min.x, bounds.max.x, (normalizedPosition.x + 1f) * 0.5f);
            float z = Mathf.Lerp(bounds.min.z, bounds.max.z, (normalizedPosition.y + 1f) * 0.5f);
            Vector3 origin = new(x, bounds.max.y + RaycastHeight, z);

            if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit hit,
                    RaycastHeight * 2f + bounds.size.y,
                    GroundLayers,
                    QueryTriggerInteraction.Ignore))
            {
                position = default;
                return false;
            }

            if (!NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, NavMeshSampleRadius, NavMesh.AllAreas))
            {
                position = default;
                return false;
            }

            position = navHit.position;
            if (position.x < bounds.min.x || position.x > bounds.max.x ||
                position.z < bounds.min.z || position.z > bounds.max.z)
            {
                return false;
            }

            if (Physics.CheckSphere(
                position + Vector3.up * PlacementRadius,
                PlacementRadius,
                UnsafeLayers,
                QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Collider[] overlaps = Physics.OverlapSphere(
                position + Vector3.up * PlacementRadius,
                PlacementRadius,
                ~0,
                QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
            {
                if (overlap.GetComponentInParent<DinoTrainingObstacle>() != null)
                {
                    return false;
                }
            }
            return true;
        }

        public Vector2 NormalizePosition(Vector3 worldPosition)
        {
            Bounds bounds = PlacementBounds.bounds;
            float x = Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPosition.x) * 2f - 1f;
            float z = Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldPosition.z) * 2f - 1f;
            return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(z, -1f, 1f));
        }
    }
}
