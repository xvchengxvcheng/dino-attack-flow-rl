using System;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;

namespace LlamAcademy.Dinos.Training
{
    public enum DinoStructuredObservationStream
    {
        Global,
        Regions,
        Walls,
        Guards,
        Houses,
        Dinos
    }

    public sealed class DinoStructuredObservationSensor : ISensor
    {
        private readonly DinoStructuredObservationBuilder Source;
        private readonly DinoStructuredObservationStream Stream;
        private readonly string Name;
        private readonly int Rows;
        private readonly int Fields;
        private readonly ObservationSpec Spec;

        public DinoStructuredObservationSensor(
            DinoStructuredObservationBuilder source,
            DinoStructuredObservationStream stream,
            string name,
            int rows,
            int fields)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Stream = stream;
            Name = string.IsNullOrEmpty(name) ? throw new ArgumentException("Sensor name is required.", nameof(name)) : name;
            Rows = rows;
            Fields = fields;
            Spec = rows == 1
                ? ObservationSpec.Vector(fields)
                : new ObservationSpec(
                    new InplaceArray<int>(rows, fields),
                    new InplaceArray<DimensionProperty>(DimensionProperty.None, DimensionProperty.None));
        }

        public ObservationSpec GetObservationSpec() => Spec;

        public int Write(ObservationWriter writer)
        {
            DinoStructuredObservationFrame frame = Source.GetFrameForAcademyStep(Academy.Instance.StepCount);
            float[] values = GetStream(frame, Stream);
            int expectedLength = Rows * Fields;
            if (values.Length != expectedLength)
            {
                throw new InvalidOperationException(
                    $"Sensor {Name} expected {expectedLength} values but received {values.Length}.");
            }

            if (Rows == 1)
            {
                for (int field = 0; field < Fields; field++)
                {
                    writer[field] = values[field];
                }
            }
            else
            {
                for (int row = 0; row < Rows; row++)
                {
                    for (int field = 0; field < Fields; field++)
                    {
                        writer[row, field] = values[row * Fields + field];
                    }
                }
            }

            return values.Length;
        }

        public byte[] GetCompressedObservation() => null;
        public void Update() { }
        public void Reset() { }
        public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();
        public string GetName() => Name;

        private static float[] GetStream(
            DinoStructuredObservationFrame frame,
            DinoStructuredObservationStream stream) => stream switch
        {
            DinoStructuredObservationStream.Global => frame.Global,
            DinoStructuredObservationStream.Regions => frame.Regions,
            DinoStructuredObservationStream.Walls => frame.Walls,
            DinoStructuredObservationStream.Guards => frame.Guards,
            DinoStructuredObservationStream.Houses => frame.Houses,
            DinoStructuredObservationStream.Dinos => frame.Dinos,
            _ => throw new ArgumentOutOfRangeException(nameof(stream), stream, null)
        };
    }
}
