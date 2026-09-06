using System;

namespace LlamAcademy.Dinos.Session
{
    public static class GameLaunchContext
    {
        private static readonly object Gate = new();
        private static bool HasPendingValue;
        private static GameLaunchRequest PendingRequest;
        private static GameResultSnapshot? PendingPlayerResult;

        public static bool HasPending
        {
            get
            {
                lock (Gate)
                {
                    return HasPendingValue;
                }
            }
        }

        public static bool TryPublish(
            GameLaunchRequest request,
            GameResultSnapshot? previousPlayerResult = null)
        {
            if (!request.IsValidRequest || !ResultMatchesRequest(previousPlayerResult, request))
            {
                return false;
            }

            lock (Gate)
            {
                if (HasPendingValue)
                {
                    return false;
                }

                PendingRequest = request;
                PendingPlayerResult = previousPlayerResult;
                HasPendingValue = true;
                return true;
            }
        }

        public static bool TryConsume(
            string sceneStableId,
            out GameLaunchRequest request,
            out GameResultSnapshot? previousPlayerResult)
        {
            request = default;
            previousPlayerResult = null;
            if (string.IsNullOrEmpty(sceneStableId))
            {
                return false;
            }

            lock (Gate)
            {
                if (!HasPendingValue ||
                    !string.Equals(
                        PendingRequest.TargetSceneStableId,
                        sceneStableId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                request = PendingRequest;
                previousPlayerResult = PendingPlayerResult;
                ClearWithoutLock();
                return true;
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                ClearWithoutLock();
            }
        }

        public static GameLaunchRequest DeriveRetry(GameLaunchRequest current) =>
            new(
                current.MapId,
                current.LayoutSeed,
                GameControllerMode.Human,
                0,
                string.Empty,
                string.Empty,
                GameLaunchRequest.CurrentProtocolVersion,
                true,
                true);

        public static GameLaunchRequest DeriveNewLayout(GameLaunchRequest current) =>
            new(
                current.MapId,
                DeriveNextLayoutSeed(current.LayoutSeed),
                GameControllerMode.Human,
                0,
                string.Empty,
                string.Empty,
                GameLaunchRequest.CurrentProtocolVersion,
                false,
                true);

        public static GameLaunchRequest DeriveInferenceReplay(
            GameLaunchRequest current,
            int inferenceSeed,
            string modelId,
            string modelHash,
            string protocolVersion) =>
            new(
                current.MapId,
                current.LayoutSeed,
                GameControllerMode.InferenceAI,
                inferenceSeed,
                modelId,
                modelHash,
                protocolVersion,
                true,
                true);

        public static GameLaunchRequest CreateMapSelectionRequest() =>
            new(
                GameMapId.MapSelect,
                0,
                GameControllerMode.Human,
                0,
                string.Empty,
                string.Empty,
                GameLaunchRequest.CurrentProtocolVersion);

        public static int DeriveNextLayoutSeed(int current)
        {
            unchecked
            {
                int next = current * 1664525 + 1013904223;
                return next == int.MinValue ? 0 : next;
            }
        }

        private static bool ResultMatchesRequest(
            GameResultSnapshot? result,
            GameLaunchRequest request)
        {
            if (!result.HasValue)
            {
                return true;
            }

            GameResultSnapshot value = result.Value;
            return GameResultPairingRules.SelectPlayerResultForLaunch(value, request).HasValue;
        }

        private static void ClearWithoutLock()
        {
            HasPendingValue = false;
            PendingRequest = default;
            PendingPlayerResult = null;
        }
    }
}
