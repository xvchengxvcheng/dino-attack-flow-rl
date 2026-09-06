using System;
using System.Collections.Generic;
using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public static class GroundGuardSpawnPlanner
    {
        public static bool TrySelect(
            Rect bounds,
            int count,
            float minimumSpacing,
            uint seed,
            int maximumAttempts,
            Func<Vector2, bool> isAllowed,
            out Vector2[] points)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (minimumSpacing < 0f || !float.IsFinite(minimumSpacing))
            {
                throw new ArgumentOutOfRangeException(nameof(minimumSpacing));
            }
            if (maximumAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
            }
            if (isAllowed == null)
            {
                throw new ArgumentNullException(nameof(isAllowed));
            }
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(bounds));
            }

            List<Vector2> selected = new(count);
            float minimumSpacingSquared = minimumSpacing * minimumSpacing;
            uint state = seed == 0u ? 0x9E3779B9u : seed;

            for (int attempt = 0; attempt < maximumAttempts && selected.Count < count; attempt++)
            {
                Vector2 candidate = new(
                    Mathf.Lerp(bounds.xMin, bounds.xMax, Next01(ref state)),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, Next01(ref state)));
                if (!isAllowed(candidate))
                {
                    continue;
                }

                bool overlaps = false;
                for (int index = 0; index < selected.Count; index++)
                {
                    if ((selected[index] - candidate).sqrMagnitude < minimumSpacingSquared)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                {
                    selected.Add(candidate);
                }
            }

            if (selected.Count != count)
            {
                points = Array.Empty<Vector2>();
                return false;
            }

            points = selected.ToArray();
            return true;
        }

        private static float Next01(ref uint state)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return (state >> 8) * (1f / 16777216f);
        }
    }
}
