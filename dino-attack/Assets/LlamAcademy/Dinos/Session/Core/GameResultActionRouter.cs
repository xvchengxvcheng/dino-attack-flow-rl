using System;

namespace LlamAcademy.Dinos.Session
{
    public enum GameResultAction
    {
        RetryCurrentLayout = 0,
        GenerateNewLayout = 1,
        ReplayWithAi = 2,
        SelectMap = 3,
    }

    public readonly struct GameInferenceLaunchOption
    {
        private GameInferenceLaunchOption(
            bool isAvailable,
            int inferenceSeed,
            string modelId,
            string modelHash,
            string protocolVersion,
            string unavailableReason)
        {
            IsAvailable = isAvailable;
            InferenceSeed = inferenceSeed;
            ModelId = modelId ?? string.Empty;
            ModelHash = modelHash ?? string.Empty;
            ProtocolVersion = protocolVersion ?? string.Empty;
            UnavailableReason = unavailableReason ?? string.Empty;
        }

        public bool IsAvailable { get; }
        public int InferenceSeed { get; }
        public string ModelId { get; }
        public string ModelHash { get; }
        public string ProtocolVersion { get; }
        public string UnavailableReason { get; }

        public static GameInferenceLaunchOption Available(
            int inferenceSeed,
            string modelId,
            string modelHash,
            string protocolVersion)
        {
            _ = new GameLaunchRequest(
                GameMapId.OpenTropicalBattlefield,
                0,
                GameControllerMode.InferenceAI,
                inferenceSeed,
                modelId,
                modelHash,
                protocolVersion,
                true,
                true);
            return new GameInferenceLaunchOption(
                true,
                inferenceSeed,
                modelId,
                modelHash,
                protocolVersion,
                string.Empty);
        }

        public static GameInferenceLaunchOption Unavailable(string reason) =>
            new(
                false,
                0,
                string.Empty,
                string.Empty,
                GameLaunchRequest.CurrentProtocolVersion,
                string.IsNullOrWhiteSpace(reason)
                    ? "A compatible trained AI model is unavailable."
                    : reason);
    }

    public readonly struct GameResultActionRoute
    {
        private GameResultActionRoute(
            bool isAvailable,
            GameLaunchRequest request,
            bool preservePlayerResult,
            string unavailableReason)
        {
            IsAvailable = isAvailable;
            Request = request;
            PreservePlayerResult = preservePlayerResult;
            UnavailableReason = unavailableReason ?? string.Empty;
        }

        public bool IsAvailable { get; }
        public GameLaunchRequest Request { get; }
        public bool PreservePlayerResult { get; }
        public string UnavailableReason { get; }

        public static GameResultActionRoute Available(
            GameLaunchRequest request,
            bool preservePlayerResult) =>
            new(true, request, preservePlayerResult, string.Empty);

        public static GameResultActionRoute Unavailable(string reason) =>
            new(false, default, false, reason);
    }

    public static class GameResultActionRouter
    {
        public static GameResultActionRoute Resolve(
            GameResultAction action,
            GameLaunchRequest current,
            GameInferenceLaunchOption inference)
        {
            if (!current.IsValidRequest || !current.MapId.IsPlayable)
            {
                throw new ArgumentException("A playable current launch request is required.", nameof(current));
            }

            return action switch
            {
                GameResultAction.RetryCurrentLayout =>
                    GameResultActionRoute.Available(GameLaunchContext.DeriveRetry(current), false),
                GameResultAction.GenerateNewLayout =>
                    GameResultActionRoute.Available(GameLaunchContext.DeriveNewLayout(current), false),
                GameResultAction.ReplayWithAi => ResolveAi(current, inference),
                GameResultAction.SelectMap =>
                    GameResultActionRoute.Available(GameLaunchContext.CreateMapSelectionRequest(), false),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
            };
        }

        private static GameResultActionRoute ResolveAi(
            GameLaunchRequest current,
            GameInferenceLaunchOption inference)
        {
            if (!inference.IsAvailable)
            {
                return GameResultActionRoute.Unavailable(inference.UnavailableReason);
            }

            GameLaunchRequest request = GameLaunchContext.DeriveInferenceReplay(
                current,
                inference.InferenceSeed,
                inference.ModelId,
                inference.ModelHash,
                inference.ProtocolVersion);
            return GameResultActionRoute.Available(request, true);
        }
    }
}
