namespace LlamAcademy.Dinos.DefenseBattle
{
    public static class DefenseBattleOutcomeRules
    {
        public static bool DidDefenderWin(bool attackersWon) => !attackersWon;
        public static bool AttackersWinWhenDefenderForfeits => true;
    }
}
