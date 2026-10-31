namespace LlamAcademy.Dinos.Companion
{
    /// <summary>Authored character identity, separate from mutable relationship save data.</summary>
    public static class CompanionCharacterInfo
    {
        public const string DisplayName = "溯光";
        public const string Introduction = "“星空”是她的来处，辽阔无垠，不分贵贱；“地外星系”是她所在的远方，于这个世界之外，以另一种目光理解生命；“望向内心”是她认识一个人的方式，看见宇宙之中，那片不为人知的宇宙；“夜晚”是她到来的时刻，唯有星空可见的夜里，她才会循着微光而至。";
        public const string AttackSkill = "法杖施法 · 自动攻击";
        public const string AttackDetails = "基础伤害18 · 射程8米\n攻击间隔0.85秒";
        public const string GuardSkill = "协同守护";
        public const string GuardDetails = "跟随玩家、守住指定位置\n集合返回玩家身边";
        public const string DefenseSkill = "自主防卫";
        public const string DefenseDetails = "获得授权后巡逻村庄\n结合关系与战况执行指令";
        public const string SkillIntroduction = AttackSkill + "\n" + AttackDetails + "\n\n" +
            GuardSkill + "\n" + GuardDetails + "\n\n" + DefenseSkill + "\n" + DefenseDetails;
    }
}
