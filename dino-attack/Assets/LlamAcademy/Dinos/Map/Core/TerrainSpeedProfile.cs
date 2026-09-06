namespace LlamAcademy.Dinos.Map
{
    public static class TerrainSpeedProfile
    {
        public static float GetMultiplier(TerrainSurfaceKind surface) => surface switch
        {
            TerrainSurfaceKind.StoneRoad => 1.10f,
            TerrainSurfaceKind.Grass => 1.00f,
            TerrainSurfaceKind.Mud => 0.85f,
            TerrainSurfaceKind.Slope => 0.80f,
            TerrainSurfaceKind.ShallowWater => 0.70f,
            _ => 1.00f
        };

        public static int GetPriority(TerrainSurfaceKind surface) => surface switch
        {
            TerrainSurfaceKind.ShallowWater => 5,
            TerrainSurfaceKind.Mud => 4,
            TerrainSurfaceKind.Slope => 3,
            TerrainSurfaceKind.StoneRoad => 2,
            _ => 1
        };
    }
}
