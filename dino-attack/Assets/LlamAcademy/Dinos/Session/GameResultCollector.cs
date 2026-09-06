using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Map.Adapters;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Training;
using UnityEngine;

namespace LlamAcademy.Dinos.Session
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class GameResultCollector : MonoBehaviour
    {
        private GameLaunchRequest LaunchRequest;
        private GameResultSnapshot? PreviousPlayerResult;
        private bool IsConfigured;
        private bool IsSubscribed;

        public event Action<GameResultSnapshot> ResultFrozen;
        public event Action<GameResultComparison> ComparisonReady;
        public event Action<GameResultPairingFailure> ComparisonRejected;

        public bool HasFrozenResult { get; private set; }
        public GameResultSnapshot FrozenResult { get; private set; }
        public GameResultComparison? Comparison { get; private set; }
        public GameResultPairingFailure PairingFailure { get; private set; }

        public void Configure(
            GameLaunchRequest launchRequest,
            GameResultSnapshot? previousPlayerResult)
        {
            LaunchRequest = launchRequest;
            PreviousPlayerResult = GameResultPairingRules.SelectPlayerResultForLaunch(
                previousPlayerResult,
                launchRequest);
            // Training results are owned by ML-Agents. Player/AI comparison snapshots only
            // support Human and InferenceAI identities and must never run on TrainingAI endings.
            IsConfigured = launchRequest.IsValidRequest &&
                           launchRequest.MapId.IsPlayable &&
                           launchRequest.Controller != GameControllerMode.TrainingAI;
            HasFrozenResult = false;
            FrozenResult = default;
            Comparison = null;
            PairingFailure = GameResultPairingFailure.None;
            if (IsConfigured && launchRequest.MapId == GameMapId.OpenTropicalBattlefield &&
                FindObjectsByType<OpenTropicalBattlefieldLayoutSignatureProvider>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length == 0)
            {
                gameObject.AddComponent<OpenTropicalBattlefieldLayoutSignatureProvider>();
            }
        }

        public bool TryFreeze(GameResultSnapshot snapshot)
        {
            if (HasFrozenResult || !IsConfigured ||
                snapshot.MapId != LaunchRequest.MapId ||
                snapshot.LayoutSeed != LaunchRequest.LayoutSeed ||
                snapshot.Controller != LaunchRequest.Controller)
            {
                return false;
            }

            HasFrozenResult = true;
            FrozenResult = snapshot;
            ResultFrozen?.Invoke(snapshot);

            if (snapshot.Controller == GameControllerMode.InferenceAI)
            {
                if (GameResultPairingRules.TryPair(
                        PreviousPlayerResult,
                        snapshot,
                        out GameResultComparison comparison,
                        out GameResultPairingFailure failure))
                {
                    Comparison = comparison;
                    PairingFailure = GameResultPairingFailure.None;
                    ComparisonReady?.Invoke(comparison);
                }
                else
                {
                    PairingFailure = failure;
                    ComparisonRejected?.Invoke(failure);
                }
            }

            return true;
        }

        private IEnumerator Start()
        {
            yield return null;
            Subscribe();
            if (RoundManager.Instance != null && RoundManager.Instance.State == GameState.Ended)
            {
                TryCaptureFromScene();
            }
        }

        private void Subscribe()
        {
            if (IsSubscribed || RoundManager.Instance == null)
            {
                return;
            }

            RoundManager.Instance.OnGameStateChange += OnGameStateChange;
            IsSubscribed = true;
        }

        private void OnGameStateChange(GameState oldState, GameState newState)
        {
            if (newState == GameState.Ended)
            {
                TryCaptureFromScene();
            }
        }

        private void TryCaptureFromScene()
        {
            if (HasFrozenResult || !IsConfigured || RoundManager.Instance == null ||
                DinoSpawner.Instance == null)
            {
                return;
            }

            IBattlefieldLayoutSignatureProvider[] providers = FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .OfType<IBattlefieldLayoutSignatureProvider>()
                .ToArray();
            List<BattlefieldLayoutSignature> capturedLayouts = new();
            foreach (IBattlefieldLayoutSignatureProvider provider in providers)
            {
                if (provider.TryCapture(out BattlefieldLayoutSignature candidate) && candidate != null)
                {
                    capturedLayouts.Add(candidate);
                }
            }
            if (capturedLayouts.Count != 1)
            {
                Debug.LogError("Cannot freeze the game result without one valid battlefield layout signature provider.");
                return;
            }
            BattlefieldLayoutSignature layoutSignature = capturedLayouts[0];

            DinoTrainingAgent rewardSummary = FindFirstObjectByType<DinoTrainingAgent>(FindObjectsInactive.Include);
            if (rewardSummary == null)
            {
                Debug.LogError("Cannot freeze the game result without the formal DinoTrainingAgent reward summary.");
                return;
            }

            GameResultSnapshot snapshot = new(
                LaunchRequest.MapId,
                LaunchRequest.TargetSceneStableId,
                LaunchRequest.LayoutSeed,
                LaunchRequest.Controller,
                RoundManager.Instance.PlayerWon,
                RoundManager.Instance.RunningElapsedSeconds,
                DinoSpawner.Instance.ResourcesToSpend,
                rewardSummary.LastCompletedEpisodeReward,
                layoutSignature.CanonicalText,
                LaunchRequest.ModelId,
                LaunchRequest.ModelHash,
                LaunchRequest.ProtocolVersion);
            TryFreeze(snapshot);
        }

        private void OnDisable()
        {
            if (IsSubscribed && RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange -= OnGameStateChange;
            }
            IsSubscribed = false;
        }
    }
}
