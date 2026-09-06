using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace LlamAcademy.Dinos.Map
{
    [DefaultExecutionOrder(-100)]
    public sealed class VillageDefenseLayoutController : MonoBehaviour
    {
        [SerializeField] private int mapSeed = 20260828;
        [SerializeField] private GameObject[] presetRoots = Array.Empty<GameObject>();
        [SerializeField] private MonoBehaviour[] runtimeSurfaces = Array.Empty<MonoBehaviour>();

        private readonly List<Transform> ValidatedSlots = new();
        private readonly Dictionary<Component, Delegate> WallDeathSubscriptions = new();
        private int ActiveRound = int.MinValue;
        private Coroutine PendingRuntimeRebuild;

        public int MapSeed => mapSeed;
        public int ActivePresetIndex { get; private set; } = -1;
        public GameObject ActivePresetRoot { get; private set; }
        public string ActivePresetId => ActivePresetRoot == null ? string.Empty : ActivePresetRoot.name;
        public IReadOnlyList<Transform> ValidatedSpawnSlots => ValidatedSlots;
        public int RuntimeRebuildCount { get; private set; }
        public double LastRuntimeRebuildMilliseconds { get; private set; }

        public event Action<int, GameObject> LayoutActivated;

        private void Awake()
        {
            ActivateForRound(mapSeed, 0);
        }

        private void OnEnable()
        {
            if (ActivePresetRoot != null)
            {
                RefreshWallDeathSubscriptions();
                RebuildValidatedSlots();
            }
        }

        private void OnDisable()
        {
            UnsubscribeWallDeaths();
            if (PendingRuntimeRebuild != null)
            {
                StopCoroutine(PendingRuntimeRebuild);
                PendingRuntimeRebuild = null;
            }
        }

        public bool ActivateForRound(int seed, int round)
        {
            if (presetRoots == null || presetRoots.Length < 2)
            {
                Debug.LogError("Village defense layout requires at least two preset roots.", this);
                return false;
            }

            int index = DefensePresetSelector.SelectIndex(seed, round, presetRoots.Length);
            if (ActivePresetIndex == index && ActiveRound == round && mapSeed == seed &&
                ActivePresetRoot != null && ActivePresetRoot.activeSelf)
            {
                DisableLegacyCarvingObstacles();
                RefreshWallDeathSubscriptions();
                RebuildValidatedSlots();
                return ValidatedSlots.Count > 0;
            }

            bool hadPreviousLayout = ActiveRound != int.MinValue;
            mapSeed = seed;
            ActiveRound = round;
            ActivePresetIndex = index;
            ActivePresetRoot = null;
            for (int i = 0; i < presetRoots.Length; i++)
            {
                GameObject preset = presetRoots[i];
                if (preset == null)
                {
                    Debug.LogError($"Defense preset {i} is missing and was skipped.", this);
                    continue;
                }

                bool selected = i == index;
                preset.SetActive(selected);
                if (selected)
                {
                    ActivePresetRoot = preset;
                }
            }

            DisableLegacyCarvingObstacles();
            RefreshWallDeathSubscriptions();
            RebuildValidatedSlots();
            LayoutActivated?.Invoke(ActivePresetIndex, ActivePresetRoot);
            if (hadPreviousLayout)
            {
                RequestRuntimeRebuild(null);
            }
            return ActivePresetRoot != null && ValidatedSlots.Count > 0;
        }

        private void DisableLegacyCarvingObstacles()
        {
            for (int i = 0; i < presetRoots.Length; i++)
            {
                GameObject preset = presetRoots[i];
                if (preset == null)
                {
                    continue;
                }

                foreach (NavMeshObstacle obstacle in preset.GetComponentsInChildren<NavMeshObstacle>(true))
                {
                    obstacle.enabled = false;
                }
            }
        }

        private void RefreshWallDeathSubscriptions()
        {
            UnsubscribeWallDeaths();
            if (ActivePresetRoot == null) return;
            MethodInfo handler = GetType().GetMethod(nameof(HandlePresetWallDeath), BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (Component component in ActivePresetRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != "LlamAcademy.Dinos.Unit.Wall")
                {
                    continue;
                }

                EventInfo deathEvent = component.GetType().GetEvent("OnDeath", BindingFlags.Instance | BindingFlags.Public);
                Delegate callback = deathEvent == null ? null : Delegate.CreateDelegate(deathEvent.EventHandlerType, this, handler, false);
                if (callback == null) continue;
                deathEvent.AddEventHandler(component, callback);
                WallDeathSubscriptions.Add(component, callback);
            }
        }

        public void NotifyActiveWallRestored(Component wall)
        {
            if (wall != null && ActivePresetRoot != null && wall.transform.IsChildOf(ActivePresetRoot.transform))
            {
                RefreshWallDeathSubscriptions();
                RequestRuntimeRebuild(null);
            }
        }

        public void NotifyActiveWallReplaced(Component wall)
        {
            NotifyActiveWallRestored(wall);
        }

        private void HandlePresetWallDeath(object diedWall)
        {
            RequestRuntimeRebuild(diedWall as Component);
        }

        private void RequestRuntimeRebuild(Component wallThatMustBecomeInactive)
        {
            if (!Application.isPlaying || !HasLiveNavMeshManager() || runtimeSurfaces == null || runtimeSurfaces.Length == 0)
            {
                return;
            }

            if (PendingRuntimeRebuild != null)
            {
                StopCoroutine(PendingRuntimeRebuild);
            }

            PendingRuntimeRebuild = StartCoroutine(RebuildRuntimeSurfaces(wallThatMustBecomeInactive));
        }

        private IEnumerator RebuildRuntimeSurfaces(Component wallThatMustBecomeInactive)
        {
            yield return null;
            for (int frame = 0; wallThatMustBecomeInactive != null &&
                                wallThatMustBecomeInactive.gameObject.activeInHierarchy && frame < 4; frame++)
            {
                yield return null;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < runtimeSurfaces.Length; i++)
            {
                MonoBehaviour surface = runtimeSurfaces[i];
                MethodInfo build = surface?.GetType().GetMethod("BuildNavMesh", BindingFlags.Instance | BindingFlags.Public);
                if (build == null)
                {
                    Debug.LogError($"Runtime NavMesh surface {i} is missing BuildNavMesh().", surface);
                    continue;
                }

                build.Invoke(surface, null);
            }
            RuntimeRebuildCount++;
            RestoreRuntimeSurfaceRegistrations();
            stopwatch.Stop();
            LastRuntimeRebuildMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            PendingRuntimeRebuild = null;
        }

        private static bool HasLiveNavMeshManager()
        {
            Component manager = FindComponent("LlamAcademy.Dinos.RoundManagement.NavMeshManager");
            return manager != null && manager.gameObject.activeInHierarchy;
        }

        private static void RestoreRuntimeSurfaceRegistrations()
        {
            Component manager = FindComponent("LlamAcademy.Dinos.RoundManagement.NavMeshManager");
            MethodInfo restore = manager?.GetType().GetMethod("RecalculateTriangulation",
                BindingFlags.Instance | BindingFlags.Public);
            if (restore == null)
            {
                Debug.LogError("Runtime NavMesh rebuild could not restore registrations through NavMeshManager.", manager);
                return;
            }

            restore.Invoke(manager, new object[] { true });
        }

        private static Component FindComponent(string fullName)
        {
            foreach (Component component in UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (component != null && component.GetType().FullName == fullName)
                {
                    return component;
                }
            }

            return null;
        }

        private void UnsubscribeWallDeaths()
        {
            foreach (KeyValuePair<Component, Delegate> subscription in WallDeathSubscriptions)
            {
                Component wall = subscription.Key;
                if (wall == null)
                {
                    continue;
                }

                wall.GetType().GetEvent("OnDeath", BindingFlags.Instance | BindingFlags.Public)
                    ?.RemoveEventHandler(wall, subscription.Value);
            }
            WallDeathSubscriptions.Clear();
        }

        private void OnDestroy()
        {
            OnDisable();
        }

        public bool TryGetSpawnPose(int stableSalt, out Vector3 position, out Quaternion rotation)
        {
            if (ValidatedSlots.Count == 0)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }

            System.Random random = new(DeriveSeed(mapSeed, ActiveRound, stableSalt));
            Transform slot = ValidatedSlots[random.Next(ValidatedSlots.Count)];
            position = slot.position;
            rotation = Quaternion.Euler(0f, (float)(random.NextDouble() * 360d), 0f);
            return true;
        }

        private void RebuildValidatedSlots()
        {
            ValidatedSlots.Clear();
            if (ActivePresetRoot == null)
            {
                return;
            }

            Transform[] candidates = ActivePresetRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform candidate = candidates[i];
                if (!candidate.name.StartsWith("Spawn Slot ", StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 p = candidate.position;
                if (!candidate.gameObject.activeInHierarchy || !IsFinite(p))
                {
                    Debug.LogError($"Invalid defense spawn slot '{candidate.name}' was skipped.", candidate);
                    continue;
                }

                ValidatedSlots.Add(candidate);
            }

            if (ValidatedSlots.Count == 0)
            {
                Debug.LogError($"Defense preset '{ActivePresetId}' has no valid spawn slots.", this);
            }
        }

        private static int DeriveSeed(int seed, int round, int salt)
        {
            unchecked
            {
                uint hash = (uint)seed;
                hash = (hash * 16777619u) ^ (uint)round;
                hash = (hash * 16777619u) ^ (uint)salt;
                hash ^= hash >> 16;
                return (int)hash;
            }
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
