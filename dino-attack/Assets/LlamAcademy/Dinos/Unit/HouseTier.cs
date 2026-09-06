using System;

namespace LlamAcademy.Dinos.Unit
{
    public enum HouseTier
    {
        Tier1 = 1,
        Tier2 = 2,
        Tier3 = 3,
        Tier4 = 4
    }

    public static class HouseTierProfile
    {
        public static int GetMaxHealth(HouseTier tier)
        {
            return tier switch
            {
                HouseTier.Tier1 => 100,
                HouseTier.Tier2 => 150,
                HouseTier.Tier3 => 225,
                HouseTier.Tier4 => 325,
                _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
            };
        }
    }
}
