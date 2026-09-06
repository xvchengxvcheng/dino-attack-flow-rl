using System;

namespace LlamAcademy.Dinos.Training
{
    public sealed class DinoStructuredObservationFrame
    {
        public float[] Global = new float[DinoTrainingObservationLayout.GlobalFields];
        public float[] Regions = new float[DinoTrainingObservationLayout.RegionCount * DinoTrainingObservationLayout.RegionFields];
        public float[] Walls = new float[DinoTrainingObservationLayout.MaxWalls * DinoTrainingObservationLayout.WallFields];
        public float[] Guards = new float[DinoTrainingObservationLayout.MaxGuards * DinoTrainingObservationLayout.GuardFields];
        public float[] Houses = new float[DinoTrainingObservationLayout.MaxHouses * DinoTrainingObservationLayout.HouseFields];
        public float[] Dinos = new float[DinoTrainingObservationLayout.MaxDinos * DinoTrainingObservationLayout.DinoFields];

        public float[][] Streams => new[] { Global, Regions, Walls, Guards, Houses, Dinos };

        public void Validate()
        {
            ValidateStream(Global, DinoTrainingObservationLayout.GlobalFields, nameof(Global));
            ValidateStream(Regions, DinoTrainingObservationLayout.RegionCount * DinoTrainingObservationLayout.RegionFields, nameof(Regions));
            ValidateStream(Walls, DinoTrainingObservationLayout.MaxWalls * DinoTrainingObservationLayout.WallFields, nameof(Walls));
            ValidateStream(Guards, DinoTrainingObservationLayout.MaxGuards * DinoTrainingObservationLayout.GuardFields, nameof(Guards));
            ValidateStream(Houses, DinoTrainingObservationLayout.MaxHouses * DinoTrainingObservationLayout.HouseFields, nameof(Houses));
            ValidateStream(Dinos, DinoTrainingObservationLayout.MaxDinos * DinoTrainingObservationLayout.DinoFields, nameof(Dinos));
        }

        private static void ValidateStream(float[] stream, int expectedLength, string name)
        {
            if (stream == null || stream.Length != expectedLength)
            {
                throw new InvalidOperationException($"{name} must contain exactly {expectedLength} values.");
            }

            for (int i = 0; i < stream.Length; i++)
            {
                if (float.IsNaN(stream[i]) || float.IsInfinity(stream[i]))
                {
                    throw new InvalidOperationException($"{name}[{i}] must be finite.");
                }
            }
        }
    }
}
