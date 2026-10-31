using System;

namespace LlamAcademy.Dinos.Companion
{
    public enum CompanionMode
    {
        FollowPlayer,
        AutonomousDefense,
        HoldPosition,
        ProtectPlayer,
        Regroup,
    }

    public enum OrderAcceptance
    {
        Accepted,
        Partial,
        Deferred,
        Rejected,
    }

    [Serializable]
    public sealed class CompanionProfileData
    {
        public const int CurrentSchemaVersion = 2;
        public int SchemaVersion = CurrentSchemaVersion;
        public string ProfileId = "map4_companion";
        public string LastModifiedUtc = string.Empty;
        public string Source = "default";
        public float Affinity = 50f;
        public float Familiarity = 25f;
        public float Trust = 45f;
        public float Respect = 45f;
        public float Stress = 0f;
        public float Extraversion = 0.35f;
        public float Sensation = 0.42f;
        public float Intuition = 0.78f;
        public float Thinking = 0.38f;
        public float Feeling = 0.76f;
        public float RiskTolerance = 0.35f;
        public float NeedForAutonomy = 0.55f;

        public CompanionProfileData Clone() => new CompanionProfileData
        {
            SchemaVersion = SchemaVersion, ProfileId = ProfileId, LastModifiedUtc = LastModifiedUtc, Source = Source,
            Affinity = Affinity, Familiarity = Familiarity, Trust = Trust, Respect = Respect, Stress = Stress,
            Extraversion = Extraversion, Sensation = Sensation, Intuition = Intuition, Thinking = Thinking,
            Feeling = Feeling, RiskTolerance = RiskTolerance, NeedForAutonomy = NeedForAutonomy,
        };

        public CompanionProfileData Sanitize()
        {
            SchemaVersion = SchemaVersion <= 0 ? CurrentSchemaVersion : Math.Min(SchemaVersion, CurrentSchemaVersion);
            ProfileId = string.IsNullOrWhiteSpace(ProfileId) ? "map4_companion" : ProfileId.Trim();
            LastModifiedUtc = LastModifiedUtc ?? string.Empty;
            Source = string.IsNullOrWhiteSpace(Source) ? "unknown" : Source.Trim();
            Affinity = Clamp(Affinity, 0f, 100f); Familiarity = Clamp(Familiarity, 0f, 100f);
            Trust = Clamp(Trust, 0f, 100f); Respect = Clamp(Respect, 0f, 100f); Stress = Clamp(Stress, 0f, 100f);
            Extraversion = Clamp01(Extraversion); Sensation = Clamp01(Sensation); Intuition = Clamp01(Intuition);
            Thinking = Clamp01(Thinking); Feeling = Clamp01(Feeling); RiskTolerance = Clamp01(RiskTolerance);
            NeedForAutonomy = Clamp01(NeedForAutonomy);
            return this;
        }

        public static CompanionProfileData CreateDefault() => new CompanionProfileData().Sanitize();
        private static float Clamp01(float value) => Clamp(value, 0f, 1f);
        private static float Clamp(float value, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? min : Math.Max(min, Math.Min(max, value));
    }

    [Serializable]
    public sealed class CompanionIntent
    {
        public CompanionMode Mode = CompanionMode.FollowPlayer;
        public float Confidence;
        public bool NeedsClarification;
        public bool ExplicitAutonomy;
        public float TacticalUrgency;
        public string Source = "local";
        public string RawText = string.Empty;
        public CompanionIntent Clone() => new CompanionIntent { Mode = Mode, Confidence = Confidence, NeedsClarification = NeedsClarification,
            ExplicitAutonomy = ExplicitAutonomy, TacticalUrgency = TacticalUrgency, Source = Source, RawText = RawText };
        public CompanionIntent Sanitize()
        {
            if (!Enum.IsDefined(typeof(CompanionMode), Mode)) Mode = CompanionMode.FollowPlayer;
            Confidence = Clamp01(Confidence); TacticalUrgency = Clamp01(TacticalUrgency);
            Source = string.IsNullOrWhiteSpace(Source) ? "unknown" : Source.Trim(); RawText = RawText ?? string.Empty; return this;
        }
        private static float Clamp01(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
    }

    [Serializable]
    public sealed class CompanionSituation
    {
        // Running is the compact public API; RoundRunning is a runtime-controller alias.
        public bool Running;
        public float SelfHealthRatio = 1f;
        public float PlayerHealthRatio = 1f;
        public bool PlayerThreatened;
        public float VillageThreat;
        public bool DestinationReachable = true;
        public bool RoundRunning { get => Running; set => Running = value; }
        public CompanionSituation Sanitize() { SelfHealthRatio = Clamp01(SelfHealthRatio); PlayerHealthRatio = Clamp01(PlayerHealthRatio); VillageThreat = Clamp01(VillageThreat); return this; }
        private static float Clamp01(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
    }

