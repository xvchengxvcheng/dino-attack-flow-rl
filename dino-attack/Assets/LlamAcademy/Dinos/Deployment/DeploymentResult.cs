namespace LlamAcademy.Dinos.Deployment
{
    public enum DeploymentFailureReason
    {
        None,
        InvalidState,
        UnknownType,
        InsufficientFood,
        OutsideBounds,
        NoGround,
        NoNavMesh,
        Overlap,
        BlockedByUI,
        UnitCapReached,
        ConfigurationError
    }

    public readonly struct DeploymentResult
    {
        private DeploymentResult(bool allowed, DeploymentFailureReason failureReason)
        {
            Allowed = allowed;
            FailureReason = failureReason;
        }

        public bool Allowed { get; }
        public DeploymentFailureReason FailureReason { get; }

        public static DeploymentResult Success() => new(true, DeploymentFailureReason.None);

        public static DeploymentResult Rejected(DeploymentFailureReason reason) => new(false, reason);
    }
}
