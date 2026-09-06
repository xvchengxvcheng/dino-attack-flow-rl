using System.Collections.Generic;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Unit;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Utility
{
    public static class DinoTargetSelector
    {
        private static readonly HashSet<int> MissingMetadataDiagnostics = new();

        public static bool TrySelect(
            GameObject self,
            IReadOnlyList<GameObject> candidates,
            AttackConfigSO attackConfig,
            NavMeshQueryFilter filter,
            out GameObject selected)
        {
            selected = null;
            if (self == null || candidates == null || attackConfig == null)
            {
                return false;
            }

            if (!self.TryGetComponent(out NavMeshAgent agent)
                || !agent.enabled
                || !agent.isOnNavMesh)
            {
                return false;
            }

            IReadOnlyList<DinoTargetCategory> categories =
                DinoTargetPriorityProfile.GetCategories(attackConfig.TargetPriorityProfile);

            foreach (DinoTargetCategory category in categories)
            {
                List<GameObject> categoryObjects = new();
                List<DinoTargetSelectionCandidate> scoredCandidates = new();

                foreach (GameObject candidateObject in candidates)
                {
                    if (!TryDescribeCandidate(candidateObject, out IDamageable damageable, out DinoTargetCategory candidateCategory, out int stableId)
                        || candidateCategory != category)
                    {
                        continue;
                    }

                    bool reachable = TryCalculatePathLength(
                        self.transform.position,
                        candidateObject,
                        agent,
                        filter,
                        out float pathLength);

                    categoryObjects.Add(candidateObject);
                    scoredCandidates.Add(new DinoTargetSelectionCandidate(
                        candidateCategory,
                        stableId,
                        pathLength,
                        reachable));
                }

                int selectedIndex = DinoTargetSelectionRules.SelectIndex(
                    attackConfig.TargetPriorityProfile,
                    scoredCandidates);
                if (selectedIndex >= 0)
                {
                    selected = categoryObjects[selectedIndex];
                    return true;
                }
            }

            return false;
        }

        private static bool TryDescribeCandidate(
            GameObject candidateObject,
            out IDamageable damageable,
            out DinoTargetCategory category,
            out int stableId)
        {
            damageable = null;
            category = default;
            stableId = 0;

            if (candidateObject == null
                || !candidateObject.TryGetComponent(out damageable)
                || !DamageableState.IsAlive(damageable))
            {
                return false;
            }

            if (candidateObject.TryGetComponent(out DinoTargetMetadata metadata) && metadata.enabled)
            {
                category = metadata.Category;
                stableId = metadata.StableId;
                return true;
            }

            if (!TryGetHistoricalCategory(candidateObject, damageable, out category))
            {
                LogMissingMetadataOnce(candidateObject);
                return false;
            }

            stableId = ComputeStableHierarchyId(candidateObject.transform);
            return true;
        }

        private static bool TryGetHistoricalCategory(
            GameObject candidateObject,
            IDamageable damageable,
            out DinoTargetCategory category)
        {
            if (candidateObject.TryGetComponent(out Wall _))
            {
                category = DinoTargetCategory.Wall;
                return true;
            }

            if (candidateObject.TryGetComponent(out Defender _)
                || damageable.UnitType != null && IsDefenderUnitType(damageable.UnitType.Type))
            {
                category = DinoTargetCategory.Defender;
                return true;
            }

            if (damageable.UnitType != null && damageable.UnitType.Type == UnitType.Building)
            {
                category = DinoTargetCategory.House;
                return true;
            }

            category = default;
            return false;
        }

        private static bool IsDefenderUnitType(UnitType type)
        {
            return type == UnitType.Archer || type == UnitType.Mage || type == UnitType.Cannoneer;
        }

        private static bool TryCalculatePathLength(
            Vector3 source,
            GameObject target,
            NavMeshAgent agent,
            NavMeshQueryFilter filter,
            out float pathLength)
        {
            Vector3 destination = target.transform.position;
            if (target.TryGetComponent(out Collider targetCollider))
            {
                destination = targetCollider.ClosestPoint(source);
            }

            // Collider.ClosestPoint lies on the occupied target volume and is commonly
            // just outside the agent's baked NavMesh. Use the same nearby approach-point
            // idea as the movement action before rejecting a living target as unreachable.
            float sampleDistance = Mathf.Max(0.1f, agent.radius + agent.stoppingDistance);
            bool sampled = NavMesh.SamplePosition(
                destination,
                out NavMeshHit sampleHit,
                sampleDistance,
                filter);
            bool raycastHit = NavMesh.Raycast(source, destination, out NavMeshHit edgeHit, filter);
            if (sampled && raycastHit)
            {
                destination = Vector3.SqrMagnitude(sampleHit.position - destination)
                    <= Vector3.SqrMagnitude(edgeHit.position - destination)
                    ? sampleHit.position
                    : edgeHit.position;
            }
            else if (sampled)
            {
                destination = sampleHit.position;
            }
            else if (raycastHit)
            {
                destination = edgeHit.position;
            }

            NavMeshPath path = new();
            if (!NavMesh.CalculatePath(source, destination, filter, path)
                || path.status != NavMeshPathStatus.PathComplete
                || path.corners == null
                || path.corners.Length == 0)
            {
                pathLength = float.PositiveInfinity;
                return false;
            }

            pathLength = 0f;
            for (int index = 1; index < path.corners.Length; index++)
            {
                pathLength += Vector3.Distance(path.corners[index - 1], path.corners[index]);
            }

            return !float.IsNaN(pathLength) && !float.IsInfinity(pathLength);
        }

        private static int ComputeStableHierarchyId(Transform target)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Transform current = target;
                while (current != null)
                {
                    string name = current.name;
                    for (int index = 0; index < name.Length; index++)
                    {
                        hash = (hash ^ name[index]) * 16777619u;
                    }

                    hash = (hash ^ (uint)current.GetSiblingIndex()) * 16777619u;
                    current = current.parent;
                }

                return (int)(hash & 0x7FFFFFFF);
            }
        }

        private static void LogMissingMetadataOnce(GameObject target)
        {
            int instanceId = target.GetInstanceID();
            if (MissingMetadataDiagnostics.Add(instanceId))
            {
                Debug.LogWarning($"Ignoring attack target '{target.name}' because it has no DinoTargetMetadata or supported historical mapping.", target);
            }
        }
    }
}
