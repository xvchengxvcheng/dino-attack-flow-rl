namespace LlamAcademy.Dinos.Session
{
    public enum GameResultPairingFailure
    {
        None = 0,
        MissingPlayerResult = 1,
        InvalidControllerRoles = 2,
        SceneMismatch = 3,
        LayoutSignatureMismatch = 4,
    }

    public readonly struct GameResultComparison
    {
        public GameResultComparison(GameResultSnapshot human, GameResultSnapshot inferenceAi)
        {
            Human = human;
            InferenceAi = inferenceAi;
        }

        public GameResultSnapshot Human { get; }
        public GameResultSnapshot InferenceAi { get; }
    }

    public static class GameResultPairingRules
    {
        public static bool TryPair(
            GameResultSnapshot? playerResult,
            GameResultSnapshot currentResult,
            out GameResultComparison comparison,
            out GameResultPairingFailure failure)
        {
            comparison = default;
            if (!playerResult.HasValue)
            {
                failure = GameResultPairingFailure.MissingPlayerResult;
                return false;
            }

            GameResultSnapshot human = playerResult.Value;
            if (human.Controller != GameControllerMode.Human ||
                currentResult.Controller != GameControllerMode.InferenceAI)
            {
                failure = GameResultPairingFailure.InvalidControllerRoles;
                return false;
            }

            if (!string.Equals(human.SceneStableId, currentResult.SceneStableId, System.StringComparison.Ordinal))
            {
                failure = GameResultPairingFailure.SceneMismatch;
                return false;
            }

            if (!string.Equals(
                    human.LayoutSignature,
                    currentResult.LayoutSignature,
                    System.StringComparison.Ordinal))
            {
                failure = GameResultPairingFailure.LayoutSignatureMismatch;
                return false;
            }

            comparison = new GameResultComparison(human, currentResult);
            failure = GameResultPairingFailure.None;
            return true;
        }

        public static GameResultSnapshot? SelectPlayerResultForLaunch(
            GameResultSnapshot? playerResult,
            GameLaunchRequest nextRequest)
        {
            if (!playerResult.HasValue || nextRequest.Controller != GameControllerMode.InferenceAI)
            {
                return null;
            }

            GameResultSnapshot value = playerResult.Value;
            return value.Controller == GameControllerMode.Human &&
                   value.MapId == nextRequest.MapId &&
                   value.LayoutSeed == nextRequest.LayoutSeed &&
                   string.Equals(
                       value.SceneStableId,
                       nextRequest.TargetSceneStableId,
                       System.StringComparison.Ordinal)
                ? value
                : null;
        }
    }
}
