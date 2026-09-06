using UnityEngine;

namespace LlamAcademy.Dinos.Deployment
{
    public enum DeploymentPhase
    {
        Setup,
        Running,
        Ending,
        Scoring,
        EnemyRepairs,
        Ended
    }

    public readonly struct DeploymentRequest
    {
        public DeploymentRequest(
            DeploymentPhase phase,
            bool isKnownDino,
            int foodAvailable,
            int foodCost,
            Vector3 worldPosition,
            bool isInsideBounds,
            bool hasGround,
            bool hasNavMesh,
            bool hasOverlap,
            bool isBlockedByUi)
        {
            Phase = phase;
            IsKnownDino = isKnownDino;
            FoodAvailable = foodAvailable;
            FoodCost = foodCost;
            WorldPosition = worldPosition;
            IsInsideBounds = isInsideBounds;
            HasGround = hasGround;
            HasNavMesh = hasNavMesh;
            HasOverlap = hasOverlap;
            IsBlockedByUi = isBlockedByUi;
        }

        public DeploymentPhase Phase { get; }
        public bool IsKnownDino { get; }
        public int FoodAvailable { get; }
        public int FoodCost { get; }
        public Vector3 WorldPosition { get; }
        public bool IsInsideBounds { get; }
        public bool HasGround { get; }
        public bool HasNavMesh { get; }
        public bool HasOverlap { get; }
        public bool IsBlockedByUi { get; }
    }
}
