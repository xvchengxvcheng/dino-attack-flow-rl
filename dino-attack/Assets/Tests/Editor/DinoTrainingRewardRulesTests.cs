using LlamAcademy.Dinos.Training;
using NUnit.Framework;

namespace DinoAttack.Training.Core.Tests
{
    public class DinoTrainingRewardRulesTests
    {
        [TestCase(0, 10f)]
        [TestCase(100, 11f)]
        [TestCase(375, 13.75f)]
        public void VictoryPaysBaseRewardPlusUnclippedFinalMeat(int finalMeat, float expected)
        {
            DinoTrainingRewardLedger ledger = new();

            Assert.That(ledger.ConcludeNatural(playerWon: true, finalMeat), Is.EqualTo(expected).Within(0.0001f));
            Assert.That(ledger.ConcludeNatural(playerWon: true, finalMeat + 100), Is.Zero);
            Assert.That(ledger.TotalReward, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void DefeatPaysMinusOneAndNeverPaysRemainingMeat()
        {
            DinoTrainingRewardLedger ledger = new();

            Assert.That(ledger.ConcludeNatural(playerWon: false, finalMeat: 10_000), Is.EqualTo(-1f));
            Assert.That(ledger.TotalReward, Is.EqualTo(-1f));
        }

        [Test]
        public void InfrastructureInterruptionConcludesWithoutTerminalReward()
        {
            DinoTrainingRewardLedger ledger = new();

            Assert.That(ledger.ConcludeInterrupted(), Is.Zero);
            Assert.That(ledger.ConcludeNatural(playerWon: true, finalMeat: 100), Is.Zero);
            Assert.That(ledger.TotalReward, Is.Zero);
        }

        [Test]
        public void DefenderAndWallRewardsAreDeduplicatedByStableIdentity()
        {
            DinoTrainingRewardLedger ledger = new();

            Assert.That(ledger.RecordDefenderDeath(stableIdentity: 17), Is.EqualTo(0.02f).Within(0.0001f));
            Assert.That(ledger.RecordDefenderDeath(stableIdentity: 17), Is.Zero);
            Assert.That(ledger.RecordDefenderDeath(stableIdentity: 18), Is.EqualTo(0.02f).Within(0.0001f));
            Assert.That(ledger.RecordWallDestroyed(stableIdentity: 31), Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(ledger.RecordWallDestroyed(stableIdentity: 31), Is.Zero);
            Assert.That(ledger.RecordWallDestroyed(stableIdentity: 32), Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(ledger.TotalReward, Is.EqualTo(0.14f).Within(0.0001f));
        }

        [Test]
        public void HouseRewardsAreDeduplicatedByStableIdentity()
        {
            DinoTrainingRewardLedger ledger = new();

            Assert.That(ledger.RecordHouseDestroyed(stableIdentity: 41), Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(ledger.RecordHouseDestroyed(stableIdentity: 41), Is.Zero);
            Assert.That(ledger.RecordHouseDestroyed(stableIdentity: 42), Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(ledger.TotalReward, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void DeploymentRewardsOnlyPenalizeRejectedNonWaitActions()
        {
            DinoTrainingRewardLedger ledger = new();

            Assert.That(ledger.RecordWait(), Is.Zero);
            Assert.That(ledger.RecordAcceptedDeployment(), Is.Zero);
            Assert.That(ledger.RecordRejectedDeployment(), Is.EqualTo(-0.02f).Within(0.0001f));
            Assert.That(ledger.RecordRejectedDeployment(), Is.EqualTo(-0.02f).Within(0.0001f));
            Assert.That(ledger.TotalReward, Is.EqualTo(-0.04f).Within(0.0001f));
        }

        [Test]
        public void ResetStartsAFreshEpisodeLedger()
        {
            DinoTrainingRewardLedger ledger = new();
            ledger.RecordDefenderDeath(1);
            ledger.ConcludeNatural(playerWon: false, finalMeat: 0);

            ledger.Reset();

            Assert.That(ledger.IsConcluded, Is.False);
            Assert.That(ledger.TotalReward, Is.Zero);
            Assert.That(ledger.RecordDefenderDeath(1), Is.EqualTo(0.02f).Within(0.0001f));
        }

        [Test]
        public void ResetPreservesTheLastNaturalEpisodeSummaryForResultCollection()
        {
            DinoTrainingRewardLedger ledger = new();
            ledger.RecordWallDestroyed(1);
            ledger.ConcludeNatural(playerWon: true, finalMeat: 100);

            ledger.Reset();

            Assert.That(ledger.LastConcludedReward, Is.EqualTo(11.05f).Within(0.0001f));
            Assert.That(ledger.TotalReward, Is.Zero);
        }
    }
}
