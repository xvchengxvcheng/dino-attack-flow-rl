using System;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.UI
{
    [DisallowMultipleComponent]
    public sealed class GameResultActionController : MonoBehaviour
    {
        public event Action<GameResultActionRoute> RouteResolved;

        private Button RetryButton;
        private Button NewLayoutButton;
        private Button AiStrategyButton;
        private Button SelectMapButton;
        private Button PpoStrategyButton;
        private Button FpoStrategyButton;
        private Button ReinFlowStrategyButton;
        private Button PolicyFlowStrategyButton;
        private GameInferenceLaunchOption PolicyFlowInference;
        private Button CloseStrategyButton;
        private VisualElement ResultActionsContainer;
        private VisualElement StrategyPanel;
        private Label AiUnavailableReason;
        private GameLaunchRequest CurrentRequest;
        private GameResultSnapshot? PlayerResult;
        private GameInferenceLaunchOption Inference;
        private GameInferenceLaunchOption FpoInference;
        private GameInferenceLaunchOption ReinFlowInference;
        private GameSessionRestartService RestartService;
        private bool IsBound;
        private bool IsActionPending;

        public void Configure(
            VisualElement root,
            GameLaunchRequest currentRequest,
            GameResultSnapshot? playerResult,
            GameInferenceLaunchOption inference,
            GameSessionRestartService restartService)
        {
            Unbind();
            CurrentRequest = currentRequest;
            PlayerResult = playerResult;
            Inference = inference;
            RestartService = restartService;
            IsActionPending = false;

            RetryButton = root?.Q<Button>("restart-button");
            NewLayoutButton = root?.Q<Button>("new-layout-button");
            AiStrategyButton = root?.Q<Button>("ai-strategy-button");
            SelectMapButton = root?.Q<Button>("select-map-button");
            PpoStrategyButton = root?.Q<Button>("ppo-strategy-button");
            FpoStrategyButton = root?.Q<Button>("fpo-strategy-button");
            ReinFlowStrategyButton = root?.Q<Button>("reinflow-strategy-button");
            CloseStrategyButton = root?.Q<Button>("close-strategy-button");
            PolicyFlowStrategyButton = root?.Q<Button>("policyflow-strategy-button");
            if (PolicyFlowStrategyButton == null && FpoStrategyButton?.parent != null)
            {
                PolicyFlowStrategyButton = new Button { name = "policyflow-strategy-button", text = "PolicyFlow" };
                foreach (string styleClass in FpoStrategyButton.GetClasses())
                    PolicyFlowStrategyButton.AddToClassList(styleClass);
                FpoStrategyButton.parent.Insert(FpoStrategyButton.parent.IndexOf(FpoStrategyButton) + 1, PolicyFlowStrategyButton);
            }
            var policyFlowConfig = LlamAcademy.Dinos.Inference.DinoInferenceModelConfig.LoadPolicyFlow();
            PolicyFlowInference = LlamAcademy.Dinos.Inference.DinoSentisPolicyRunner.TryValidateConfiguredModel(policyFlowConfig, out string policyFlowReason)
                ? policyFlowConfig.ToLaunchOption() : GameInferenceLaunchOption.Unavailable(policyFlowReason);
            ResultActionsContainer = root?.Q("result-actions");
            StrategyPanel = root?.Q("strategy-panel");
            AiUnavailableReason = root?.Q<Label>("ai-strategy-reason");

            if (RetryButton == null || NewLayoutButton == null ||
                AiStrategyButton == null || SelectMapButton == null ||
                PpoStrategyButton == null || FpoStrategyButton == null ||
                ReinFlowStrategyButton == null || CloseStrategyButton == null ||
                ResultActionsContainer == null || StrategyPanel == null)
            {
                Debug.LogError("The result panel is missing one or more required action buttons.");
                return;
            }

            FpoInference = GameInferenceLaunchOption.Unavailable("A compatible FPO model is not installed yet.");
            ReinFlowInference = GameInferenceLaunchOption.Unavailable("A compatible ReinFlow model is not installed yet.");
            SetStrategyPanelVisible(false);
            Bind();
            SetEndedInteractable(true);
        }

        public void SetStrategyOptions(
            GameInferenceLaunchOption ppo,
            GameInferenceLaunchOption fpo,
            GameInferenceLaunchOption reinFlow)
        {
            Inference = ppo;
            FpoInference = fpo;
            ReinFlowInference = reinFlow;
            SetEndedInteractable(true);
        }

        public void SetEndedInteractable(bool interactable)
        {
            bool ready = interactable && !IsActionPending &&
                         (RestartService == null || !RestartService.IsReloadPending);
            RetryButton?.SetEnabled(ready);
            NewLayoutButton?.SetEnabled(ready);
            SelectMapButton?.SetEnabled(ready);
            AiStrategyButton?.SetEnabled(ready);
            PpoStrategyButton?.SetEnabled(ready && Inference.IsAvailable);
            FpoStrategyButton?.SetEnabled(ready && FpoInference.IsAvailable);
            ReinFlowStrategyButton?.SetEnabled(ready && ReinFlowInference.IsAvailable);
            CloseStrategyButton?.SetEnabled(ready);
            PolicyFlowStrategyButton?.SetEnabled(ready && PolicyFlowInference.IsAvailable);
            if (PpoStrategyButton != null)
            {
                PpoStrategyButton.text = Inference.IsAvailable ? "PPO" : "PPO · unavailable";
            }
            if (FpoStrategyButton != null)
            {
                FpoStrategyButton.text = FpoInference.IsAvailable ? "FPO" : "FPO · unavailable";
            }
            if (ReinFlowStrategyButton != null)
            {
                ReinFlowStrategyButton.text = "ReinFlow";
            }

            if (!interactable)
            {
                SetStrategyPanelVisible(false);
                ShowStatus(string.Empty);
            }
        }

        public void SetPlayerResult(GameResultSnapshot? playerResult)
        {
            PlayerResult = playerResult.HasValue &&
                           playerResult.Value.Controller == GameControllerMode.Human
                ? playerResult
                : null;
        }

        private void RetryCurrentLayout() => Execute(GameResultAction.RetryCurrentLayout);
        private void GenerateNewLayout() => Execute(GameResultAction.GenerateNewLayout);
        private void OpenStrategyPicker()
        {
            if (IsActionPending || (RestartService != null && RestartService.IsReloadPending))
            {
                return;
            }

            SetStrategyPanelVisible(true);
            ShowStatus(BuildStrategyAvailabilityMessage());
        }

        private void CloseStrategyPicker()
        {
            SetStrategyPanelVisible(false);
            ShowStatus(string.Empty);
        }

        private void ReplayWithPpo() => Execute(GameResultAction.ReplayWithAi, Inference);
        private void ReplayWithFpo() => Execute(GameResultAction.ReplayWithAi, FpoInference);
        private void ReplayWithReinFlow() => Execute(GameResultAction.ReplayWithAi, ReinFlowInference);
        private void ReplayWithPolicyFlow() => Execute(GameResultAction.ReplayWithAi, PolicyFlowInference);
        private void SelectMap() => Execute(GameResultAction.SelectMap);

        private void Execute(GameResultAction action) => Execute(action, Inference);

        private void Execute(GameResultAction action, GameInferenceLaunchOption selectedInference)
        {
            if (IsActionPending || (RestartService != null && RestartService.IsReloadPending))
            {
                return;
            }

            GameResultActionRoute route = GameResultActionRouter.Resolve(
                action,
                CurrentRequest,
                selectedInference);
            RouteResolved?.Invoke(route);
            if (!route.IsAvailable)
            {
                ShowStatus(route.UnavailableReason);
                SetEndedInteractable(true);
                return;
            }

            if (RestartService == null)
            {
                ShowStatus("Game launch service is unavailable.");
                SetEndedInteractable(true);
                return;
            }

            IsActionPending = true;
            SetEndedInteractable(false);
            GameResultSnapshot? comparison = route.PreservePlayerResult ? PlayerResult : null;
            if (RestartService.TryScheduleLaunch(route.Request, comparison))
            {
                ShowStatus("Loading…");
                return;
            }

            IsActionPending = false;
            ShowStatus("This destination is not available yet.");
            SetEndedInteractable(true);
        }

        private string BuildStrategyAvailabilityMessage()
        {
            if (Inference.IsAvailable && !FpoInference.IsAvailable && !ReinFlowInference.IsAvailable && !PolicyFlowInference.IsAvailable)
            {
                return "PPO is ready. FPO and ReinFlow models are not installed yet.";
            }

            if (!Inference.IsAvailable && !FpoInference.IsAvailable && !ReinFlowInference.IsAvailable && !PolicyFlowInference.IsAvailable)
            {
                return "No compatible trained AI model is available.";
            }

            return "Choose an available trained strategy.";
        }

        private void SetStrategyPanelVisible(bool visible)
        {
            if (StrategyPanel != null)
            {
                StrategyPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (ResultActionsContainer != null)
            {
                ResultActionsContainer.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void ShowStatus(string message)
        {
            if (AiUnavailableReason == null)
            {
                return;
            }

            AiUnavailableReason.text = message ?? string.Empty;
            AiUnavailableReason.style.display = string.IsNullOrEmpty(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private void OnEnable() => Bind();

        private void OnDisable() => Unbind();

        private void Bind()
        {
            if (IsBound || RetryButton == null || NewLayoutButton == null ||
                AiStrategyButton == null || SelectMapButton == null ||
                PpoStrategyButton == null || FpoStrategyButton == null ||
                ReinFlowStrategyButton == null || CloseStrategyButton == null)
            {
                return;
            }

            RetryButton.clicked += RetryCurrentLayout;
            NewLayoutButton.clicked += GenerateNewLayout;
            AiStrategyButton.clicked += OpenStrategyPicker;
            SelectMapButton.clicked += SelectMap;
            PpoStrategyButton.clicked += ReplayWithPpo;
            FpoStrategyButton.clicked += ReplayWithFpo;
            ReinFlowStrategyButton.clicked += ReplayWithReinFlow;
            CloseStrategyButton.clicked += CloseStrategyPicker;
            if (PolicyFlowStrategyButton != null) PolicyFlowStrategyButton.clicked += ReplayWithPolicyFlow;
            IsBound = true;
        }

        private void Unbind()
        {
            if (!IsBound)
            {
                return;
            }

            RetryButton.clicked -= RetryCurrentLayout;
            NewLayoutButton.clicked -= GenerateNewLayout;
            AiStrategyButton.clicked -= OpenStrategyPicker;
            SelectMapButton.clicked -= SelectMap;
            PpoStrategyButton.clicked -= ReplayWithPpo;
            FpoStrategyButton.clicked -= ReplayWithFpo;
            ReinFlowStrategyButton.clicked -= ReplayWithReinFlow;
            CloseStrategyButton.clicked -= CloseStrategyPicker;
            if (PolicyFlowStrategyButton != null) PolicyFlowStrategyButton.clicked -= ReplayWithPolicyFlow;
            IsBound = false;
        }
    }
}
