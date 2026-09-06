using System;
using System.Linq;
using LlamAcademy.Dinos.Training;
using NUnit.Framework;

namespace DinoAttack.Training.Core.Tests
{
    public class DinoStructuredObservationFrameTests
    {
        [Test]
        public void FrameAllocatesTheSixFrozenV2Streams()
        {
            DinoStructuredObservationFrame frame = new();

            Assert.That(DinoTrainingObservationLayout.ProtocolVersion,
                Is.EqualTo("dino_attack_structured_set_v2"));
            Assert.That(DinoTrainingObservationLayout.GlobalShape, Is.EqualTo(new[] { 5 }));
            Assert.That(DinoTrainingObservationLayout.RegionShape, Is.EqualTo(new[] { 5, 8 }));
            Assert.That(DinoTrainingObservationLayout.GuardShape, Is.EqualTo(new[] { 11, 7 }));
            Assert.That(DinoTrainingObservationLayout.TotalScalarCount, Is.EqualTo(270));
            Assert.That(frame.Streams.Select(stream => stream.Length),
                Is.EqualTo(new[] { 5, 40, 30, 77, 48, 70 }));
        }

        [Test]
        public void ValidateRejectsNonFiniteStreamValues()
        {
            DinoStructuredObservationFrame frame = new();
            frame.Dinos[4] = float.NaN;

            Assert.Throws<InvalidOperationException>(() => frame.Validate());
        }

        [Test]
        public void ValidateRejectsWrongLength()
        {
            DinoStructuredObservationFrame frame = new();
            frame.Walls = new float[1];

            Assert.Throws<InvalidOperationException>(() => frame.Validate());
        }
    }
}
