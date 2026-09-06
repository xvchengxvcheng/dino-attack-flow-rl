namespace LlamAcademy.Dinos.Deployment
{
    public static class DinoDeploymentRules
    {
        public static DeploymentResult Evaluate(DeploymentRequest request)
        {
            if (request.Phase != DeploymentPhase.Setup && request.Phase != DeploymentPhase.Running)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.InvalidState);
            }

            if (!request.IsKnownDino)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.UnknownType);
            }

            if (request.FoodAvailable < request.FoodCost)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.InsufficientFood);
            }

            if (!request.IsInsideBounds)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.OutsideBounds);
            }

            if (!request.HasGround)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.NoGround);
            }

            if (!request.HasNavMesh)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.NoNavMesh);
            }

            if (request.HasOverlap)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.Overlap);
            }

            if (request.IsBlockedByUi)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.BlockedByUI);
            }

            return DeploymentResult.Success();
        }
    }
}
