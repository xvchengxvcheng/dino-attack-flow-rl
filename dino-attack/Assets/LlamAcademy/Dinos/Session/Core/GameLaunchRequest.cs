using System;

namespace LlamAcademy.Dinos.Session
{
    public readonly struct GameLaunchRequest : IEquatable<GameLaunchRequest>
    {
        public const string CurrentProtocolVersion = "dino_attack_structured_set_v2";

        public GameLaunchRequest(
            GameMapId mapId,
            int layoutSeed,
            GameControllerMode controller,
            int inferenceSeed,
            string modelId,
            string modelHash,
            string protocolVersion,
            bool reuseLayoutSeed = false,
            bool startAutomatically = false)
        {
            if (!IsValid(
                    mapId,
                    layoutSeed,
                    controller,
                    inferenceSeed,
                    modelId,
                    modelHash,
                    protocolVersion,
                    reuseLayoutSeed,
                    startAutomatically))
            {
                throw new ArgumentException("The launch request is invalid or incompatible with the current protocol.");
            }

            MapId = mapId;
            LayoutSeed = layoutSeed;
            Controller = controller;
            InferenceSeed = inferenceSeed;
            ModelId = modelId ?? string.Empty;
            ModelHash = modelHash ?? string.Empty;
            ProtocolVersion = protocolVersion;
            ReuseLayoutSeed = reuseLayoutSeed;
            StartAutomatically = startAutomatically;
        }

        public GameMapId MapId { get; }
        public int LayoutSeed { get; }
        public GameControllerMode Controller { get; }
        public int InferenceSeed { get; }
        public string ModelId { get; }
        public string ModelHash { get; }
        public string ProtocolVersion { get; }
        public bool ReuseLayoutSeed { get; }
        public bool StartAutomatically { get; }
        public string TargetSceneStableId => MapId.SceneStableId;
        public string TargetSceneName => MapId.SceneName;
        public bool IsValidRequest => IsValid(
            MapId,
            LayoutSeed,
            Controller,
            InferenceSeed,
            ModelId,
            ModelHash,
            ProtocolVersion,
            ReuseLayoutSeed,
            StartAutomatically);

        public static GameLaunchRequest CreateHuman(GameMapId mapId, int layoutSeed) =>
            new(
                mapId,
                layoutSeed,
                GameControllerMode.Human,
                0,
                string.Empty,
                string.Empty,
                CurrentProtocolVersion,
                false,
                false);

        public static GameLaunchRequest CreateTraining(GameMapId mapId, int layoutSeed) =>
            new(
                mapId,
                layoutSeed,
                GameControllerMode.TrainingAI,
                0,
                string.Empty,
                string.Empty,
                CurrentProtocolVersion,
                false,
                true);

        public static bool TryCreate(
            GameMapId mapId,
            int layoutSeed,
            GameControllerMode controller,
            int inferenceSeed,
            string modelId,
            string modelHash,
            string protocolVersion,
            out GameLaunchRequest request)
        {
            request = default;
            if (!IsValid(
                    mapId,
                    layoutSeed,
                    controller,
                    inferenceSeed,
                    modelId,
                    modelHash,
                    protocolVersion,
                    false,
                    false))
            {
                return false;
            }

            request = new GameLaunchRequest(
                mapId,
                layoutSeed,
                controller,
                inferenceSeed,
                modelId,
                modelHash,
                protocolVersion,
                false,
                false);
            return true;
        }

        public static bool TryCreate(
            GameMapId mapId,
            int layoutSeed,
            GameControllerMode controller,
            int inferenceSeed,
            string modelId,
            string modelHash,
            string protocolVersion,
            bool reuseLayoutSeed,
            out GameLaunchRequest request)
        {
            request = default;
            if (!IsValid(
                    mapId,
                    layoutSeed,
                    controller,
                    inferenceSeed,
                    modelId,
                    modelHash,
                    protocolVersion,
                    reuseLayoutSeed,
                    reuseLayoutSeed))
            {
                return false;
            }

            request = new GameLaunchRequest(
                mapId,
                layoutSeed,
                controller,
                inferenceSeed,
                modelId,
                modelHash,
                protocolVersion,
                reuseLayoutSeed,
                reuseLayoutSeed);
            return true;
        }

        public bool Equals(GameLaunchRequest other) =>
            MapId == other.MapId &&
            LayoutSeed == other.LayoutSeed &&
            Controller == other.Controller &&
            InferenceSeed == other.InferenceSeed &&
            ReuseLayoutSeed == other.ReuseLayoutSeed &&
            StartAutomatically == other.StartAutomatically &&
            string.Equals(ModelId, other.ModelId, StringComparison.Ordinal) &&
            string.Equals(ModelHash, other.ModelHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ProtocolVersion, other.ProtocolVersion, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is GameLaunchRequest other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MapId.GetHashCode();
                hash = hash * 397 ^ LayoutSeed;
                hash = hash * 397 ^ (int)Controller;
                hash = hash * 397 ^ InferenceSeed;
                hash = hash * 397 ^ ReuseLayoutSeed.GetHashCode();
                hash = hash * 397 ^ StartAutomatically.GetHashCode();
                hash = hash * 397 ^ (ModelId == null ? 0 : StringComparer.Ordinal.GetHashCode(ModelId));
                hash = hash * 397 ^ (ModelHash == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(ModelHash));
                hash = hash * 397 ^ (ProtocolVersion == null ? 0 : StringComparer.Ordinal.GetHashCode(ProtocolVersion));
                return hash;
            }
        }

        public static bool operator ==(GameLaunchRequest left, GameLaunchRequest right) => left.Equals(right);
        public static bool operator !=(GameLaunchRequest left, GameLaunchRequest right) => !left.Equals(right);

        private static bool IsValid(
            GameMapId mapId,
            int layoutSeed,
            GameControllerMode controller,
            int inferenceSeed,
            string modelId,
            string modelHash,
            string protocolVersion,
            bool reuseLayoutSeed,
            bool startAutomatically)
        {
            if (!mapId.IsKnown || layoutSeed == int.MinValue ||
                !Enum.IsDefined(typeof(GameControllerMode), controller) ||
                !string.Equals(protocolVersion, CurrentProtocolVersion, StringComparison.Ordinal))
            {
                return false;
            }

            if (mapId == GameMapId.MapSelect)
            {
                return controller == GameControllerMode.Human &&
                       layoutSeed == 0 &&
                       inferenceSeed == 0 &&
                       !reuseLayoutSeed &&
                       !startAutomatically &&
                       string.IsNullOrEmpty(modelId) &&
                       string.IsNullOrEmpty(modelHash);
            }

            if (!mapId.IsPlayable)
            {
                return false;
            }

            if (controller == GameControllerMode.InferenceAI)
            {
                return inferenceSeed != int.MinValue &&
                       reuseLayoutSeed &&
                       startAutomatically &&
                       !string.IsNullOrWhiteSpace(modelId) &&
                       IsSha256(modelHash);
            }

            return inferenceSeed == 0 &&
                   string.IsNullOrEmpty(modelId) &&
                   string.IsNullOrEmpty(modelHash);
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool isDigit = character >= '0' && character <= '9';
                bool isLowerHex = character >= 'a' && character <= 'f';
                bool isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isLowerHex && !isUpperHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
