using System;
using System.Collections.Generic;

namespace LlamAcademy.Dinos.Map
{
    public sealed class TerrainSurfaceStack
    {
        private readonly Dictionary<TerrainSurfaceKind, int> surfaceCounts = new();

        public TerrainSurfaceKind Current { get; private set; } = TerrainSurfaceKind.Grass;

        public void Enter(TerrainSurfaceKind surface)
        {
            surfaceCounts.TryGetValue(surface, out int count);
            surfaceCounts[surface] = count + 1;
            RecomputeCurrent();
        }

        public void Exit(TerrainSurfaceKind surface)
        {
            if (!surfaceCounts.TryGetValue(surface, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                surfaceCounts.Remove(surface);
            }
            else
            {
                surfaceCounts[surface] = count - 1;
            }

            RecomputeCurrent();
        }

        public void Clear()
        {
            surfaceCounts.Clear();
            Current = TerrainSurfaceKind.Grass;
        }

        private void RecomputeCurrent()
        {
            TerrainSurfaceKind current = TerrainSurfaceKind.Grass;
            int currentPriority = TerrainSpeedProfile.GetPriority(current);

            foreach (KeyValuePair<TerrainSurfaceKind, int> entry in surfaceCounts)
            {
                int priority = TerrainSpeedProfile.GetPriority(entry.Key);
                if (entry.Value > 0 && priority > currentPriority)
                {
                    current = entry.Key;
                    currentPriority = priority;
                }
            }

            Current = current;
        }
    }
}
