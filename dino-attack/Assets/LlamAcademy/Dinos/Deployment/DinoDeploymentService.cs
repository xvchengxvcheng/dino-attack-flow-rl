using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using LlamAcademy.Dinos.Map;

namespace LlamAcademy.Dinos.Deployment
{
    public sealed class DinoDeploymentService : MonoBehaviour
    {
        [SerializeField] private BoxCollider PlacementBounds;
        [SerializeField] private DinoDeploymentZone[] PlacementZones = System.Array.Empty<DinoDeploymentZone>();
        [SerializeField] private bool UseLegacyPlacementBounds;
        [SerializeField] private LayerMask GroundLayers = ~0;
        [SerializeField] private LayerMask UnsafeLayers;
        [SerializeField] private Transform NavigationBarrierRoot;
        [SerializeField, Min(0.01f)] private float PlacementRadius = 0.35f;
        [SerializeField, Min(0.1f)] private float RaycastHeight = 20f;
        [SerializeField, Min(0.1f)] private float NavMeshSampleRadius = 2f;

        private static readonly DeploymentZoneId[] RequiredZoneIds =
            (DeploymentZoneId[])Enum.GetValues(typeof(DeploymentZoneId));

        private DinoDeploymentZone[] ReadOnlySource;
        private IReadOnlyList<DinoDeploymentZone> ReadOnlyConfiguredZones;

        public void ConfigureZones(DinoDeploymentZone[] zones)
        {
            PlacementZones = zones ?? Array.Empty<DinoDeploymentZone>();
            ReadOnlySource = null;
            ReadOnlyConfiguredZones = null;
        }
        private readonly Collider[] BarrierHits = new Collider[32];

        public bool IsZoneConfigurationValid { get; private set; }

        public IReadOnlyList<DinoDeploymentZone> ConfiguredZones
        {
            get
            {
                DinoDeploymentZone[] zones = PlacementZones ?? Array.Empty<DinoDeploymentZone>();
                if (!ReferenceEquals(ReadOnlySource, zones))
                {
                    ReadOnlySource = zones;
                    ReadOnlyConfiguredZones = Array.AsReadOnly(zones);
                }

                return ReadOnlyConfiguredZones;
            }
        }

        public DeploymentResult Evaluate(
            DeploymentPhase phase,
            bool isKnownDino,
            int foodAvailable,
            int foodCost,
            Vector3 requestedPosition,
            bool isBlockedByUi,
            out Vector3 resolvedPosition)
        {
            return Evaluate(
                phase,
                isKnownDino,
                foodAvailable,
                foodCost,
                requestedPosition,
                PlacementRadius,
                isBlockedByUi,
                out resolvedPosition);
        }

        public DeploymentResult Evaluate(
            DeploymentPhase phase,
            bool isKnownDino,
            int foodAvailable,
            int foodCost,
            Vector3 requestedPosition,
            float footprintRadius,
            bool isBlockedByUi,
            out Vector3 resolvedPosition)
        {
            float resolvedRadius = Mathf.Max(PlacementRadius, footprintRadius);
            bool isInsideBounds = IsInsideApprovedZone(requestedPosition);
            bool hasGround = Physics.Raycast(
                requestedPosition + Vector3.up * RaycastHeight,
                Vector3.down,
                out RaycastHit groundHit,
                RaycastHeight * 2f,
                GroundLayers,
                QueryTriggerInteraction.Ignore);

            NavMeshHit navMeshHit = default;
            bool hasNavMesh = hasGround && NavMesh.SamplePosition(
                groundHit.point,
                out navMeshHit,
                NavMeshSampleRadius,
                NavMesh.AllAreas);
            resolvedPosition = hasNavMesh ? navMeshHit.position : requestedPosition;

            bool hasOverlap = hasNavMesh && (Physics.CheckSphere(
                    resolvedPosition + Vector3.up * resolvedRadius,
                    resolvedRadius,
                    UnsafeLayers,
                    QueryTriggerInteraction.Ignore)
                || IsInsideNavigationBarrier(resolvedPosition, resolvedRadius));

            DeploymentRequest request = new(
                phase,
                isKnownDino,
                foodAvailable,
                foodCost,
                resolvedPosition,
                isInsideBounds,
                hasGround,
                hasNavMesh,
                hasOverlap,
                isBlockedByUi);
            return DinoDeploymentRules.Evaluate(request);
        }

        private bool IsInsideNavigationBarrier(Vector3 position, float footprintRadius)
        {
            if (NavigationBarrierRoot == null)
            {
                return false;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(
                position + Vector3.up * footprintRadius,
                footprintRadius,
                BarrierHits,
                1 << 6,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < hitCount; index++)
            {
                Collider hit = BarrierHits[index];
                if (hit != null && hit.transform.IsChildOf(NavigationBarrierRoot))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInsideApprovedZone(Vector3 position)
        {
            if (PlacementZones != null && PlacementZones.Length > 0)
            {
                return TryGetZone(position, out _);
            }

            return UseLegacyPlacementBounds &&
                   PlacementBounds != null &&
                   DinoDeploymentZoneLayout.ContainsHorizontal(PlacementBounds.bounds, position);
        }

        public bool ValidateZoneConfiguration()
        {
            IsZoneConfigurationValid = false;
            IReadOnlyList<DinoDeploymentZone> zones = ConfiguredZones;
            if (zones.Count != RequiredZoneIds.Length)
            {
                return false;
            }

            HashSet<DinoDeploymentZone> uniqueZones = new();
            HashSet<DeploymentZoneId> foundIds = new();
            for (int i = 0; i < zones.Count; i++)
            {
                DinoDeploymentZone zone = zones[i];
                if (zone == null ||
                    !zone.IsGeometryValid ||
                    !uniqueZones.Add(zone) ||
                    !Enum.IsDefined(typeof(DeploymentZoneId), zone.Id) ||
                    !foundIds.Add(zone.Id))
                {
                    return false;
                }
            }

            for (int i = 0; i < RequiredZoneIds.Length; i++)
            {
                if (!foundIds.Contains(RequiredZoneIds[i]))
                {
                    return false;
                }
            }

            IsZoneConfigurationValid = true;
            return true;
        }

        public bool TryGetZone(Vector3 position, out DinoDeploymentZone zone)
        {
            if (!ValidateZoneConfiguration())
            {
                zone = null;
                return false;
            }

            IReadOnlyList<DinoDeploymentZone> zones = ConfiguredZones;
            for (int i = 0; i < zones.Count; i++)
            {
                DinoDeploymentZone candidate = zones[i];
                if (candidate != null && candidate.Contains(position))
                {
                    zone = candidate;
                    return true;
                }
            }

            zone = null;
            return false;
        }
    }
}
