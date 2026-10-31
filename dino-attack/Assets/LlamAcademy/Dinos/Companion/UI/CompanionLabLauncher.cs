using UnityEngine;
using UnityEngine.SceneManagement;
using LlamAcademy.Dinos.Session;

namespace LlamAcademy.Dinos.Companion
{
    /// <summary>Small scene-launch adapter used by the legacy MapSelect canvas.</summary>
    public sealed class CompanionLabLauncher : MonoBehaviour
    {
        [SerializeField] private string LabSceneName = "CompanionLab";

        public void Launch()
        {
            // Explicit navigation to the lab cancels any interrupted battlefield launch.
            GameLaunchContext.Clear();
            SceneManager.LoadScene(LabSceneName);
        }
        public void OpenLab() => Launch();
    }
}
