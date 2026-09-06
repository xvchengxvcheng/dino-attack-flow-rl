using System.Collections.Generic;
using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public sealed class DinoDeploymentZone : MonoBehaviour
    {
        [field: SerializeField] public DeploymentZoneId Id { get; private set; }
        [field: SerializeField] public string DisplayName { get; private set; }
        [field: SerializeField] public TerrainSurfaceKind DominantSurface { get; private set; }

        [SerializeField] private Vector2 VertexA = new(-0.5f, -0.5f);
        [SerializeField] private Vector2 VertexB = new(0.5f, -0.5f);
        [SerializeField] private Vector2 VertexC = new(0.5f, 0.5f);
        [SerializeField] private Vector2 VertexD = new(-0.5f, 0.5f);

        public bool IsGeometryValid => TryGetLocalGeometry(out _);

        public IReadOnlyList<Vector3> WorldVertices
        {
            get
            {
                return new[]
                {
                    ToWorld(VertexA),
                    ToWorld(VertexB),
                    ToWorld(VertexC),
                    ToWorld(VertexD)
                };
            }
        }

        public void ConfigureGeometry(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            VertexA = a;
            VertexB = b;
            VertexC = c;
            VertexD = d;
        }

        public bool Contains(Vector3 point)
        {
            if (!TryGetLocalGeometry(out QuadrilateralXZ geometry))
            {
                return false;
            }

            Vector3 local = transform.InverseTransformPoint(point);
            return geometry.Contains(new Vector2(local.x, local.z));
        }

        public bool TryResolveRelative(Vector2 uv01, out Vector3 worldPoint)
        {
            worldPoint = default;
            if (!TryGetLocalGeometry(out QuadrilateralXZ geometry) ||
                !geometry.TryEvaluate(uv01, out Vector2 localPoint))
            {
                return false;
            }

            worldPoint = transform.TransformPoint(new Vector3(localPoint.x, 0f, localPoint.y));
            return true;
        }

        private bool TryGetLocalGeometry(out QuadrilateralXZ geometry)
        {
            return QuadrilateralXZ.TryCreate(VertexA, VertexB, VertexC, VertexD, out geometry);
        }

        private Vector3 ToWorld(Vector2 localPoint)
        {
            return transform.TransformPoint(new Vector3(localPoint.x, 0f, localPoint.y));
        }
    }
}
