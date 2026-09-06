namespace LlamAcademy.Dinos.DefenseBattle
{
    public static class DefenseBattleReplayContext
    {
        private static DefenseLayoutSnapshot PendingSnapshot;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Clear();

        public static void PublishRetry(DefenseLayoutSnapshot snapshot)
        {
            PendingSnapshot = snapshot;
        }

        public static bool TryConsume(int layoutSeed, out DefenseLayoutSnapshot snapshot)
        {
            snapshot = PendingSnapshot;
            PendingSnapshot = null;
            return snapshot != null && snapshot.LayoutSeed == layoutSeed;
        }

        public static void Clear() => PendingSnapshot = null;
    }
}
