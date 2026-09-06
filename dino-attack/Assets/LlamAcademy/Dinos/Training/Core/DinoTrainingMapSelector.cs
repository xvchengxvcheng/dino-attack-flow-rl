namespace LlamAcademy.Dinos.Training
{
    public enum DinoTrainingMap
    {
        OpenTropicalBattlefield,
        LayeredBattlefield
    }

    public static class DinoTrainingMapSelector
    {
        public static DinoTrainingMap Select(int episodeSeed) =>
            (unchecked((uint)episodeSeed) & 1u) == 0u
                ? DinoTrainingMap.OpenTropicalBattlefield
                : DinoTrainingMap.LayeredBattlefield;
    }
}
