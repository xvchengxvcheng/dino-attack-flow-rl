using System;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using UnityEngine;

namespace LlamAcademy.Dinos.Training
{
    [DisallowMultipleComponent]
    public sealed class DinoStructuredObservationBuilder : MonoBehaviour
    {
        private const float MapMinX = -55.5f;
        private const float MapMaxX = 48.5f;
        private const float MapMinZ = -53f;
        private const float MapMaxZ = 29f;
        private const float HouseHealthScale = 325f;

        [SerializeField] private SessionDefenseLayoutController DefenseLayout;
        [SerializeField] private SessionGroundDefenseController GroundDefenseLayout;
        [SerializeField] private RoundManager RoundManagerSource;
        [SerializeField] private DinoSpawner DinoSpawnerSource;
        [SerializeField] private DinoTrainingAgent TrainingAgent;
        [SerializeField] private DinoDeploymentZone[] Zones = Array.Empty<DinoDeploymentZone>();
        [SerializeField] private DinoSO[] DinoTypes = Array.Empty<DinoSO>();
        [SerializeField, Min(1f)] private float FoodScale = 5000f;

        private int CachedAcademyStep = int.MinValue;
        private DinoStructuredObservationFrame CachedFrame;
        private bool HasInferenceActionValidity;
        private bool InferenceLastActionWasValid = true;

        public DinoSO[] ConfiguredDinoTypes => DinoTypes == null
            ? Array.Empty<DinoSO>()
            : (DinoSO[])DinoTypes.Clone();
        public DinoDeploymentZone[] ConfiguredZones => Zones == null
            ? Array.Empty<DinoDeploymentZone>()
            : (DinoDeploymentZone[])Zones.Clone();

        public void ConfigureZones(DinoDeploymentZone[] zones)
        {
            Zones = zones ?? Array.Empty<DinoDeploymentZone>();
            CachedAcademyStep = int.MinValue;
            CachedFrame = null;
        }

        public void SetInferenceLastActionValidity(bool wasValid)
        {
            HasInferenceActionValidity = true;
            InferenceLastActionWasValid = wasValid;
        }

        public DinoStructuredObservationFrame GetFrameForAcademyStep(int academyStep)
        {
            if (CachedFrame == null || CachedAcademyStep != academyStep)
            {
                CachedFrame = BuildFrame();
                CachedAcademyStep = academyStep;
            }

            return CachedFrame;
        }

        public DinoStructuredObservationFrame BuildFrame()
        {
            ResolveSources();
            ValidateSources();

            DinoStructuredObservationFrame frame = new();
            WriteGlobal(frame.Global);
            WriteRegions(frame.Regions);
            WriteWalls(frame.Walls);
            WriteGuards(frame.Guards);
            WriteHouses(frame.Houses);
            WriteDinos(frame.Dinos);
            frame.Validate();
            return frame;
        }

        private void ResolveSources()
        {
            RoundManagerSource ??= RoundManager.Instance;
            DinoSpawnerSource ??= DinoSpawner.Instance;
            DefenseLayout ??= FindFirstObjectByType<SessionDefenseLayoutController>(FindObjectsInactive.Include);
            GroundDefenseLayout ??= FindFirstObjectByType<SessionGroundDefenseController>(FindObjectsInactive.Include);
            TrainingAgent ??= GetComponent<DinoTrainingAgent>();
        }

