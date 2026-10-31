using System;

namespace LlamAcademy.Dinos.Companion
{
    public static class CompanionOrderEvaluator
    {
        public const string RoundNotRunning = "RoundNotRunning";
        public const string ClarificationRequired = "ClarificationRequired";
        public const string LowConfidence = "LowConfidence";
        public const string UnsafeDestination = "UnsafeDestination";
        public const string SelfHealthDanger = "SelfHealthDanger";
        public const string PlayerUnderThreat = "PlayerUnderThreat";
        public const string LowFamiliarity = "LowFamiliarity";
        public const string LowTrust = "LowTrust";
        public const string AutonomyGranted = "AutonomyGranted";
        public const string CommandAccepted = "CommandAccepted";
        public const string CommandPartiallyAccepted = "CommandPartiallyAccepted";

        public static CompanionOrder Evaluate(
            CompanionProfileData profile,
            CompanionIntent intent,
            CompanionSituation situation)
        {
            profile = (profile ?? CompanionProfileData.CreateDefault()).Clone().Sanitize();
            intent = (intent ?? new CompanionIntent { NeedsClarification = true }).Clone().Sanitize();
            situation = (situation ?? new CompanionSituation()).Sanitize();

            float affinity = profile.Affinity / 100f;
            float familiarity = profile.Familiarity / 100f;
            float trust = profile.Trust / 100f;
            float respect = profile.Respect / 100f;
            float stress = profile.Stress / 100f;
            float relationship = 0.35f * affinity + 0.25f * familiarity + 0.25f * trust + 0.15f * respect;
            float autonomy = 0.45f * relationship + 0.25f * profile.NeedForAutonomy +
                             0.15f * profile.Intuition + 0.15f * (1f - stress);
            float safety = Math.Max(0f, Math.Min(1f, situation.SelfHealthRatio * (1f - 0.65f * situation.VillageThreat)));
            float score = 0.50f * intent.Confidence + 0.20f * autonomy +
                          0.15f * intent.TacticalUrgency + 0.15f * safety;

            CompanionOrder order = NewOrder(intent, score, autonomy);
            if (!situation.Running)
                return Finish(order, OrderAcceptance.Deferred, RoundNotRunning,
                    "当前不在战斗阶段，命令将在下一场战斗开始时重新评估。", 2f, 10f, 0f);

            // Safety is independent of the latest utterance. A missing or ambiguous
            // command must not suppress the companion's emergency rescue behavior.
            if (situation.PlayerThreatened)
            {
                float allowed = Math.Min(0.35f, profile.RiskTolerance + 0.15f);
                return Finish(order, OrderAcceptance.Accepted, PlayerUnderThreat,
                    "你正在受到威胁，我先保护你。", 2.5f, 18f, allowed, CompanionMode.ProtectPlayer);
            }
            if (situation.SelfHealthRatio < 0.20f)
            {
                return Finish(order, OrderAcceptance.Partial, SelfHealthDanger,
                    "我受伤太重，先撤到安全位置再继续。", 3.5f, 16f, Math.Min(0.1f, profile.RiskTolerance), CompanionMode.Regroup);
            }

            if (intent.NeedsClarification)
                return Finish(order, OrderAcceptance.Rejected, ClarificationRequired,
                    "我没有听清要执行哪一种战术。", 3f, 12f, 0f);
            if (intent.Confidence < 0.40f)
                return Finish(order, OrderAcceptance.Rejected, LowConfidence,
                    "这句话还不够明确，请说明跟随、守点或自主防卫。", 3f, 12f, 0f);
            if (!situation.DestinationReachable && intent.Mode == CompanionMode.HoldPosition)
                return Finish(order, OrderAcceptance.Deferred, UnsafeDestination,
                    "目标位置暂时无法到达，我会先留在安全点。", 4f, 14f, Math.Min(0.2f, profile.RiskTolerance));

            bool highRisk = intent.Mode == CompanionMode.AutonomousDefense || intent.Mode == CompanionMode.ProtectPlayer;
            // Explicit wording raises intent confidence, but cannot erase the
            // relationship floor for a high-risk command.
            if (highRisk && (trust < 0.25f || familiarity < 0.15f))
            {
                return Finish(order, OrderAcceptance.Partial, trust < 0.25f ? LowTrust : LowFamiliarity,
                    "我还不够了解你的战术习惯，会在附近的安全范围内行动。", 8f, 18f,
                    Math.Min(0.2f, profile.RiskTolerance), CompanionMode.FollowPlayer);
            }

            if (highRisk && autonomy < 0.45f && intent.Confidence < 0.80f)
            {
                return Finish(order, OrderAcceptance.Partial, LowFamiliarity,
                    "我先在你附近观察，确认安全后再扩大防区。", 8f, 22f,
                    Math.Min(0.25f, profile.RiskTolerance), CompanionMode.FollowPlayer);
            }

            if (score >= 0.72f || intent.Mode == CompanionMode.Regroup || intent.Mode == CompanionMode.HoldPosition)
            {
                string reason = intent.Mode == CompanionMode.AutonomousDefense && intent.ExplicitAutonomy
                    ? AutonomyGranted : CommandAccepted;
                return Finish(order, OrderAcceptance.Accepted, reason,
                    "收到，我会按这个战术行动。", intent.Mode == CompanionMode.AutonomousDefense ? 45f : 30f,
                    intent.Mode == CompanionMode.FollowPlayer ? 12f : 28f,
                    Math.Min(1f, profile.RiskTolerance + 0.2f));
            }

            return Finish(order, OrderAcceptance.Partial, CommandPartiallyAccepted,
                "我会先执行安全部分，之后再扩大行动范围。", 12f,
                intent.Mode == CompanionMode.FollowPlayer ? 10f : 18f,
                Math.Min(0.35f, profile.RiskTolerance + 0.05f));
        }

        private static CompanionOrder NewOrder(CompanionIntent intent, float score, float autonomy)
        {
            return new CompanionOrder
            {
                Mode = intent.Mode,
                Acceptance = OrderAcceptance.Rejected,
                FollowDistance = intent.Mode == CompanionMode.FollowPlayer ? 3.5f : 5f,
                LeashRadius = intent.Mode == CompanionMode.FollowPlayer ? 12f : 24f,
                AllowedRisk = Math.Min(1f, autonomy),
                DurationSeconds = 15f,
                DecisionScore = score,
                IntentConfidence = intent.Confidence,
            };
        }

        private static CompanionOrder Finish(
            CompanionOrder order,
            OrderAcceptance acceptance,
            string reason,
            string message,
            float duration,
            float leash,
            float allowedRisk,
            CompanionMode? mode = null)
        {
            order.Acceptance = acceptance;
            order.Mode = mode ?? order.Mode;
            order.ReasonCode = reason;
            order.Message = message;
            order.DurationSeconds = duration;
            order.LeashRadius = Math.Max(order.FollowDistance, leash);
            order.AllowedRisk = allowedRisk;
            return order.Sanitize();
        }
    }
}
