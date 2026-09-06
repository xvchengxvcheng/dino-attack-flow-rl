using System.Collections.Generic;

namespace LlamAcademy.Dinos.RoundManagement
{
    public sealed class HouseRewardLedger
    {
        public const int MeatPerHouse = 100;

        private readonly HashSet<int> RewardedHouseIds = new();

        public int RewardedHouseCount => RewardedHouseIds.Count;
        public int TotalMeatAwarded => RewardedHouseCount * MeatPerHouse;

        public bool TryRecord(int stableHouseId)
        {
            return stableHouseId >= 0 && RewardedHouseIds.Add(stableHouseId);
        }
    }
}
