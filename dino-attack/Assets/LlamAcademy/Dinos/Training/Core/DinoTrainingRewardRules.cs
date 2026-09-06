using System.Collections.Generic;

namespace LlamAcademy.Dinos.Training
{
    public static class DinoTrainingRewardRules
    {
        public const float VictoryReward = 10f;
        public const float FinalMeatRewardScale = 0.01f;
        public const float DefeatReward = -1f;
        public const float DefenderKillReward = 0.02f;
        public const float WallDestroyedReward = 0.05f;
        public const float HouseDestroyedReward = 0.1f;
        public const float InvalidDeploymentReward = -0.02f;

        public static float NaturalTerminalReward(bool playerWon, int finalMeat)
        {
            return playerWon
                ? VictoryReward + FinalMeatRewardScale * finalMeat
                : DefeatReward;
        }
    }

    public sealed class DinoTrainingRewardLedger
    {
        private readonly HashSet<int> RewardedDefenders = new();
        private readonly HashSet<int> RewardedWalls = new();
        private readonly HashSet<int> RewardedHouses = new();

        public bool IsConcluded { get; private set; }
        public float TotalReward { get; private set; }
        public float LastConcludedReward { get; private set; }

        public void Reset()
        {
            RewardedDefenders.Clear();
            RewardedWalls.Clear();
            RewardedHouses.Clear();
            IsConcluded = false;
            TotalReward = 0f;
        }

        public float RecordDefenderDeath(int stableIdentity)
        {
            return RecordUniqueEntityReward(
                RewardedDefenders,
                stableIdentity,
                DinoTrainingRewardRules.DefenderKillReward);
        }

        public float RecordWallDestroyed(int stableIdentity)
        {
            return RecordUniqueEntityReward(
                RewardedWalls,
                stableIdentity,
                DinoTrainingRewardRules.WallDestroyedReward);
        }

        public float RecordHouseDestroyed(int stableIdentity)
        {
            return RecordUniqueEntityReward(
                RewardedHouses,
                stableIdentity,
                DinoTrainingRewardRules.HouseDestroyedReward);
        }

        public float RecordWait()
        {
            return 0f;
        }

        public float RecordAcceptedDeployment()
        {
            return 0f;
        }

        public float RecordRejectedDeployment()
        {
            return Add(DinoTrainingRewardRules.InvalidDeploymentReward);
        }

        public float ConcludeNatural(bool playerWon, int finalMeat)
        {
            if (IsConcluded)
            {
                return 0f;
            }

            float reward = DinoTrainingRewardRules.NaturalTerminalReward(playerWon, finalMeat);
            IsConcluded = true;
            TotalReward += reward;
            LastConcludedReward = TotalReward;
            return reward;
        }

        public float ConcludeInterrupted()
        {
            if (IsConcluded)
            {
                return 0f;
            }

            IsConcluded = true;
            LastConcludedReward = 0f;
            return 0f;
        }

        private float RecordUniqueEntityReward(HashSet<int> rewardedEntities, int stableIdentity, float reward)
        {
            if (IsConcluded || !rewardedEntities.Add(stableIdentity))
            {
                return 0f;
            }

            return Add(reward);
        }

        private float Add(float reward)
        {
            if (IsConcluded)
            {
                return 0f;
            }

            TotalReward += reward;
            return reward;
        }
    }
}
