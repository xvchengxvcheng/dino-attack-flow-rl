using System;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Map.Adapters;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Enemy.Defense
{
    [DefaultExecutionOrder(6)]
    [DisallowMultipleComponent]
    public sealed class SessionGroundDefenseController : MonoBehaviour
    {
        private const int GroundGuardStableIdBase = 300;
        private readonly Collider[] BlockingHits = new Collider[64];

        [SerializeField] private DinoDeploymentZone[] DeploymentZones = Array.Empty<DinoDeploymentZone>();
        [SerializeField] private Defender ArcherPrefab;
        [SerializeField] private Defender MagePrefab;
        [SerializeField] private Transform SpawnRoot;
        [SerializeField] private Transform FaceTarget;
        [SerializeField] private Vector2 SpawnMin = new(-24f, -49f);
        [SerializeField] private Vector2 SpawnMax = new(20f, -21f);
        [SerializeField, Min(0f)] private float MinimumSpacing = 6f;
        [SerializeField, Min(0.1f)] private float SpawnClearance = 2.5f;
        [SerializeField, Min(0.1f)] private float NavMeshSampleRadius = 0.75f;
        [SerializeField, Min(5)] private int MaximumAttempts = 512;
        [SerializeField] private LayerMask BlockingLayers = 1 << 7;
        [SerializeField] private int DefaultStartSeed = 20260829;
        [SerializeField] private LayeredBattlefieldLayoutController LayeredLayout;

        private readonly List<Defender> GroundGuards = new();
        private bool IsInitialized;

        public IReadOnlyList<Defender> ActiveGroundGuards => GroundGuards;
        public int StartSeed { get; private set; }

        public event Action<Defender> GuardSpawned;

        public bool InitializeDefaultSession()
        {
            int seed = GameSessionRestartService.Instance == null
                ? DefaultStartSeed
                : GameSessionRestartService.Instance.CurrentStartSeed;
            return InitializeForSession(seed);
        }

        public bool InitializeForSession(int startSeed)
        {
            if (IsInitialized || !ValidateConfiguration())
            {
                return false;
            }

            Vector2[] selected;
            BattlefieldGuardKind[] kinds;
            if (LayeredLayout != null && LayeredLayout.IsApplied)
            {
                SelectedGuardPlacement[] guards = LayeredLayout.SelectedGroundGuards.ToArray();
                selected = guards.Select(value => value.Position).ToArray();
                kinds = guards.Select(value => value.Kind).ToArray();
                if (selected.Length != 8 || kinds.Count(value => value == BattlefieldGuardKind.Archer) != 5 ||
                    kinds.Count(value => value == BattlefieldGuardKind.Mage) != 3)
                {
                    Debug.LogError("Layered battlefield requires exactly eight ground guards (five archers and three mages).", this);
                    return false;
                }
            }
            else
            {
                Rect bounds = Rect.MinMaxRect(SpawnMin.x, SpawnMin.y, SpawnMax.x, SpawnMax.y);
                if (!GroundGuardSpawnPlanner.TrySelect(bounds, 5, MinimumSpacing, unchecked((uint)startSeed),
                        MaximumAttempts, IsAllowedCandidate, out selected))
                {
                    Debug.LogError("Could not find five valid separated public-ground positions for session defenders.", this);
                    return false;
                }
                kinds = new[] { BattlefieldGuardKind.Archer, BattlefieldGuardKind.Archer, BattlefieldGuardKind.Archer,
                    BattlefieldGuardKind.Mage, BattlefieldGuardKind.Mage };
                int[] assignment = DeterministicSlotSelector.SelectWithoutReplacement(
                    kinds.Length, kinds.Length, unchecked((uint)startSeed) ^ 0xA511E9B3u);
                kinds = assignment.Select(index => kinds[index]).ToArray();
            }
            int archerNumber = 0;
            int mageNumber = 0;

            for (int index = 0; index < selected.Length; index++)
            {
                Defender prefab = kinds[index] == BattlefieldGuardKind.Archer ? ArcherPrefab : MagePrefab;
                NavMeshAgent prefabAgent = prefab.GetComponent<NavMeshAgent>();
                NavMeshQueryFilter filter = CreateFilter(prefabAgent);
                Vector3 candidate = new(selected[index].x, 0f, selected[index].y);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavMeshSampleRadius, filter))
                {
                    Debug.LogError($"Validated ground guard point {candidate} is unavailable for {prefab.name}.", this);
                    CleanupSpawnedGuards();
                    return false;
                }

                Vector3 direction = FaceTarget == null ? Vector3.forward : FaceTarget.position - hit.position;
                direction.y = 0f;
                Quaternion rotation = direction.sqrMagnitude < 0.01f
                    ? Quaternion.identity
                    : Quaternion.LookRotation(direction.normalized);
                Defender guard = Instantiate(prefab, hit.position, rotation, SpawnRoot);
                bool isArcher = guard.UnitType.Type == UnitType.Archer;
                int typeNumber = isArcher ? ++archerNumber : ++mageNumber;
                guard.name = $"Ground {(isArcher ? "Archer" : "Mage")} {typeNumber}";

                DinoTargetMetadata metadata = guard.GetComponent<DinoTargetMetadata>();
                if (metadata == null)
                {
                    metadata = guard.gameObject.AddComponent<DinoTargetMetadata>();
                }
                metadata.Configure(DinoTargetCategory.Defender, GroundGuardStableIdBase + index + 1);

                GroundGuards.Add(guard);
                GuardSpawned?.Invoke(guard);
            }

            StartSeed = startSeed;
            IsInitialized = true;
            return true;
        }

        private bool IsAllowedCandidate(Vector2 candidate)
        {
            Vector3 world = new(candidate.x, 0f, candidate.y);
            if (DeploymentZones.Any(zone => zone != null && zone.Contains(world)))
            {
                return false;
            }

            NavMeshAgent prefabAgent = ArcherPrefab == null ? null : ArcherPrefab.GetComponent<NavMeshAgent>();
            if (!NavMesh.SamplePosition(world, out NavMeshHit hit, NavMeshSampleRadius, CreateFilter(prefabAgent)))
            {
                return false;
            }
            Vector2 resolved = new(hit.position.x, hit.position.z);
            if (Vector2.Distance(candidate, resolved) > NavMeshSampleRadius)
            {
                return false;
            }

            int blockers = Physics.OverlapSphereNonAlloc(
                hit.position + Vector3.up,
                SpawnClearance,
                BlockingHits,
                BlockingLayers,
                QueryTriggerInteraction.Ignore);
            return blockers == 0;
        }

        private bool ValidateConfiguration()
        {
            if (DeploymentZones == null || DeploymentZones.Length != 5 || DeploymentZones.Any(zone => zone == null) ||
                ArcherPrefab == null || MagePrefab == null || SpawnRoot == null || SpawnMin.x >= SpawnMax.x ||
                SpawnMin.y >= SpawnMax.y || MinimumSpacing < 0f || MaximumAttempts < 5)
            {
                Debug.LogError("Session ground defense requires five zones, Archer/Mage prefabs, a spawn root, and valid bounds.", this);
                return false;
            }
            return true;
        }

        private static NavMeshQueryFilter CreateFilter(NavMeshAgent agent) => new()
        {
            agentTypeID = agent == null ? 0 : agent.agentTypeID,
            areaMask = agent == null ? NavMesh.AllAreas : agent.areaMask
        };

        private void CleanupSpawnedGuards()
        {
            foreach (Defender guard in GroundGuards)
            {
                if (guard != null)
                {
                    Destroy(guard.gameObject);
                }
            }
            GroundGuards.Clear();
        }
    }
}
