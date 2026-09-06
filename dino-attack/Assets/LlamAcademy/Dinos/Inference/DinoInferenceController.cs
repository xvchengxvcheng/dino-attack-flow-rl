using System;
using System.Collections;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.Training;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.Inference
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class DinoInferenceController : MonoBehaviour
    {
        private readonly DinoInferenceDecisionPolicy DecisionPolicy = new();
        private IDinoInferencePolicyRunner Runner;
        private DinoSentisPolicyRunner OwnedRunner;
        private DinoStructuredObservationBuilder ObservationBuilder;
        private DinoSpawner Spawner;
        private RoundManager Round;
        private DinoSO[] DinoTypes = Array.Empty<DinoSO>();
        private DinoDeploymentZone[] Zones = Array.Empty<DinoDeploymentZone>();
        private bool IsConfigured;
        private int RejectedDecisionCount;
        public string LastDecisionFailureReason { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterSceneBootstrap()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapInitialInferenceSession() => BootstrapInferenceSession();

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) =>
            BootstrapInferenceSession();

        private static void BootstrapInferenceSession()
        {
            GameSessionRestartService restart = GameSessionRestartService.Instance;
            if (restart == null || restart.CurrentLaunchRequest.Controller != GameControllerMode.InferenceAI)
            {
                return;
            }

            DinoSpawner spawner = DinoSpawner.Instance;
            GameObject host = spawner == null ? restart.gameObject : spawner.gameObject;
            if (host.GetComponent<DinoInferenceController>() == null)
            {
                host.AddComponent<DinoInferenceController>();
            }
        }

        private IEnumerator Start()
        {
            yield return null;
            if (IsConfigured)
            {
                yield break;
            }

            GameSessionRestartService restart = GameSessionRestartService.Instance;
            if (restart == null || restart.CurrentLaunchRequest.Controller != GameControllerMode.InferenceAI)
            {
                enabled = false;
                yield break;
            }

            DinoSpawner.Instance?.SetPlayerInputEnabled(false);
            DinoInferenceModelConfig config = DinoInferenceModelConfig.LoadForRequest(
                restart.CurrentLaunchRequest);
            if (config == null)
            {
                Debug.LogWarning(
                    "Dino inference is disabled: no installed model matches the launch request.",
                    this);
                enabled = false;
                yield break;
            }
            if (!DinoSentisPolicyRunner.TryCreate(config, out DinoSentisPolicyRunner runner, out string reason) ||
                !TryConfigure(restart.CurrentLaunchRequest, config, runner, out reason))
            {
                runner?.Dispose();
                Debug.LogWarning($"Dino inference is disabled: {reason}", this);
                enabled = false;
                yield break;
            }

            OwnedRunner = runner;
        }

        public bool TryConfigure(
            GameLaunchRequest request,
            DinoInferenceModelConfig config,
            IDinoInferencePolicyRunner runner,
            out string failureReason)
        {
            return TryConfigureInternal(request, config, runner, true, out failureReason);
        }

        public bool TryConfigureDeferredStart(
            GameLaunchRequest request,
            DinoInferenceModelConfig config,
            IDinoInferencePolicyRunner runner,
            out string failureReason)
        {
            return TryConfigureInternal(request, config, runner, false, out failureReason);
        }

        private bool TryConfigureInternal(
            GameLaunchRequest request,
            DinoInferenceModelConfig config,
            IDinoInferencePolicyRunner runner,
            bool startRound,
            out string failureReason)
        {
            IsConfigured = false;
            if (!request.IsValidRequest || request.Controller != GameControllerMode.InferenceAI)
            {
                failureReason = "Inference requires a valid InferenceAI launch request.";
                return false;
            }

            if (config == null)
            {
                failureReason = "The inference model configuration is missing.";
                return false;
            }

            if (!config.Matches(request, out failureReason))
            {
                return false;
            }

            if (runner == null)
            {
                failureReason = "The inference policy runner is missing.";
                return false;
            }

            ObservationBuilder = FindFirstObjectByType<DinoStructuredObservationBuilder>(FindObjectsInactive.Include);
            Spawner = DinoSpawner.Instance;
            Round = RoundManager.Instance;
            if (ObservationBuilder == null || Spawner == null || Round == null)
            {
                failureReason = "Inference requires the structured observation builder, DinoSpawner, and RoundManager.";
                return false;
            }

            DinoTypes = ObservationBuilder.ConfiguredDinoTypes;
            Zones = ObservationBuilder.ConfiguredZones;
            if (DinoTypes.Length != 3 || Zones.Length != DinoTrainingObservationLayout.RegionCount)
            {
                failureReason = "Inference requires the frozen three-dino and five-zone action configuration.";
                return false;
            }

            for (int index = 0; index < Zones.Length; index++)
            {
                if (Zones[index] == null || !Zones[index].IsGeometryValid || (int)Zones[index].Id != index)
                {
                    failureReason = "Inference zones must be valid and ordered Z1-Z5.";
                    return false;
                }
            }

            DinoTrainingAgent agent = FindFirstObjectByType<DinoTrainingAgent>(FindObjectsInactive.Include);
            DinoTrainingDecisionScheduler scheduler =
                FindFirstObjectByType<DinoTrainingDecisionScheduler>(FindObjectsInactive.Include);
            if (agent != null)
            {
                agent.enabled = false;
            }
            if (scheduler != null)
            {
                scheduler.enabled = false;
            }

            Runner = runner;
            RejectedDecisionCount = 0;
            LastDecisionFailureReason = string.Empty;
            ObservationBuilder.SetInferenceLastActionValidity(true);
            Spawner.SetPlayerInputEnabled(false);
            if (startRound && Round.State == GameState.Setup)
            {
                Round.StartRound();
            }

            IsConfigured = true;
            failureReason = string.Empty;
            return true;
        }

        public bool TryProcessDecision(int academyStep, out string failureReason)
        {
            if (!IsConfigured || Round == null || Runner == null ||
                Round.State != GameState.Running ||
                !DecisionPolicy.TryBeginDecision(true, academyStep))
            {
                failureReason = "No inference decision is due for this simulation step.";
                return false;
            }

            DinoStructuredObservationFrame frame = ObservationBuilder.GetFrameForAcademyStep(academyStep);
            if (!Runner.TryRun(frame, out float[] rawActions, out failureReason) ||
                !DecisionPolicy.TrySanitizeActions(rawActions, out float[] actions, out failureReason) ||
                !DinoTrainingActionCodec.TryDecode(actions, out DecodedDinoTrainingAction decoded))
            {
                if (string.IsNullOrEmpty(failureReason))
                {
                    failureReason = "The inference action could not be decoded.";
                }
                return RejectDecision(failureReason);
            }

            if (decoded.IsWait)
            {
                AcceptDecision();
                failureReason = string.Empty;
                return true;
            }

            if (decoded.ZoneIndex < 0 || decoded.ZoneIndex >= Zones.Length ||
                decoded.DinoIndex < 0 || decoded.DinoIndex >= DinoTypes.Length ||
                !Zones[decoded.ZoneIndex].TryResolveRelative(decoded.RelativeUv, out Vector3 position))
            {
                failureReason = "The decoded inference deployment was rejected.";
                return RejectDecision(failureReason);
            }

            if (!Spawner.TryDeploy(
                    DinoTypes[decoded.DinoIndex],
                    position,
                    false,
                    out DeploymentResult deploymentResult,
                    out _))
            {
                failureReason = $"The decoded inference deployment was rejected: {deploymentResult.FailureReason}.";
                return RejectDecision(failureReason);
            }

            AcceptDecision();
            failureReason = string.Empty;
            return true;
        }

        private void AcceptDecision()
        {
            LastDecisionFailureReason = string.Empty;
            ObservationBuilder.SetInferenceLastActionValidity(true);
        }

        private bool RejectDecision(string reason)
        {
            LastDecisionFailureReason = reason ?? "Inference decision failed.";
            RejectedDecisionCount++;
            ObservationBuilder.SetInferenceLastActionValidity(false);
            if (RejectedDecisionCount == 1 || RejectedDecisionCount % 10 == 0)
            {
                Debug.LogWarning(
                    $"Dino inference rejected {RejectedDecisionCount} decision(s). Last reason: {LastDecisionFailureReason}",
                    this);
            }
            return false;
        }

        private void OnEnable()
        {
            Academy.Instance.AgentPreStep -= OnAcademyPreStep;
            Academy.Instance.AgentPreStep += OnAcademyPreStep;
        }

        private void OnDisable()
        {
            if (Academy.IsInitialized)
            {
                Academy.Instance.AgentPreStep -= OnAcademyPreStep;
            }
        }

        private void OnAcademyPreStep(int academyStep) =>
            TryProcessDecision(academyStep, out _);

        private void OnDestroy()
        {
            OwnedRunner?.Dispose();
            OwnedRunner = null;
        }
    }
}
