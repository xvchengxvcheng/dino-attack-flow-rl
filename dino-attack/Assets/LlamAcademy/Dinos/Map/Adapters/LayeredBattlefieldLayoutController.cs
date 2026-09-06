using System;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Training;
using LlamAcademy.Dinos.Unit;
using UnityEngine;

namespace LlamAcademy.Dinos.Map.Adapters
{
    [Serializable]
    public sealed class LayeredBattlefieldZoneBinding
    {
        public int PresetIndex;
        public DeploymentZoneId ZoneId;
        public DinoDeploymentZone Zone;
    }

    [Serializable]
    public sealed class LayeredBattlefieldCandidateBinding
    {
        public string StableId;
        public GameObject Candidate;
    }

    [DefaultExecutionOrder(-40), DisallowMultipleComponent]
    public sealed class LayeredBattlefieldLayoutController : MonoBehaviour, IBattlefieldLayoutSignatureProvider
    {
        [SerializeField] private int DefaultLayoutSeed = 20260831;
        [SerializeField] private LayeredBattlefieldZoneBinding[] ZoneBindings = Array.Empty<LayeredBattlefieldZoneBinding>();
        [SerializeField] private LayeredBattlefieldCandidateBinding[] HouseBindings = Array.Empty<LayeredBattlefieldCandidateBinding>();
        [SerializeField] private LayeredBattlefieldCandidateBinding[] WallBindings = Array.Empty<LayeredBattlefieldCandidateBinding>();
        [SerializeField] private LayeredBattlefieldCandidateBinding[] GroundGuardBindings = Array.Empty<LayeredBattlefieldCandidateBinding>();
        [SerializeField] private LayeredBattlefieldCandidateBinding[] TargetBindings = Array.Empty<LayeredBattlefieldCandidateBinding>();
        [SerializeField] private RoundManager RoundManagerSource;
        [SerializeField] private DinoTrainingAgent TrainingAgent;
        [SerializeField] private DinoStructuredObservationBuilder ObservationBuilder;
        [SerializeField] private DinoDeploymentService DeploymentService;
        [SerializeField] private DinoDeploymentZonePresenter ZonePresenter;

        public LayeredBattlefieldLayoutSelection AppliedSelection { get; private set; }
        public BattlefieldLayoutSignature AppliedSignature { get; private set; }
        public bool IsApplied { get; private set; }

        private void Awake()
        {
            int seed = GameSessionRestartService.Instance == null ? DefaultLayoutSeed : GameSessionRestartService.Instance.CurrentStartSeed;
            if (!TryApply(seed, out string error))
            {
                Debug.LogError($"Layered battlefield layout could not be applied: {error}", this);
                enabled = false;
            }
        }

        public bool TryApply(int layoutSeed, out string error)
        {
            if (IsApplied)
            {
                error = "The layered battlefield layout has already been applied.";
                return false;
            }
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            if (!TryBuildBindingMaps(config, out BindingMaps maps, out error)) return false;

            LayeredBattlefieldLayoutSelection selection;
            try
            {
                selection = LayeredBattlefieldLayoutSelector.Select(config, layoutSeed);
            }
            catch (InvalidOperationException exception)
            {
                error = $"layered battlefield selection failed: {exception.Message}";
                return false;
            }

            DinoDeploymentZone[] activeZones = selection.Zones
                .OrderBy(value => value.ZoneId)
                .Select(value => maps.Zones[(selection.ZonePresetIndex, value.ZoneId)])
                .ToArray();
            if (!CanConfigureZonePresentation(activeZones, out error)) return false;

            DinoDeploymentZone[] previousServiceZones = DeploymentService == null
                ? null
                : DeploymentService.ConfiguredZones.ToArray();
            DeploymentService?.ConfigureZones(activeZones);
            if (ZonePresenter != null && !ZonePresenter.ConfigureZones(DeploymentService, activeZones))
            {
                DeploymentService?.ConfigureZones(previousServiceZones);
                error = "deployment zone presentation could not bind the active preset";
                return false;
            }

            SetOnlySelected(ZoneBindings.Select(value => value.Zone.gameObject),
                ZoneBindings.Where(value => value.PresetIndex == selection.ZonePresetIndex).Select(value => value.Zone.gameObject).ToHashSet());
            SetOnlySelected(HouseBindings, selection.Houses.Select(value => value.StableId));
            SetOnlySelected(WallBindings, selection.Walls.Select(value => value.StableId));
            SetOnlySelected(GroundGuardBindings, selection.Guards.Where(value => value.Kind != BattlefieldGuardKind.WallArcher).Select(value => value.StableId));
            SetOnlySelected(TargetBindings, new[] { selection.Target.StableId });
            ApplyHousePositions(selection.Houses, maps.Houses);

            TrainingAgent?.ConfigureZones(activeZones);
            ObservationBuilder?.ConfigureZones(activeZones);
            if (RoundManagerSource != null)
            {
                RoundManagerSource.ConfigureLayeredSession(
                    maps.Targets[selection.Target.StableId].transform,
                    selection.Houses.Select(value => maps.Houses[value.StableId].GetComponent<VillageHouse>()).ToArray(),
                    50);
            }
            AppliedSelection = selection;
            AppliedSignature = CaptureActualSignature(selection, maps);
            IsApplied = true;
            error = string.Empty;
            return true;
        }

