using System;
using System.Collections.Generic;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Training;
using LlamAcademy.Dinos.Unit;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.RoundManagement
{
    public static class DinoEpisodeNavigationReadinessGate
    {
        private static readonly Vector2[] ZoneProbeCoordinates =
        {
            new(0.5f, 0.5f),
            new(0.2f, 0.2f),
            new(0.8f, 0.2f),
            new(0.8f, 0.8f),
            new(0.2f, 0.8f)
        };

        public static bool IsReady(
            RoundManager round,
            IReadOnlyList<Defender> defenders,
            IReadOnlyList<Dino> dinos,
            out string failureReason)
        {
            if (round == null || round.DinoTarget == null)
            {
                failureReason = "round target is unavailable";
                return false;
            }
            if (NavMeshManager.Instance == null || !NavMeshManager.Instance.IsReady)
            {
                failureReason = "NavMesh data is not installed";
                return false;
            }
            if (!AgentsAreReady(defenders, out failureReason) ||
                !AgentsAreReady(dinos, out failureReason))
            {
                return false;
            }

            DinoStructuredObservationBuilder builder =
                UnityEngine.Object.FindFirstObjectByType<DinoStructuredObservationBuilder>(
                    FindObjectsInactive.Include);
            if (builder == null)
            {
                // Historical scenes do not install the formal structured protocol.
                failureReason = string.Empty;
                return true;
            }

            DinoSO[] dinoTypes = builder.ConfiguredDinoTypes;
            DinoDeploymentZone[] zones = builder.ConfiguredZones;
            if (dinoTypes.Length == 0 || zones.Length == 0)
            {
                failureReason = "formal navigation probes are not configured";
                return false;
            }

            foreach (DinoSO dinoType in dinoTypes)
            {
                NavMeshAgent prefabAgent = dinoType == null || dinoType.Prefab == null
                    ? null
                    : dinoType.Prefab.GetComponent<NavMeshAgent>();
                if (prefabAgent == null)
                {
                    failureReason = $"dino type {dinoType?.name ?? "<null>"} has no NavMeshAgent";
                    return false;
                }

                NavMeshQueryFilter filter = new()
                {
                    agentTypeID = prefabAgent.agentTypeID,
                    areaMask = prefabAgent.areaMask
                };
                float sampleDistance = Mathf.Max(2f, prefabAgent.radius + prefabAgent.height);
                if (!NavMesh.SamplePosition(
                        round.DinoTarget.position,
                        out NavMeshHit targetHit,
                        20f,
                        filter))
                {
                    failureReason = $"{dinoType.name} target is not on its NavMesh";
                    return false;
                }

                foreach (DinoDeploymentZone zone in zones)
                {
                    if (zone == null || !HasCompletePathFromZone(
                            zone,
                            targetHit.position,
                            filter,
                            sampleDistance))
                    {
                        failureReason = $"{dinoType.name} has no complete path from {zone?.name ?? "<null>"}";
                        return false;
                    }
                }
            }

            failureReason = string.Empty;
            return true;
        }

        private static bool AgentsAreReady<T>(IReadOnlyList<T> units, out string failureReason)
            where T : Unit.Unit
        {
            if (units != null)
            {
                foreach (T unit in units)
                {
                    if (unit == null)
                    {
                        continue;
                    }
                    NavMeshAgent agent = unit.Agent;
                    if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                    {
                        failureReason = $"active unit {unit.name} is not registered on its NavMesh";
                        return false;
                    }
                }
            }

            failureReason = string.Empty;
            return true;
        }

        private static bool HasCompletePathFromZone(
            DinoDeploymentZone zone,
            Vector3 target,
            NavMeshQueryFilter filter,
            float sampleDistance)
        {
            if (!zone.IsGeometryValid)
            {
                return false;
            }

            foreach (Vector2 uv in ZoneProbeCoordinates)
            {
                if (!zone.TryResolveRelative(uv, out Vector3 candidate) ||
                    !NavMesh.SamplePosition(candidate, out NavMeshHit startHit, sampleDistance, filter))
                {
                    continue;
                }

                NavMeshPath path = new();
                if (NavMesh.CalculatePath(startHit.position, target, filter, path) &&
                    path.status == NavMeshPathStatus.PathComplete)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
