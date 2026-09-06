using UnityEngine;

namespace LlamAcademy.Dinos.Training
{
    public static class DinoTrainingRunContext
    {
        private const int DefaultBaseSeed = 20260829;
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        private static bool IsConfigured;

        public static int BaseSeed { get; private set; }
        public static int EnvironmentId { get; private set; }
        public static int EpisodeIndex { get; private set; }
        public static int EpisodeSeed { get; private set; }
        public static int ProcessGeneration { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForProcess()
        {
            IsConfigured = false;
            BaseSeed = DefaultBaseSeed;
            EnvironmentId = 0;
            EpisodeIndex = 0;
            ProcessGeneration = 0;
            EpisodeSeed = DeriveEpisodeSeed(BaseSeed, EnvironmentId, EpisodeIndex);
        }

        public static void Configure(int baseSeed, int environmentIndex, int processGeneration)
        {
            if (IsConfigured && BaseSeed == baseSeed && EnvironmentId == environmentIndex &&
                ProcessGeneration == processGeneration)
            {
                return;
            }

            IsConfigured = true;
            BaseSeed = baseSeed;
            EnvironmentId = environmentIndex;
            EpisodeIndex = 0;
            ProcessGeneration = processGeneration;
            EpisodeSeed = DeriveEpisodeSeed(BaseSeed, EnvironmentId, EpisodeIndex);
        }

        public static int AdvanceEpisode()
        {
            EpisodeIndex = checked(EpisodeIndex + 1);
            EpisodeSeed = DeriveEpisodeSeed(BaseSeed, EnvironmentId, EpisodeIndex);
            return EpisodeSeed;
        }

        public static int DeriveEpisodeSeed(int baseSeed, int environmentIndex, int episodeIndex)
        {
            unchecked
            {
                uint hash = FnvOffsetBasis;
                hash = (hash ^ (uint)baseSeed) * FnvPrime;
                hash = (hash ^ (uint)environmentIndex) * FnvPrime;
                hash = (hash ^ (uint)episodeIndex) * FnvPrime;
                return (int)hash;
            }
        }
    }
}
