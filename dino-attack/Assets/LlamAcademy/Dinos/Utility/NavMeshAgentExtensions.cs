using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Utility
{
    public enum NavMeshPathRequestResult
    {
        Complete,
        InvalidAgent,
        TargetNotOnNavMesh,
        CalculationFailed,
        PartialPath,
        SetPathFailed
    }

    public static class NavMeshAgentExtensions
    {
        public static bool SetDestinationImmediate(
            this NavMeshAgent agent,
            Vector3 targetLocation,
            float positionLeniency = 0)
        {
            return agent.TrySetDestinationImmediate(
                targetLocation,
                out _,
                positionLeniency,
                true);
        }

        public static bool TrySetDestinationImmediate(
            this NavMeshAgent agent,
            Vector3 targetLocation,
            out NavMeshPathRequestResult result,
            float positionLeniency = 0,
            bool allowPartialPath = false)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                result = NavMeshPathRequestResult.InvalidAgent;
                return false;
            }

            NavMeshPath path = new();
            NavMeshQueryFilter queryFilter = new() {
                agentTypeID = agent.agentTypeID,
                areaMask = agent.areaMask
            };
            if (positionLeniency != 0)
            {
                if (!NavMesh.SamplePosition(targetLocation, out NavMeshHit hit, positionLeniency, queryFilter))
                {
                    result = NavMeshPathRequestResult.TargetNotOnNavMesh;
                    return false;
                }

                targetLocation = hit.position;
            }

            bool canSetPath = NavMesh.CalculatePath(
                agent.transform.position,
                targetLocation,
                queryFilter,
                path
            );

            if (!canSetPath)
            {
                result = NavMeshPathRequestResult.CalculationFailed;
                return false;
            }
            if (path.status == NavMeshPathStatus.PathPartial && !allowPartialPath)
            {
                result = NavMeshPathRequestResult.PartialPath;
                return false;
            }
            if (path.status != NavMeshPathStatus.PathComplete &&
                path.status != NavMeshPathStatus.PathPartial)
            {
                result = NavMeshPathRequestResult.CalculationFailed;
                return false;
            }
            if (!agent.SetPath(path))
            {
                result = NavMeshPathRequestResult.SetPathFailed;
                return false;
            }

            result = path.status == NavMeshPathStatus.PathComplete
                ? NavMeshPathRequestResult.Complete
                : NavMeshPathRequestResult.PartialPath;
            return true;
        }
    }
}
