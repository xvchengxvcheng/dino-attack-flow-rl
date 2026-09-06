using System;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Map.Adapters;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using UnityEngine;

namespace LlamAcademy.Dinos.Enemy.Defense
{
    [DefaultExecutionOrder(5)]
    [DisallowMultipleComponent]
    public sealed class SessionDefenseLayoutController : MonoBehaviour
    {
        [SerializeField] private WallSpawnSlot[] Slots = Array.Empty<WallSpawnSlot>();
        [SerializeField] private Wall WallPrefab;
        [SerializeField] private Defender ArcherPrefab;
        [SerializeField] private Transform SpawnRoot;
        [SerializeField] private int DefaultStartSeed = 20260829;
        [SerializeField, Min(1f)] private float WallGuardAttackRadius = 30f;
        [SerializeField] private LayeredBattlefieldLayoutController LayeredLayout;

        private readonly List<int> ActiveIndices = new();
        private readonly List<Wall> Walls = new();
        private readonly List<Defender> Guards = new();
        private bool IsInitialized;

        public IReadOnlyList<WallSpawnSlot> AllWallSlots => Slots;
        public IReadOnlyList<int> ActiveWallSlotIndices => ActiveIndices;
        public IReadOnlyList<Wall> ActiveWalls => Walls;
        public IReadOnlyList<Defender> ActiveGuards => Guards;
        public int StartSeed { get; private set; }

        public event Action<IReadOnlyList<int>> LayoutInitialized;
        public event Action<Wall> WallSpawned;
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
            if (IsInitialized)
            {
                return false;
            }

            if (!ValidateConfiguration())
            {
                return false;
            }

            IsInitialized = true;
            StartSeed = startSeed;
            int[] selected = LayeredLayout != null && LayeredLayout.IsApplied
                ? Slots.Select((slot, index) => new { slot, index })
                    .Where(value => value.slot.gameObject.activeSelf)
                    .Select(value => value.index)
                    .ToArray()
                : DeterministicSlotSelector.SelectWithoutReplacement(Slots.Length, 3, unchecked((uint)startSeed));
            if (selected.Length != 3)
            {
                Debug.LogError("Layered battlefield must select exactly three wall slots.", this);
                return false;
            }

            foreach (int slotIndex in selected)
            {
                WallSpawnSlot slot = Slots[slotIndex];
                Wall wall = Instantiate(WallPrefab, slot.transform.position, slot.transform.rotation, SpawnRoot);
                wall.name = $"Session Wall {slot.StableId}";
                DinoTargetMetadata wallMetadata = wall.GetComponent<DinoTargetMetadata>();
                if (wallMetadata == null)
                {
                    wallMetadata = wall.gameObject.AddComponent<DinoTargetMetadata>();
                }
                wallMetadata.Configure(DinoTargetCategory.Wall, 100 + slot.StableId);

                Defender guard = Instantiate(
                    ArcherPrefab,
                    slot.GuardAnchor.position,
                    slot.GuardAnchor.rotation,
                    SpawnRoot);
                guard.name = $"Fixed Archer {slot.StableId}";
                AttackRadius attackRadius = guard.GetComponentInChildren<AttackRadius>(true);
                if (attackRadius == null || !attackRadius.ConfigureRadius(WallGuardAttackRadius))
                {
                    Debug.LogError($"Fixed archer {slot.StableId} has no configurable spherical attack radius.", guard);
                }
                DinoTargetMetadata guardMetadata = guard.GetComponent<DinoTargetMetadata>();
                if (guardMetadata == null)
                {
                    guardMetadata = guard.gameObject.AddComponent<DinoTargetMetadata>();
                }
                guardMetadata.Configure(DinoTargetCategory.Defender, 200 + slot.StableId);

                WallGuardPost post = slot.GetComponent<WallGuardPost>();
                if (post == null)
                {
                    post = slot.gameObject.AddComponent<WallGuardPost>();
                }
                post.Bind(wall, guard, slot.GuardAnchor);

                ActiveIndices.Add(slotIndex);
                Walls.Add(wall);
                Guards.Add(guard);
                WallSpawned?.Invoke(wall);
                GuardSpawned?.Invoke(guard);
            }

            LayoutInitialized?.Invoke(ActiveIndices);
            return true;
        }

        private bool ValidateConfiguration()
        {
            if (Slots == null
                || Slots.Length != 6
                || WallPrefab == null
                || ArcherPrefab == null
                || SpawnRoot == null)
            {
                Debug.LogError("Session defense layout requires exactly six slots, one wall prefab, one archer prefab, and a spawn root.", this);
                return false;
            }

            if (Slots.Any(slot => slot == null || slot.GuardAnchor == null)
                || Slots.Select(slot => slot.StableId).Distinct().Count() != Slots.Length)
            {
                Debug.LogError("Session defense layout slots must be complete and have unique stable IDs.", this);
                return false;
            }

            return true;
        }
    }
}
