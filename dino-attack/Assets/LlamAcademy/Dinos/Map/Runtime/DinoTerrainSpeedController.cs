using LlamAcademy.Dinos.Map;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Map
{
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class DinoTerrainSpeedController : MonoBehaviour
    {
        private readonly TerrainSurfaceStack surfaceStack = new();
        private readonly Dictionary<Collider, ActiveSurfaceVolume> activeVolumes = new();

        private NavMeshAgent agent;
        private float baseSpeed;
        private bool hasValidBaseSpeed;

        public TerrainSurfaceKind CurrentSurface => surfaceStack.Current;
        public float CurrentMultiplier => TerrainSpeedProfile.GetMultiplier(CurrentSurface);

        private void Awake()
        {
            CaptureBaseSpeedOnce();
        }

        private void OnEnable()
        {
            CaptureBaseSpeedOnce();
            RestoreBaseSpeed();
        }

        private void OnDisable()
        {
            ResetSurfaceState();
        }

        private void OnDestroy()
        {
            ResetSurfaceState();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.TryGetComponent(out TerrainSurfaceVolume volume))
            {
                return;
            }

            if (activeVolumes.TryGetValue(other, out ActiveSurfaceVolume activeVolume))
            {
                activeVolume.EnterCount++;
            }
            else
            {
                activeVolumes.Add(other, new ActiveSurfaceVolume(volume.Surface));
                surfaceStack.Enter(volume.Surface);
            }

            ApplyCurrentMultiplier();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.TryGetComponent(out TerrainSurfaceVolume _)
                || !activeVolumes.TryGetValue(other, out ActiveSurfaceVolume activeVolume))
            {
                return;
            }

            activeVolume.EnterCount--;
            if (activeVolume.EnterCount == 0)
            {
                activeVolumes.Remove(other);
                surfaceStack.Exit(activeVolume.Surface);
            }

            ApplyCurrentMultiplier();
        }

        private void CaptureBaseSpeedOnce()
        {
            if (hasValidBaseSpeed)
            {
                return;
            }

            agent ??= GetComponent<NavMeshAgent>();
            if (agent == null || !IsFinitePositive(agent.speed))
            {
                return;
            }

            baseSpeed = agent.speed;
            hasValidBaseSpeed = true;
        }

        private void ApplyCurrentMultiplier()
        {
            if (!hasValidBaseSpeed || agent == null)
            {
                return;
            }

            float candidateSpeed = baseSpeed * CurrentMultiplier;
            if (IsFinitePositive(candidateSpeed))
            {
                agent.speed = candidateSpeed;
            }
        }

        private void ResetSurfaceState()
        {
            activeVolumes.Clear();
            surfaceStack.Clear();
            RestoreBaseSpeed();
        }

        private void RestoreBaseSpeed()
        {
            if (hasValidBaseSpeed && agent != null && IsFinitePositive(baseSpeed))
            {
                agent.speed = baseSpeed;
            }
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class ActiveSurfaceVolume
        {
            public ActiveSurfaceVolume(TerrainSurfaceKind surface)
            {
                Surface = surface;
                EnterCount = 1;
            }

            public TerrainSurfaceKind Surface { get; }
            public int EnterCount { get; set; }
        }
    }
}