    [Serializable]
    public sealed class CompanionOrder
    {
        public CompanionMode Mode;
        public OrderAcceptance Acceptance;
        public float FollowDistance;
        public float LeashRadius;
        public float AllowedRisk;
        public float DurationSeconds;
        public string ReasonCode = string.Empty;
        public string Message = string.Empty;
        public float DecisionScore;
        public float IntentConfidence;
        public bool HasAnchor;
        // Anchor is intentionally not represented in Core: Unity world positions belong to the runtime adapter.
        public static CompanionOrder Default(CompanionMode mode) => new CompanionOrder
        {
            Mode = mode, Acceptance = OrderAcceptance.Accepted, FollowDistance = 3.5f, LeashRadius = 12f,
            AllowedRisk = 0.25f, DurationSeconds = 30f, ReasonCode = "Default", Message = string.Empty, DecisionScore = 1f, IntentConfidence = 1f,
        };
        public CompanionOrder Sanitize()
        {
            if (!Enum.IsDefined(typeof(CompanionMode), Mode)) Mode = CompanionMode.FollowPlayer;
            if (!Enum.IsDefined(typeof(OrderAcceptance), Acceptance)) Acceptance = OrderAcceptance.Rejected;
            FollowDistance = Clamp(FollowDistance, 1.5f, 20f); LeashRadius = Clamp(LeashRadius, FollowDistance, 60f);
            AllowedRisk = Clamp(AllowedRisk, 0f, 1f); DurationSeconds = Clamp(DurationSeconds, 0f, 300f);
            DecisionScore = Clamp(DecisionScore, 0f, 1f); IntentConfidence = Clamp(IntentConfidence, 0f, 1f);
            ReasonCode = ReasonCode ?? string.Empty; Message = Message ?? string.Empty; return this;
        }
        private static float Clamp(float value, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? min : Math.Max(min, Math.Min(max, value));
    }

    public static class CompanionLocalIntentParser
    {
        public static CompanionIntent Parse(string text)
        {
            string value = (text ?? string.Empty).Trim();
            CompanionIntent intent = new CompanionIntent { RawText = value, Source = "local" };
            if (value.Length == 0) { intent.NeedsClarification = true; intent.Confidence = 0f; return intent; }
            bool negativeLeave = ContainsAny(value, "不要离开", "别离开", "不要走", "别走", "不许离开");
            bool negativeFollow = ContainsAny(value, "不要跟", "别跟", "不必跟", "不用跟");
            bool autonomy = ContainsAny(value, "自主", "自己判断", "自由行动", "随你", "你决定", "自行防卫", "独立防守");
            bool protect = ContainsAny(value, "保护我", "护着我", "拦住攻击", "优先救我");
            bool follow = ContainsAny(value, "待在我身边", "在我身边", "跟着我", "跟我", "靠近我", "跟随");
            bool hold = ContainsAny(value, "守住", "守点", "原地", "待在这里", "坚守", "留守");
            bool regroup = ContainsAny(value, "回来", "回到我身边", "集合", "重聚", "撤回");
            bool defense = ContainsAny(value, "自主防卫", "自己防守", "离开", "村口", "防守村庄", "守卫村庄", "独立防守") && !negativeLeave;
            if (negativeFollow && !defense) { intent.Mode = CompanionMode.AutonomousDefense; intent.ExplicitAutonomy = true; intent.Confidence = .84f; }
            else if (regroup || negativeLeave || protect || follow) { intent.Mode = regroup ? CompanionMode.Regroup : (protect ? CompanionMode.ProtectPlayer : CompanionMode.FollowPlayer); intent.Confidence = .9f; }
            else if (defense || autonomy) { intent.Mode = CompanionMode.AutonomousDefense; intent.ExplicitAutonomy = autonomy; intent.Confidence = autonomy && defense ? .93f : .78f; }
            else if (hold) { intent.Mode = CompanionMode.HoldPosition; intent.Confidence = .84f; }
            else { intent.NeedsClarification = true; intent.Confidence = .18f; }
            intent.TacticalUrgency = ContainsAny(value, "立刻", "马上", "紧急", "快", "危险") ? .85f : .35f;
            return intent.Sanitize();
        }
        private static bool ContainsAny(string value, params string[] terms) { for (int i = 0; i < terms.Length; i++) if (value.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }
    }
}