        private void ValidateSources()
        {
            if (RoundManagerSource == null || DinoSpawnerSource == null || DefenseLayout == null || GroundDefenseLayout == null)
            {
                throw new InvalidOperationException(
                    "Structured observations require RoundManager, DinoSpawner, session wall layout, and session ground defense layout.");
            }

            if (RoundManagerSource.DinoTarget == null)
            {
                throw new InvalidOperationException("Structured observations require the RoundManager DinoTarget.");
            }

            if (Zones == null || Zones.Length != DinoTrainingObservationLayout.RegionCount)
            {
                throw new InvalidOperationException(
                    $"Structured observations require exactly {DinoTrainingObservationLayout.RegionCount} zone references.");
            }

            DinoDeploymentZone[] orderedZones = Zones.OrderBy(zone => zone == null ? int.MaxValue : (int)zone.Id).ToArray();
            if (orderedZones.Any(zone => zone == null))
            {
                throw new InvalidOperationException("Structured observations require non-null zone references.");
            }

            if (!orderedZones.Select(zone => zone.Id)
                    .SequenceEqual(Enum.GetValues(typeof(DeploymentZoneId)).Cast<DeploymentZoneId>()))
            {
                throw new InvalidOperationException("Structured observations require unique Zone IDs in the complete Z1-Z5 sequence.");
            }

            foreach (DinoDeploymentZone zone in orderedZones)
            {
                if (!zone.IsGeometryValid || zone.WorldVertices.Count != 4 ||
                    zone.WorldVertices.Any(vertex => !float.IsFinite(vertex.x) || !float.IsFinite(vertex.z)))
                {
                    throw new InvalidOperationException($"Structured observations require valid geometry for zone {zone.Id}.");
                }
            }

            if (DefenseLayout.AllWallSlots == null || DefenseLayout.AllWallSlots.Count != DinoTrainingObservationLayout.MaxWalls)
            {
                throw new InvalidOperationException(
                    $"Structured observations require exactly {DinoTrainingObservationLayout.MaxWalls} stable wall slots.");
            }
            if (DefenseLayout.AllWallSlots.Any(slot => slot == null))
            {
                throw new InvalidOperationException("Stable wall slots cannot contain null entries.");
            }

            if (RoundManagerSource.SessionHouses == null ||
                RoundManagerSource.SessionHouses.Count != DinoTrainingObservationLayout.MaxHouses)
            {
                throw new InvalidOperationException(
                    $"Structured observations require exactly {DinoTrainingObservationLayout.MaxHouses} stable houses.");
            }
            if (RoundManagerSource.SessionHouses.Any(house => house == null))
            {
                throw new InvalidOperationException("Stable house rows cannot contain null entries.");
            }
        }

        private void WriteGlobal(float[] values)
        {
            float remainingSeconds = RoundManager.SessionDurationSeconds - RoundManagerSource.RunningElapsedSeconds;
            values[0] = Mathf.Clamp01(remainingSeconds / RoundManager.SessionDurationSeconds);
            float scale = TrainingAgent == null ? FoodScale : TrainingAgent.FoodNormalizationScale;
            values[1] = Mathf.Clamp01(DinoSpawnerSource.ResourcesToSpend / Mathf.Max(1f, scale));
            bool lastActionWasValid = HasInferenceActionValidity
                ? InferenceLastActionWasValid
                : TrainingAgent == null || TrainingAgent.LastActionWasValid;
            values[2] = lastActionWasValid ? 1f : 0f;
            WritePosition(values, 3, RoundManagerSource.DinoTarget.position);
        }

        private void WriteRegions(float[] values)
        {
            DinoDeploymentZone[] orderedZones = Zones.OrderBy(zone => zone.Id).ToArray();
            for (int row = 0; row < orderedZones.Length; row++)
            {
                IReadOnlyList<Vector3> vertices = orderedZones[row].WorldVertices;
                int offset = row * DinoTrainingObservationLayout.RegionFields;
                for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
                {
                    WritePosition(values, offset + vertexIndex * 2, vertices[vertexIndex]);
                }
            }
        }

        private void WriteWalls(float[] values)
        {
            IReadOnlyList<int> activeIndices = DefenseLayout.ActiveWallSlotIndices;
            IReadOnlyList<Wall> activeWalls = DefenseLayout.ActiveWalls;
            if (activeIndices.Count != activeWalls.Count)
            {
                throw new InvalidOperationException("Active wall slot indices and active wall instances must remain aligned.");
            }

            Dictionary<int, Wall> activeByOriginalSlot = new();
            for (int index = 0; index < activeIndices.Count; index++)
            {
                activeByOriginalSlot.Add(activeIndices[index], activeWalls[index]);
            }

            (WallSpawnSlot slot, int originalIndex)[] slots = DefenseLayout.AllWallSlots
                .Select((slot, originalIndex) => (slot, originalIndex))
                .OrderBy(entry => entry.slot.StableId)
                .ToArray();
            for (int row = 0; row < slots.Length; row++)
            {
                WallSpawnSlot slot = slots[row].slot;
                activeByOriginalSlot.TryGetValue(slots[row].originalIndex, out Wall wall);
                bool active = IsLive(wall);
                int offset = row * DinoTrainingObservationLayout.WallFields;
                values[offset] = 1f;
                values[offset + 1] = active ? 1f : 0f;
                WritePosition(values, offset + 2, slot.transform.position);
                values[offset + 4] = active ? HealthRatio(wall) : 0f;
            }
        }

