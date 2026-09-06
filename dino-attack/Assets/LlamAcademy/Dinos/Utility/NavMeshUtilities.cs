using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Utility
{
    public enum NavigationState
    {
        Moving,
        Arrived,
        NoPath,
        PartialPath,
        InvalidAgent
    }

    public static class NavMeshUtilities
    {
        public static float GetSquareDistanceOfPath(NavMeshPath path)
        {
            Vector3[] corners = path.corners;
            float distance = 0;
            for (int i = 1; i < corners.Length; i++)
            {
                distance += (corners[i - 1] - corners[i]).sqrMagnitude;
            }

            return distance;
        }

        public static NavigationState GetNavigationState(
            NavMeshAgent agent,
            Vector3 targetLocation,
            float targetTolerance = 0.25f)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return NavigationState.InvalidAgent;
            }
            if (agent.pathPending)
            {
                return NavigationState.Moving;
            }
            float arrivalDistance = agent.stoppingDistance + agent.radius;
            bool targetReached = Vector3.Distance(agent.transform.position, targetLocation) <=
                                 arrivalDistance + Mathf.Max(0f, targetTolerance);
            if (!agent.hasPath)
            {
                return targetReached ? NavigationState.Arrived : NavigationState.NoPath;
            }
            if (agent.pathStatus == NavMeshPathStatus.PathPartial)
            {
                return NavigationState.PartialPath;
            }
            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                return NavigationState.NoPath;
            }

            bool remainingDistanceReached = float.IsFinite(agent.remainingDistance) &&
                                            agent.remainingDistance <= arrivalDistance;
            return remainingDistanceReached && targetReached
                ? NavigationState.Arrived
                : NavigationState.Moving;
        }

        public static bool IsAtTargetLocation(NavMeshAgent agent) =>
            agent != null && agent.enabled && agent.isOnNavMesh && !agent.pathPending &&
            agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathInvalid &&
            float.IsFinite(agent.remainingDistance) &&
            agent.remainingDistance <= agent.stoppingDistance + agent.radius;

        public static bool IsAtTargetLocation(NavMeshAgent agent, Vector3 targetLocation) =>
            GetNavigationState(agent, targetLocation) == NavigationState.Arrived;
    }
}
