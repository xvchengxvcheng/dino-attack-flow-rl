using System;
using LlamAcademy.Dinos.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.Training
{
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    public sealed class DinoTrainingEntryBootstrap : MonoBehaviour
    {
        private static bool RouteAttempted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => RouteAttempted = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RouteCommandLineEntry()
        {
            TryRoute(Environment.GetCommandLineArgs());
        }

        private void Awake()
        {
            TryRoute(Environment.GetCommandLineArgs());
        }

        public static bool TryRoute(string[] arguments)
        {
            if (RouteAttempted || !DinoTrainingLaunchOptions.TryParse(arguments, out DinoTrainingLaunchOptions options))
            {
                return false;
            }

            RouteAttempted = true;
            int firstEpisodeSeed = DinoTrainingRunContext.DeriveEpisodeSeed(
                options.BaseSeed,
                options.EnvironmentIndex,
                0);
            DinoTrainingMap selected = DinoTrainingMapSelector.Select(firstEpisodeSeed);
            GameMapId map = selected == DinoTrainingMap.LayeredBattlefield
                ? GameMapId.LayeredBattlefield
                : GameMapId.OpenTropicalBattlefield;
            if (!GameLaunchContext.TryPublish(GameLaunchRequest.CreateTraining(map, firstEpisodeSeed)))
            {
                return false;
            }

            Scene active = SceneManager.GetActiveScene();
            if (!string.Equals(active.name, map.SceneName, StringComparison.Ordinal))
            {
                SceneManager.LoadScene(map.SceneName, LoadSceneMode.Single);
            }
            return true;
        }
    }
}
