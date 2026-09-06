using System;
using System.Collections;
using System.IO;
using LlamAcademy.Dinos.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.RoundManagement
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class GameSessionRestartService : MonoBehaviour
    {
        private const float MinimumReloadDelay = 0.15f;

        [SerializeField] private int InitialStartSeed = 20260829;

        public static GameSessionRestartService Instance { get; private set; }
        public bool IsReloadPending { get; private set; }
        public bool IsRestartLaunch { get; private set; }
        public int CurrentStartSeed { get; private set; }
        public GameLaunchRequest CurrentLaunchRequest { get; private set; }
        public GameResultSnapshot? PreviousPlayerResult { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            GameLaunchContext.Clear();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Scene activeScene = SceneManager.GetActiveScene();
            if (GameMapId.TryFromSceneName(activeScene.name, out GameMapId activeMap) &&
                GameLaunchContext.TryConsume(
                    activeMap.SceneStableId,
                    out GameLaunchRequest request,
                    out GameResultSnapshot? previousPlayerResult))
            {
                CurrentLaunchRequest = request;
                PreviousPlayerResult = previousPlayerResult;
                CurrentStartSeed = request.LayoutSeed;
                IsRestartLaunch = request.StartAutomatically;
                return;
            }

            GameMapId fallbackMap = activeMap.IsPlayable
                ? activeMap
                : GameMapId.OpenTropicalBattlefield;
            CurrentLaunchRequest = GameLaunchRequest.CreateHuman(fallbackMap, InitialStartSeed);
            CurrentStartSeed = InitialStartSeed;
            PreviousPlayerResult = null;
            IsRestartLaunch = false;
        }

        public bool TryScheduleReload(bool deriveNewStartSeed, float realTimeDelay = MinimumReloadDelay)
        {
            int nextSeed = deriveNewStartSeed
                ? GameLaunchContext.DeriveNextLayoutSeed(CurrentStartSeed)
                : CurrentStartSeed;
            GameLaunchRequest request = deriveNewStartSeed
                ? GameLaunchRequest.CreateHuman(GameMapId.OpenTropicalBattlefield, nextSeed)
                : GameLaunchContext.DeriveRetry(
                    GameLaunchRequest.CreateHuman(GameMapId.OpenTropicalBattlefield, nextSeed));
            return TryScheduleLaunch(request, null, realTimeDelay);
        }

        public bool TryScheduleReloadWithSeed(int nextSeed, float realTimeDelay = MinimumReloadDelay)
        {
            if (nextSeed == int.MinValue)
            {
                return false;
            }

            return TryScheduleLaunch(
                GameLaunchRequest.CreateTraining(GameMapId.OpenTropicalBattlefield, nextSeed),
                null,
                realTimeDelay);
        }

        public bool TryScheduleLaunch(
            GameLaunchRequest request,
            GameResultSnapshot? previousPlayerResult = null,
            float realTimeDelay = MinimumReloadDelay)
        {
            if (IsReloadPending || !request.IsValidRequest ||
                !IsSceneAvailableForLaunch(request.TargetSceneName))
            {
                return false;
            }

            if (!GameLaunchContext.TryPublish(request, previousPlayerResult))
            {
                return false;
            }

            IsReloadPending = true;
            StartCoroutine(Reload(
                request.TargetSceneName,
                Mathf.Max(MinimumReloadDelay, realTimeDelay)));
            return true;
        }

        private static bool IsSceneAvailableForLaunch(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            for (int buildIndex = 0; buildIndex < sceneCount; buildIndex++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                string configuredSceneName = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(configuredSceneName, sceneName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerator Reload(string sceneName, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation != null)
            {
                yield return operation;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
