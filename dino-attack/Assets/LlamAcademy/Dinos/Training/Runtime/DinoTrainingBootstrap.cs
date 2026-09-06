using System;
using System.Collections;
using System.Reflection;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.UI;
using LlamAcademy.Dinos.Unit;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Training
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class DinoTrainingBootstrap : MonoBehaviour
    {
        [SerializeField] private DinoTrainingAgent Agent;
        [SerializeField] private DinoStructuredObservationBuilder ObservationBuilder;
        [SerializeField] private DinoStructuredObservationSensorComponent SensorComponent;
        [SerializeField] private DinoTrainingDecisionScheduler DecisionScheduler;
        [SerializeField] private DinoTrainingSceneReloader SceneReloader;
        [SerializeField] private DinoTrainingArea TrainingArea;
        [SerializeField] private BehaviorParameters BehaviorParameters;
        [SerializeField] private GameSessionRestartService RestartService;

        private static Func<string[]> CommandLineArgumentsProvider = Environment.GetCommandLineArgs;
        private DinoSpawner TrainingSpawner;
        private EnemyAIController TrainingEnemy;
        private bool TrainingClockSubscribed;
        private float LastLoggedTrainingTimeScale = float.NaN;

        public bool IsTrainingMode { get; private set; }
        public int TrainingClockRepairCount { get; private set; }

        private void Awake()
        {
            string[] arguments = CommandLineArgumentsProvider?.Invoke() ?? Array.Empty<string>();
            if (!DinoTrainingLaunchOptions.TryParse(arguments, out DinoTrainingLaunchOptions options))
            {
                IsTrainingMode = false;
                Time.captureDeltaTime = 0f;
                return;
            }

            IsTrainingMode = true;
            DinoTrainingRunContext.Configure(
                options.BaseSeed,
                options.EnvironmentIndex,
                options.ProcessGeneration);
            ApplyEpisodeSeedBeforeSessionInitialization(DinoTrainingRunContext.EpisodeSeed);
            SetFormalTrainingComponentsEnabled(true);
            SubscribeToTrainingClock();
            SynchronizeAndAuditTrainingClock();
        }

        public static float CalculateCaptureDeltaTime(float fixedDeltaTime, float timeScale)
        {
            if (!float.IsFinite(fixedDeltaTime) || fixedDeltaTime <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedDeltaTime),
                    "The fixed timestep must be finite and positive.");
            }
            if (!float.IsFinite(timeScale) || timeScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeScale),
                    "The training time scale must be finite and positive.");
            }

            return fixedDeltaTime / timeScale;
        }

        public static int CalculateCaptureFrameRate(float fixedDeltaTime, float timeScale)
        {
            float captureDeltaTime = CalculateCaptureDeltaTime(fixedDeltaTime, timeScale);
            double captureFrameRate = 1.0 / captureDeltaTime;
            if (!double.IsFinite(captureFrameRate) || captureFrameRate > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeScale),
                    "The resulting capture frame rate is outside Unity's supported range.");
            }

            return Math.Max(1, (int)Math.Round(
                captureFrameRate,
                MidpointRounding.AwayFromZero));
        }

        public static float SynchronizeCaptureDeltaTimeWithCurrentTimeScale()
        {
            int captureFrameRate = CalculateCaptureFrameRate(
                Time.fixedDeltaTime,
                Time.timeScale);
            if (Time.captureFramerate != captureFrameRate)
            {
                // captureFramerate is Unity's persistent owner of captureDeltaTime.
                // Writing captureDeltaTime alone while captureFramerate remains zero is
                // reverted by Unity at the next player-loop iteration.
                Time.captureFramerate = captureFrameRate;
            }
            return Time.captureDeltaTime;
        }

        private void SubscribeToTrainingClock()
        {
            Academy.Instance.AgentPreStep += OnAcademyPreStep;
            TrainingClockSubscribed = true;
        }

        private void OnAcademyPreStep(int academyStep)
        {
            SynchronizeAndAuditTrainingClock();
        }

        private void SynchronizeAndAuditTrainingClock()
        {
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            int previousCaptureFrameRate = Time.captureFramerate;
            float activeTimeScale = Time.timeScale;
            int expectedCaptureFrameRate = CalculateCaptureFrameRate(
                Time.fixedDeltaTime,
                activeTimeScale);
            float captureDeltaTime = SynchronizeCaptureDeltaTimeWithCurrentTimeScale();
            bool repairedOverride = previousCaptureFrameRate != expectedCaptureFrameRate ||
                                    !Mathf.Approximately(
                                        previousCaptureDeltaTime,
                                        captureDeltaTime);
            if (repairedOverride)
            {
                TrainingClockRepairCount++;
            }
            if (repairedOverride ||
                !Mathf.Approximately(LastLoggedTrainingTimeScale, activeTimeScale))
            {
                Debug.Log(
                    "DINO_TRAINING_CLOCK " +
                    $"time_scale={activeTimeScale:R}; " +
                    $"fixed_delta_time={Time.fixedDeltaTime:R}; " +
                    $"capture_frame_rate={Time.captureFramerate}; " +
                    $"capture_delta_time={captureDeltaTime:R}; " +
                    $"repaired_override={repairedOverride.ToString().ToLowerInvariant()}",
                    this);
                LastLoggedTrainingTimeScale = activeTimeScale;
            }
        }

        private IEnumerator Start()
        {
            if (!IsTrainingMode)
            {
                yield break;
            }

            // Scene objects finish Awake before presentation components are disabled. In
            // particular, RuntimeUI needs its UIDocument visual tree during Awake.
            ShutdownPresentationComponents();
            SubscribeToRuntimePresentationSources();
            while (RoundManager.Instance == null || RoundManager.Instance.State != GameState.Setup)
            {
                yield return null;
            }

            ShutdownPresentationComponents();
            RoundManager.Instance.StartRound();
            yield return null;
            ShutdownPresentationComponents();
        }

        private void SubscribeToRuntimePresentationSources()
        {
            TrainingSpawner = FindFirstObjectByType<DinoSpawner>(FindObjectsInactive.Include);
            if (TrainingSpawner != null) TrainingSpawner.OnSpawnDino += ShutdownUnitPresentation;

            TrainingEnemy = FindFirstObjectByType<EnemyAIController>(FindObjectsInactive.Include);
            if (TrainingEnemy == null) return;
            TrainingEnemy.OnSpawnDefender += ShutdownDefenderPresentation;
            TrainingEnemy.OnSpawnWall += ShutdownWallPresentation;
        }

        private static void ShutdownUnitPresentation(Unit.Unit unit) => ShutdownPresentation(unit);
        private static void ShutdownDefenderPresentation(Defender defender) => ShutdownPresentation(defender);
        private static void ShutdownWallPresentation(Wall wall) => ShutdownPresentation(wall);

        private static void ShutdownPresentation(Component root)
        {
            if (root == null) return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Light light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (AudioBehaviour audio in root.GetComponentsInChildren<AudioBehaviour>(true)) audio.enabled = false;
        }

        private void ApplyEpisodeSeedBeforeSessionInitialization(int episodeSeed)
        {
            RestartService ??= FindFirstObjectByType<GameSessionRestartService>(FindObjectsInactive.Include);
            if (RestartService == null)
            {
                throw new InvalidOperationException("Formal training requires GameSessionRestartService.");
            }

            FieldInfo initialSeed = typeof(GameSessionRestartService).GetField(
                "InitialStartSeed", BindingFlags.Instance | BindingFlags.NonPublic);
            if (initialSeed == null)
            {
                throw new MissingFieldException(typeof(GameSessionRestartService).FullName, "InitialStartSeed");
            }
            initialSeed.SetValue(RestartService, episodeSeed);
        }

        private void SetFormalTrainingComponentsEnabled(bool enabled)
        {
            foreach (DinoTrainingScenarioRandomizer randomizer in
                     FindObjectsByType<DinoTrainingScenarioRandomizer>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                randomizer.enabled = false;
            }

            if (TrainingArea != null) TrainingArea.enabled = enabled;
            if (SceneReloader != null) SceneReloader.enabled = enabled;
            if (ObservationBuilder != null) ObservationBuilder.enabled = enabled;
            if (SensorComponent != null) SensorComponent.enabled = enabled;
            if (BehaviorParameters != null) BehaviorParameters.enabled = enabled;
            if (Agent != null) Agent.enabled = enabled;
            if (DecisionScheduler != null) DecisionScheduler.enabled = enabled;

            BoxCollider bounds = TrainingArea == null ? null : TrainingArea.GetComponent<BoxCollider>();
            if (bounds != null) bounds.enabled = enabled;
        }

        private static void ShutdownPresentationComponents()
        {
            DinoSpawner spawner = FindFirstObjectByType<DinoSpawner>(FindObjectsInactive.Include);
            spawner?.SetPlayerInputEnabled(false);

            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                camera.enabled = false;
            foreach (CameraControl control in FindObjectsByType<CameraControl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                control.enabled = false;
            foreach (RuntimeUI ui in FindObjectsByType<RuntimeUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                ui.enabled = false;
            foreach (UIDocument document in FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                document.enabled = false;
            foreach (PlaceDinoVisualization visualization in FindObjectsByType<PlaceDinoVisualization>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                visualization.enabled = false;
            foreach (DinoDeploymentZonePresenter presenter in FindObjectsByType<DinoDeploymentZonePresenter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                presenter.enabled = false;
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                renderer.enabled = false;
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                light.enabled = false;
            foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                volume.enabled = false;
            foreach (ParticleSystem particles in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (AudioBehaviour audio in FindObjectsByType<AudioBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                audio.enabled = false;
        }

        private void OnDestroy()
        {
            if (TrainingClockSubscribed && Academy.IsInitialized)
            {
                Academy.Instance.AgentPreStep -= OnAcademyPreStep;
            }
            if (TrainingSpawner != null) TrainingSpawner.OnSpawnDino -= ShutdownUnitPresentation;
            if (TrainingEnemy == null) return;
            TrainingEnemy.OnSpawnDefender -= ShutdownDefenderPresentation;
            TrainingEnemy.OnSpawnWall -= ShutdownWallPresentation;
        }
    }
}
