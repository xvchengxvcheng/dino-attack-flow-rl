using System;
using System.Collections.Generic;

namespace LlamAcademy.Dinos.Map
{
    public readonly struct DinoTargetSelectionCandidate
    {
        public DinoTargetSelectionCandidate(
            DinoTargetCategory category,
            int stableId,
            float pathLength,
            bool isReachable)
        {
            Category = category;
            StableId = stableId;
            PathLength = pathLength;
            IsReachable = isReachable;
        }

        public DinoTargetCategory Category { get; }
        public int StableId { get; }
        public float PathLength { get; }
        public bool IsReachable { get; }
    }

    public static class DinoTargetSelectionRules
    {
        public const float PathTieTolerance = 0.01f;

        public static int SelectIndex(
            DinoTargetProfileId profileId,
            IReadOnlyList<DinoTargetSelectionCandidate> candidates)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            IReadOnlyList<DinoTargetCategory> categories =
                DinoTargetPriorityProfile.GetCategories(profileId);

            foreach (DinoTargetCategory category in categories)
            {
                float shortestLength = float.PositiveInfinity;

                for (int index = 0; index < candidates.Count; index++)
                {
                    DinoTargetSelectionCandidate candidate = candidates[index];
                    if (candidate.Category != category
                        || !candidate.IsReachable
                        || !IsFinite(candidate.PathLength)
                        || candidate.PathLength < 0f)
                    {
                        continue;
                    }

                    if (candidate.PathLength < shortestLength)
                    {
                        shortestLength = candidate.PathLength;
                    }
                }

                if (!IsFinite(shortestLength))
                {
                    continue;
                }

                int bestIndex = -1;
                int bestStableId = int.MaxValue;
                float tieLimit = shortestLength + PathTieTolerance;
                for (int index = 0; index < candidates.Count; index++)
                {
                    DinoTargetSelectionCandidate candidate = candidates[index];
                    if (candidate.Category == category
                        && candidate.IsReachable
                        && IsFinite(candidate.PathLength)
                        && candidate.PathLength >= 0f
                        && candidate.PathLength <= tieLimit
                        && candidate.StableId < bestStableId)
                    {
                        bestIndex = index;
                        bestStableId = candidate.StableId;
                    }
                }

                if (bestIndex != -1)
                {
                    return bestIndex;
                }
            }

            return -1;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
