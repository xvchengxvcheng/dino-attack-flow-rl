using LlamAcademy.Dinos.Session;
using NUnit.Framework;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class GameResultPairingRulesTests
    {
        private const string LayoutA = "layered_battlefield|2718|layout-a";
        private const string LayoutB = "layered_battlefield|2718|layout-b";
        private static readonly string ModelHash = new('a', 64);

        [Test]
        public void HumanAndInferenceAi_WithSameSceneAndLayout_ArePaired()
        {
            GameResultSnapshot human = Snapshot(
                GameControllerMode.Human,
                LayoutA,
                modelId: string.Empty,
                modelHash: string.Empty,
                won: true,
                elapsedSeconds: 18.5f,
                finalMeat: 225,
                totalReward: 12.25f);
            GameResultSnapshot ai = Snapshot(
                GameControllerMode.InferenceAI,
                LayoutA,
                modelId: "default-v2",
                modelHash: ModelHash,
                won: false,
                elapsedSeconds: 40f,
                finalMeat: 0,
                totalReward: -0.93f);

            bool paired = GameResultPairingRules.TryPair(
                human,
                ai,
                out GameResultComparison comparison,
                out GameResultPairingFailure failure);

            Assert.That(paired, Is.True);
            Assert.That(failure, Is.EqualTo(GameResultPairingFailure.None));
            Assert.That(comparison.Human, Is.EqualTo(human));
            Assert.That(comparison.InferenceAi, Is.EqualTo(ai));
        }

        [Test]
        public void DifferentLayoutSignature_IsRejectedExplicitly()
        {
            GameResultSnapshot human = Snapshot(GameControllerMode.Human, LayoutA);
            GameResultSnapshot ai = Snapshot(
                GameControllerMode.InferenceAI,
                LayoutB,
                "default-v2",
                ModelHash);

            bool paired = GameResultPairingRules.TryPair(
                human,
                ai,
                out _,
                out GameResultPairingFailure failure);

            Assert.That(paired, Is.False);
            Assert.That(failure, Is.EqualTo(GameResultPairingFailure.LayoutSignatureMismatch));
        }

        [Test]
        public void NewMapNewSeedAndEveryNewHumanRun_ClearOldPlayerResult()
        {
            GameResultSnapshot human = Snapshot(GameControllerMode.Human, LayoutA);
            GameLaunchRequest sameLayoutAi = GameLaunchContext.DeriveInferenceReplay(
                GameLaunchRequest.CreateHuman(GameMapId.LayeredBattlefield, 2718),
                7,
                "default-v2",
                ModelHash,
                GameLaunchRequest.CurrentProtocolVersion);
            GameLaunchRequest newSeedAi = GameLaunchContext.DeriveInferenceReplay(
                GameLaunchRequest.CreateHuman(GameMapId.LayeredBattlefield, 3141),
                7,
                "default-v2",
                ModelHash,
                GameLaunchRequest.CurrentProtocolVersion);
            GameLaunchRequest newMapAi = GameLaunchContext.DeriveInferenceReplay(
                GameLaunchRequest.CreateHuman(GameMapId.OpenTropicalBattlefield, 2718),
                7,
                "default-v2",
                ModelHash,
                GameLaunchRequest.CurrentProtocolVersion);
            GameLaunchRequest newHuman = GameLaunchContext.DeriveRetry(
                GameLaunchRequest.CreateHuman(GameMapId.LayeredBattlefield, 2718));

            Assert.That(
                GameResultPairingRules.SelectPlayerResultForLaunch(human, sameLayoutAi),
                Is.EqualTo(human));
            Assert.That(GameResultPairingRules.SelectPlayerResultForLaunch(human, newSeedAi), Is.Null);
            Assert.That(GameResultPairingRules.SelectPlayerResultForLaunch(human, newMapAi), Is.Null);
            Assert.That(GameResultPairingRules.SelectPlayerResultForLaunch(human, newHuman), Is.Null);
        }

        private static GameResultSnapshot Snapshot(
            GameControllerMode controller,
            string layoutSignature,
            string modelId = "",
            string modelHash = "",
            bool won = true,
            float elapsedSeconds = 12f,
            int finalMeat = 150,
            float totalReward = 11.5f) =>
            new(
                GameMapId.LayeredBattlefield,
                GameMapId.LayeredBattlefield.SceneStableId,
                2718,
                controller,
                won,
                elapsedSeconds,
                finalMeat,
                totalReward,
                layoutSignature,
                modelId,
                modelHash,
                GameLaunchRequest.CurrentProtocolVersion);
    }
}
