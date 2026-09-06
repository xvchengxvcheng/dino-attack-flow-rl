namespace LlamAcademy.Dinos.Training
{
    public static class DinoTrainingObservationLayout
    {
        public const string ProtocolVersion = "dino_attack_structured_set_v2";

        public const int GlobalFields = 5;
        public const int RegionCount = 5;
        public const int RegionFields = 8;
        public const int MaxWalls = 6;
        public const int WallFields = 5;
        public const int MaxGuards = 11;
        public const int GuardFields = 7;
        public const int MaxHouses = 8;
        public const int HouseFields = 6;
        public const int MaxDinos = 10;
        public const int DinoFields = 7;

        private static readonly int[] GlobalShapeValues = { GlobalFields };
        private static readonly int[] RegionShapeValues = { RegionCount, RegionFields };
        private static readonly int[] WallShapeValues = { MaxWalls, WallFields };
        private static readonly int[] GuardShapeValues = { MaxGuards, GuardFields };
        private static readonly int[] HouseShapeValues = { MaxHouses, HouseFields };
        private static readonly int[] DinoShapeValues = { MaxDinos, DinoFields };

        public static int[] GlobalShape => (int[])GlobalShapeValues.Clone();
        public static int[] RegionShape => (int[])RegionShapeValues.Clone();
        public static int[] WallShape => (int[])WallShapeValues.Clone();
        public static int[] GuardShape => (int[])GuardShapeValues.Clone();
        public static int[] HouseShape => (int[])HouseShapeValues.Clone();
        public static int[] DinoShape => (int[])DinoShapeValues.Clone();

        public const int TotalScalarCount = GlobalFields
                                            + RegionCount * RegionFields
                                            + MaxWalls * WallFields
                                            + MaxGuards * GuardFields
                                            + MaxHouses * HouseFields
                                            + MaxDinos * DinoFields;

        // Temporary compatibility aliases for the existing runtime producer. Task 2 replaces
        // that producer with the six-stream v2 contract; these aliases must not define new data.
        public const int GlobalSize = GlobalFields;
        public const int DinoSlotSize = DinoFields;
        public const int MaxDefenders = MaxGuards;
        public const int DefenderSlotSize = GuardFields;
        public const int WallSlotSize = WallFields;
        public const int ObservationSize = TotalScalarCount;
    }
}