        public IReadOnlyList<SelectedGuardPlacement> SelectedGroundGuards =>
            AppliedSelection?.Guards?.Where(value => value.Kind != BattlefieldGuardKind.WallArcher).ToArray()
            ?? Array.Empty<SelectedGuardPlacement>();

        public bool TryGetGroundGuardObject(string stableId, out GameObject candidate)
        {
            candidate = GroundGuardBindings?
                .FirstOrDefault(value => string.Equals(value.StableId, stableId, StringComparison.Ordinal))?
                .Candidate;
            return candidate != null;
        }

        public bool TryCapture(out BattlefieldLayoutSignature signature)
        {
            signature = AppliedSignature;
            return signature != null;
        }

        private bool TryBuildBindingMaps(LayeredBattlefieldLayoutConfig config, out BindingMaps maps, out string error)
        {
            maps = null;
            if (!TryValidateZones(config, out var zones, out error) ||
                !TryValidateCandidates(HouseBindings, config.HouseCandidates.Select(value => value.StableId), "house", out var houses, out error) ||
                !TryValidateCandidates(WallBindings, config.WallCandidates.Select(value => value.StableId), "wall", out var walls, out error) ||
                !TryValidateCandidates(GroundGuardBindings, config.GroundGuardCandidates.Select(value => value.StableId), "ground guard", out var guards, out error) ||
                !TryValidateCandidates(TargetBindings, config.TargetCandidates.Select(value => value.StableId), "target", out var targets, out error)) return false;
            maps = new BindingMaps(zones, houses, walls, guards, targets);
            return true;
        }

        private bool TryValidateZones(LayeredBattlefieldLayoutConfig config,
            out Dictionary<(int, DeploymentZoneId), DinoDeploymentZone> result, out string error)
        {
            result = new Dictionary<(int, DeploymentZoneId), DinoDeploymentZone>();
            if (ZoneBindings == null || ZoneBindings.Length != config.ZonePresets.Count * 5)
            {
                error = "zone bindings must contain exactly five zones for every preset";
                return false;
            }
            var presets = config.ZonePresets.ToDictionary(value => value.StableIndex);
            HashSet<DinoDeploymentZone> boundZones = new();
            foreach (LayeredBattlefieldZoneBinding binding in ZoneBindings)
            {
                if (binding == null || binding.Zone == null || !presets.TryGetValue(binding.PresetIndex, out BattlefieldZonePreset preset) ||
                    binding.Zone.Id != binding.ZoneId || !boundZones.Add(binding.Zone) ||
                    !result.TryAdd((binding.PresetIndex, binding.ZoneId), binding.Zone))
                {
                    error = "zone bindings contain a null, unknown, mismatched, or duplicate stable ID";
                    return false;
                }
                BattlefieldZoneDefinition expected = preset.Zones.SingleOrDefault(value => value.ZoneId == binding.ZoneId);
                if (expected == null || !GeometryMatches(binding.Zone, expected))
                {
                    error = $"zone binding {binding.PresetIndex}/{binding.ZoneId} does not match preset geometry";
                    return false;
                }
            }
            foreach (BattlefieldZonePreset preset in config.ZonePresets)
            foreach (DeploymentZoneId id in Enum.GetValues(typeof(DeploymentZoneId)).Cast<DeploymentZoneId>())
                if (!result.ContainsKey((preset.StableIndex, id)))
                {
                    error = $"zone preset {preset.StableIndex} is missing stable zone {id}";
                    return false;
                }
            error = string.Empty;
            return true;
        }

        private static bool GeometryMatches(DinoDeploymentZone actual, BattlefieldZoneDefinition expected)
        {
            Vector3[] actualVertices = actual.WorldVertices.ToArray();
            Vector2[] expectedVertices = { expected.Geometry.A, expected.Geometry.B, expected.Geometry.C, expected.Geometry.D };
            return actualVertices.Length == 4 && Enumerable.Range(0, 4).All(index =>
                new QuantizedBattlefieldPoint(new Vector2(actualVertices[index].x, actualVertices[index].z)).Equals(
                    new QuantizedBattlefieldPoint(expectedVertices[index])));
        }

