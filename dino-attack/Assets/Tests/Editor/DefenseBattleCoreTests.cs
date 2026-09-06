using System;
using System.Collections.Generic;
using LlamAcademy.Dinos.DefenseBattle;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DefenseBattleCoreTests
    {
        [Test]
        public void DefenseMapId_IsStableAndResolvable()
        {
            Assert.That(GameMapId.DefenseBattlefield.StableId, Is.EqualTo("defense_battlefield"));
            Assert.That(GameMapId.DefenseBattlefield.SceneName, Is.EqualTo("DefenseBattlefield"));
            Assert.That(GameMapId.DefenseBattlefield.DisplayName, Is.EqualTo("防御战场"));
            Assert.That(GameMapId.TryFromSceneName("DefenseBattlefield", out GameMapId resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(GameMapId.DefenseBattlefield));
        }

        [Test]
        public void Inventory_RequiresExactThreeFiveThreeAndReturnsSafely()
        {
            DefenseInventoryState state = DefenseInventoryState.Full;
            for (int i = 0; i < 3; i++) Assert.That(state.TryPlace(DefensePlacementKind.Wall, out state), Is.True);
            for (int i = 0; i < 5; i++) Assert.That(state.TryPlace(DefensePlacementKind.Archer, out state), Is.True);
            for (int i = 0; i < 3; i++) Assert.That(state.TryPlace(DefensePlacementKind.Mage, out state), Is.True);
            Assert.That(state.IsComplete, Is.True);
            Assert.That(state.TryPlace(DefensePlacementKind.Wall, out _), Is.False);
            Assert.That(state.TryReturn(DefensePlacementKind.Wall, out state), Is.True);
            Assert.That(state.Walls, Is.EqualTo(1));
        }

        [Test]
        public void MissingMessage_IncludesUnitsAndExplicitStrategy()
        {
            Assert.That(DefenseInventoryState.Full.BuildMissingMessage(false),
                Is.EqualTo("还需放置：城墙 3、弓箭手 5、法师 3；请选择 AI Strategy"));
            Assert.That(new DefenseInventoryState(0, 0, 0).BuildMissingMessage(false),
                Is.EqualTo("还需放置：请选择 AI Strategy"));
            Assert.That(new DefenseInventoryState(0, 0, 0).BuildMissingMessage(true), Is.Empty);
        }

        [Test]
        public void Snapshot_IsStableAndRejectsMissingOrDuplicateTokens()
        {
            List<DefensePlacementToken> placements = BuildCompleteLayout();
            DefenseLayoutSnapshot first = new(GameMapId.DefenseBattlefield, 17, "v1", placements);
            placements.Reverse();
            DefenseLayoutSnapshot second = new(GameMapId.DefenseBattlefield, 17, "v1", placements);
            Assert.That(second.CanonicalText, Is.EqualTo(first.CanonicalText));
            Assert.That(second.Sha256, Is.EqualTo(first.Sha256));
            Assert.That(first.Sha256.Length, Is.EqualTo(64));

            placements.RemoveAt(0);
            Assert.Throws<ArgumentException>(() =>
                new DefenseLayoutSnapshot(GameMapId.DefenseBattlefield, 17, "v1", placements));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Outcome_IsPresentedFromDefenderPerspective(bool attackersWon, bool defenderWon)
        {
            Assert.That(DefenseBattleOutcomeRules.DidDefenderWin(attackersWon), Is.EqualTo(defenderWon));
            Assert.That(DefenseBattleOutcomeRules.AttackersWinWhenDefenderForfeits, Is.True);
        }

        private static List<DefensePlacementToken> BuildCompleteLayout()
        {
            List<DefensePlacementToken> result = new();
            for (int i = 0; i < 3; i++) result.Add(new DefensePlacementToken(DefensePlacementKind.Wall, i, i, 0, i + 1, 0));
            for (int i = 0; i < 5; i++) result.Add(new DefensePlacementToken(DefensePlacementKind.Archer, i, i + 5, 0, i + 6, 0));
            for (int i = 0; i < 3; i++) result.Add(new DefensePlacementToken(DefensePlacementKind.Mage, i, i + 11, 0, i + 12, 0));
            return result;
        }
    }
}
