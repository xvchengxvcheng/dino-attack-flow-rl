namespace LlamAcademy.Dinos.Map
{
    public readonly struct DeploymentHoverInfo
    {
        public DinoDeploymentZone Zone { get; }
        public string DisplayName => Zone.DisplayName;
        public TerrainSurfaceKind DominantSurface => Zone.DominantSurface;

        public DeploymentHoverInfo(DinoDeploymentZone zone)
        {
            Zone = zone;
        }
    }
}