        private void WriteGuards(float[] values)
        {
            Defender[] wallGuards = DefenseLayout.ActiveGuards
                .Where(IsLive)
                .OrderBy(StableTargetId)
                .ToArray();
            Defender[] groundGuards = GroundDefenseLayout.ActiveGroundGuards
                .Where(IsLive)
                .OrderBy(StableTargetId)
                .ToArray();
            Defender[] guards = wallGuards.Concat(groundGuards).ToArray();
            if (guards.Length > DinoTrainingObservationLayout.MaxGuards)
            {
                throw new InvalidOperationException(
                    $"Live guard count {guards.Length} exceeds the formal limit {DinoTrainingObservationLayout.MaxGuards}.");
            }

            HashSet<Defender> wallGuardSet = new(wallGuards);
            for (int row = 0; row < guards.Length; row++)
            {
                Defender guard = guards[row];
                int offset = row * DinoTrainingObservationLayout.GuardFields;
                values[offset] = 1f;
                switch (guard.UnitType.Type)
                {
                    case UnitType.Archer:
                        values[offset + 1] = 1f;
                        break;
                    case UnitType.Mage:
                        values[offset + 2] = 1f;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Formal guard observations support Archer and Mage only, not {guard.UnitType.Type}.");
                }

                WritePosition(values, offset + 3, guard.transform.position);
                values[offset + 5] = HealthRatio(guard);
                values[offset + 6] = wallGuardSet.Contains(guard) ? 1f : 0f;
            }
        }

        private void WriteHouses(float[] values)
        {
            VillageHouse[] houses = RoundManagerSource.SessionHouses
                .OrderBy(house => house.StableHouseId)
                .ToArray();
            for (int row = 0; row < houses.Length; row++)
            {
                VillageHouse house = houses[row];
                bool alive = !house.IsDestroyed && house.Health > 0;
                int offset = row * DinoTrainingObservationLayout.HouseFields;
                values[offset] = 1f;
                values[offset + 1] = alive ? 1f : 0f;
                WritePosition(values, offset + 2, house.transform.position);
                values[offset + 4] = alive ? HealthRatio(house) : 0f;
                values[offset + 5] = Mathf.Clamp01(house.MaxHealth / HouseHealthScale);
            }
        }

        private void WriteDinos(float[] values)
        {
            IReadOnlyList<Dino> activeDinos = RoundManagerSource.ActiveDinoUnits;
            if (activeDinos.Count > DinoTrainingObservationLayout.MaxDinos)
            {
                throw new InvalidOperationException(
                    $"Active dino count {activeDinos.Count} exceeds the formal limit {DinoTrainingObservationLayout.MaxDinos}.");
            }

            Dino[] dinos = activeDinos
                .Where(IsLive)
                .OrderBy(dino => dino.GetInstanceID())
                .ToArray();
            for (int row = 0; row < dinos.Length; row++)
            {
                Dino dino = dinos[row];
                int typeIndex = Array.IndexOf(DinoTypes, dino.UnitType as DinoSO);
                if (typeIndex < 0 || typeIndex > 2)
                {
                    throw new InvalidOperationException(
                        $"Dino {dino.name} does not use one of the three configured formal DinoSO types.");
                }

                int offset = row * DinoTrainingObservationLayout.DinoFields;
                values[offset] = 1f;
                values[offset + 1 + typeIndex] = 1f;
                WritePosition(values, offset + 4, dino.transform.position);
                values[offset + 6] = HealthRatio(dino);
            }
        }

        private static int StableTargetId(Defender guard)
        {
            DinoTargetMetadata metadata = guard.GetComponent<DinoTargetMetadata>();
            return metadata == null ? guard.GetInstanceID() : metadata.StableId;
        }

        private static bool IsLive(LlamAcademy.Dinos.Unit.Unit unit) =>
            unit != null && unit.gameObject.activeInHierarchy && unit.Health > 0;

        private static float HealthRatio(IDamageable damageable) =>
            damageable.MaxHealth <= 0
                ? 0f
                : Mathf.Clamp01((float)damageable.Health / damageable.MaxHealth);

        private static void WritePosition(float[] values, int offset, Vector3 position)
        {
            values[offset] = NormalizeCoordinate(position.x, MapMinX, MapMaxX);
            values[offset + 1] = NormalizeCoordinate(position.z, MapMinZ, MapMaxZ);
        }

        private static float NormalizeCoordinate(float value, float minimum, float maximum) =>
            Mathf.Clamp(Mathf.InverseLerp(minimum, maximum, value) * 2f - 1f, -1f, 1f);
    }
}