        private bool CanConfigureZonePresentation(
            IReadOnlyList<DinoDeploymentZone> zones,
            out string error)
        {
            if (ZonePresenter == null)
            {
                error = string.Empty;
                return true;
            }
            if (DeploymentService == null || zones == null || zones.Count != 5 ||
                zones.Any(zone => zone == null || !zone.IsGeometryValid) ||
                zones.Select(zone => zone.Id).Distinct().Count() != 5)
            {
                error = "deployment zone presentation requires a valid deployment service and five valid zones";
                return false;
            }

            Renderer[] boundaries = zones
                .Select(zone => zone.transform.Find("Boundary")?.GetComponent<Renderer>())
                .ToArray();
            if (boundaries.Any(boundary => boundary == null) || boundaries.Distinct().Count() != boundaries.Length)
            {
                error = "deployment zone presentation requires one unique boundary renderer per active zone";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryValidateCandidates(LayeredBattlefieldCandidateBinding[] bindings, IEnumerable<string> expectedIds,
            string category, out Dictionary<string, GameObject> result, out string error)
        {
            result = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            HashSet<string> expected = expectedIds.ToHashSet(StringComparer.Ordinal);
            HashSet<GameObject> boundObjects = new();
            if (bindings == null || bindings.Length != expected.Count)
            {
                error = $"{category} bindings have missing or extra stable IDs";
                return false;
            }
            foreach (LayeredBattlefieldCandidateBinding binding in bindings)
                if (binding == null || binding.Candidate == null || string.IsNullOrWhiteSpace(binding.StableId) ||
                    !expected.Contains(binding.StableId) || !boundObjects.Add(binding.Candidate) ||
                    !result.TryAdd(binding.StableId, binding.Candidate))
                {
                    error = $"{category} bindings contain a null, unknown, or duplicate stable ID";
                    return false;
                }
            if (!expected.SetEquals(result.Keys))
            {
                error = $"{category} bindings have missing or extra stable IDs";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private BattlefieldLayoutSignature CaptureActualSignature(LayeredBattlefieldLayoutSelection selection, BindingMaps maps)
        {
            BattlefieldZoneSignature[] zones = Enum.GetValues(typeof(DeploymentZoneId)).Cast<DeploymentZoneId>()
                .Select(id => maps.Zones[(selection.ZonePresetIndex, id)])
                .Select(zone => new BattlefieldZoneSignature(zone.Id, zone.WorldVertices.Select(vertex => new Vector2(vertex.x, vertex.z)))).ToArray();
            var houses = selection.Houses.Select(value => Signature(value.StableId, maps.Houses[value.StableId].transform.position)).ToArray();
            var walls = selection.Walls.Select(value => Signature(value.StableId, maps.Walls[value.StableId].transform.position)).ToArray();
            List<BattlefieldEntitySignature> guards = selection.Guards.Where(value => value.Kind != BattlefieldGuardKind.WallArcher)
                .Select(value => maps.Guards.TryGetValue(value.StableId, out GameObject guard)
                    ? Signature(value.StableId, guard.transform.position)
                    : new BattlefieldEntitySignature(value.StableId, value.Position))
                .ToList();
            foreach (SelectedWallPlacement wall in selection.Walls)
            {
                GameObject wallObject = maps.Walls[wall.StableId];
                WallSpawnSlot slot = wallObject.GetComponent<WallSpawnSlot>();
                guards.Add(Signature($"wall_archer_{wall.StableId}", slot == null || slot.GuardAnchor == null ? wallObject.transform.position : slot.GuardAnchor.position));
            }
            return BattlefieldLayoutSignature.Create("layered_battlefield", selection.LayoutSeed, selection.ZonePresetIndex,
                zones, houses, walls, guards, new[] { Signature(selection.Target.StableId, maps.Targets[selection.Target.StableId].transform.position) });
        }

        private static void SetOnlySelected(IEnumerable<GameObject> candidates, ISet<GameObject> selected)
        { foreach (GameObject candidate in candidates) candidate.SetActive(selected.Contains(candidate)); }

        private static void ApplyHousePositions(
            IEnumerable<SelectedHousePlacement> houses,
            IReadOnlyDictionary<string, GameObject> bindings)
        {
            foreach (SelectedHousePlacement house in houses)
            {
                Transform transform = bindings[house.StableId].transform;
                Vector3 position = transform.position;
                transform.position = new Vector3(house.Position.x, position.y, house.Position.y);
            }
        }

        private static void SetOnlySelected(IEnumerable<LayeredBattlefieldCandidateBinding> bindings, IEnumerable<string> selectedIds)
        {
            HashSet<string> selected = selectedIds.ToHashSet(StringComparer.Ordinal);
            foreach (LayeredBattlefieldCandidateBinding binding in bindings) binding.Candidate.SetActive(selected.Contains(binding.StableId));
        }

        private static BattlefieldEntitySignature Signature(string stableId, Vector3 position) => new(stableId, new Vector2(position.x, position.z));

        private sealed class BindingMaps
        {
            public BindingMaps(Dictionary<(int, DeploymentZoneId), DinoDeploymentZone> zones, Dictionary<string, GameObject> houses,
                Dictionary<string, GameObject> walls, Dictionary<string, GameObject> guards, Dictionary<string, GameObject> targets)
            { Zones = zones; Houses = houses; Walls = walls; Guards = guards; Targets = targets; }
            public Dictionary<(int, DeploymentZoneId), DinoDeploymentZone> Zones { get; }
            public Dictionary<string, GameObject> Houses { get; }
            public Dictionary<string, GameObject> Walls { get; }
            public Dictionary<string, GameObject> Guards { get; }
            public Dictionary<string, GameObject> Targets { get; }
        }
    }
}
