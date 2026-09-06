using System.Collections;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.Training
{
    public sealed class DinoTrainingSceneReloader : MonoBehaviour
    {
        private bool LocalReloadPending;
        public bool IsReloadPending =>
            LocalReloadPending || GameSessionRestartService.Instance != null && GameSessionRestartService.Instance.IsReloadPending;

        public void ScheduleReload(float realTimeDelay = 0.15f)
        {
            if (IsReloadPending)
            {
                return;
            }

            int nextSeed = DinoTrainingRunContext.AdvanceEpisode();
            GameMapId targetMap = ResolveTargetMap(nextSeed);
            float delay = Mathf.Max(0.15f, realTimeDelay);
            if (GameSessionRestartService.Instance != null)
            {
                GameSessionRestartService.Instance.TryScheduleLaunch(
                    GameLaunchRequest.CreateTraining(targetMap, nextSeed),
                    null,
                    delay);
                return;
            }

            if (!GameLaunchContext.TryPublish(GameLaunchRequest.CreateTraining(targetMap, nextSeed)))
            {
                return;
            }

            LocalReloadPending = true;
            StartCoroutine(Reload(targetMap.SceneName, delay));
        }

        public static string ResolveTargetSceneName(int episodeSeed) =>
            ResolveTargetMap(episodeSeed).SceneName;

        private static GameMapId ResolveTargetMap(int episodeSeed)
        {
            DinoTrainingMap map = DinoTrainingMapSelector.Select(episodeSeed);
            return map == DinoTrainingMap.LayeredBattlefield
                ? GameMapId.LayeredBattlefield
                : GameMapId.OpenTropicalBattlefield;
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
    }
}
