using System;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace LlamAcademy.Dinos.Training
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DinoStructuredObservationBuilder))]
    public sealed class DinoStructuredObservationSensorComponent : SensorComponent
    {
        [SerializeField] private DinoStructuredObservationBuilder Source;

        private void Awake()
        {
            Source ??= GetComponent<DinoStructuredObservationBuilder>();
        }

        public override ISensor[] CreateSensors()
        {
            Source ??= GetComponent<DinoStructuredObservationBuilder>();
            if (Source == null)
            {
                throw new InvalidOperationException("Structured observation sensors require a frame builder.");
            }

            return new ISensor[]
            {
                new DinoStructuredObservationSensor(
                    Source,
                    DinoStructuredObservationStream.Global,
                    "00_Global",
                    1,
                    DinoTrainingObservationLayout.GlobalFields),
                new DinoStructuredObservationSensor(
                    Source,
                    DinoStructuredObservationStream.Regions,
                    "01_Regions",
                    DinoTrainingObservationLayout.RegionCount,
                    DinoTrainingObservationLayout.RegionFields),
                new DinoStructuredObservationSensor(
                    Source,
                    DinoStructuredObservationStream.Walls,
                    "02_Walls",
                    DinoTrainingObservationLayout.MaxWalls,
                    DinoTrainingObservationLayout.WallFields),
                new DinoStructuredObservationSensor(
                    Source,
                    DinoStructuredObservationStream.Guards,
                    "03_Guards",
                    DinoTrainingObservationLayout.MaxGuards,
                    DinoTrainingObservationLayout.GuardFields),
                new DinoStructuredObservationSensor(
                    Source,
                    DinoStructuredObservationStream.Houses,
                    "04_Houses",
                    DinoTrainingObservationLayout.MaxHouses,
                    DinoTrainingObservationLayout.HouseFields),
                new DinoStructuredObservationSensor(
                    Source,
                    DinoStructuredObservationStream.Dinos,
                    "05_Dinos",
                    DinoTrainingObservationLayout.MaxDinos,
                    DinoTrainingObservationLayout.DinoFields)
            };
        }
    }
}
