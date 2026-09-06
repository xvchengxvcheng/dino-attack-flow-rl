using System;

namespace LlamAcademy.Dinos.Session
{
    public readonly struct GameResultSnapshot : IEquatable<GameResultSnapshot>
    {
        public GameResultSnapshot(
            GameMapId mapId,
            string sceneStableId,
            int layoutSeed,
            GameControllerMode controller,
            bool won,
            float elapsedSeconds,
            int finalMeat,
            float totalReward,
            string layoutSignature,
            string modelId,
            string modelHash,
            string protocolVersion)
        {
            if (!mapId.IsPlayable || layoutSeed == int.MinValue || elapsedSeconds < 0f || finalMeat < 0 ||
                float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) ||
                float.IsNaN(totalReward) || float.IsInfinity(totalReward) ||
                !string.Equals(sceneStableId, mapId.SceneStableId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(layoutSignature) ||
                !string.Equals(protocolVersion, GameLaunchRequest.CurrentProtocolVersion, StringComparison.Ordinal) ||
                !IsValidControllerIdentity(controller, modelId, modelHash))
            {
                throw new ArgumentException("The game result snapshot contains invalid values.");
            }

            MapId = mapId;
            SceneStableId = sceneStableId;
            LayoutSeed = layoutSeed;
            Controller = controller;
            Won = won;
            ElapsedSeconds = elapsedSeconds;
            FinalMeat = finalMeat;
            TotalReward = totalReward;
            LayoutSignature = layoutSignature;
            ModelId = modelId ?? string.Empty;
            ModelHash = modelHash ?? string.Empty;
            ProtocolVersion = protocolVersion;
        }

        public GameMapId MapId { get; }
        public string SceneStableId { get; }
        public int LayoutSeed { get; }
        public GameControllerMode Controller { get; }
        public bool Won { get; }
        public float ElapsedSeconds { get; }
        public int FinalMeat { get; }
        public float TotalReward { get; }
        public string LayoutSignature { get; }
        public string ModelId { get; }
        public string ModelHash { get; }
        public string ProtocolVersion { get; }

        public bool Equals(GameResultSnapshot other) =>
            MapId == other.MapId &&
            string.Equals(SceneStableId, other.SceneStableId, StringComparison.Ordinal) &&
            LayoutSeed == other.LayoutSeed &&
            Controller == other.Controller &&
            Won == other.Won &&
            ElapsedSeconds.Equals(other.ElapsedSeconds) &&
            FinalMeat == other.FinalMeat &&
            TotalReward.Equals(other.TotalReward) &&
            string.Equals(LayoutSignature, other.LayoutSignature, StringComparison.Ordinal) &&
            string.Equals(ModelId, other.ModelId, StringComparison.Ordinal) &&
            string.Equals(ModelHash, other.ModelHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ProtocolVersion, other.ProtocolVersion, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is GameResultSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MapId.GetHashCode();
                hash = hash * 397 ^ (SceneStableId == null ? 0 : StringComparer.Ordinal.GetHashCode(SceneStableId));
                hash = hash * 397 ^ LayoutSeed;
                hash = hash * 397 ^ (int)Controller;
                hash = hash * 397 ^ Won.GetHashCode();
                hash = hash * 397 ^ ElapsedSeconds.GetHashCode();
                hash = hash * 397 ^ FinalMeat;
                hash = hash * 397 ^ TotalReward.GetHashCode();
                hash = hash * 397 ^ (LayoutSignature == null ? 0 : StringComparer.Ordinal.GetHashCode(LayoutSignature));
                hash = hash * 397 ^ (ModelId == null ? 0 : StringComparer.Ordinal.GetHashCode(ModelId));
                hash = hash * 397 ^ (ModelHash == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(ModelHash));
                hash = hash * 397 ^ (ProtocolVersion == null ? 0 : StringComparer.Ordinal.GetHashCode(ProtocolVersion));
                return hash;
            }
        }

        private static bool IsValidControllerIdentity(
            GameControllerMode controller,
            string modelId,
            string modelHash)
        {
            if (controller == GameControllerMode.Human)
            {
                return string.IsNullOrEmpty(modelId) && string.IsNullOrEmpty(modelHash);
            }

            if (controller != GameControllerMode.InferenceAI || string.IsNullOrWhiteSpace(modelId) ||
                modelHash == null || modelHash.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < modelHash.Length; index++)
            {
                char character = modelHash[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool operator ==(GameResultSnapshot left, GameResultSnapshot right) => left.Equals(right);
        public static bool operator !=(GameResultSnapshot left, GameResultSnapshot right) => !left.Equals(right);
    }
}
