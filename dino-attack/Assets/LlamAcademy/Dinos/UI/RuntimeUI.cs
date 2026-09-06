using System.Collections;
using System.Collections.Generic;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.Inference;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class RuntimeUI : MonoBehaviour
    {
        [SerializeField] private DinoSO[] Dinos;
        [SerializeField] private ResourceSO[] ResourceSOs;
        [SerializeField] private DinoDeploymentZonePresenter ZonePresenter;

        [SerializeField] private VisualTreeAsset DinoTemplate;
        [SerializeField] private VisualTreeAsset ResourceTemplate;

        private UIDocument Document;
        private GameResultActionController ResultActions;
        private GameResultCollector ResultCollector;
        private GameComparisonPresenter ComparisonPresenter;
        private Dictionary<DinoSO, VisualElement> DinoToButtonDictionary = new();
        private int LastKnownFood = int.MinValue;
        private Button RegisteredStartButton;
        private Button RegisteredForfeitButton;
        private GameLaunchRequest LaunchRequest;
        private GameResultSnapshot? PreviousPlayerResult;
        private GameInferenceLaunchOption PpoInference;
        private GameInferenceLaunchOption FpoInference;
        private GameSessionRestartService RestartService;
        private Button ForfeitButton => Document.rootVisualElement.Q<Button>("forfeit-button");

        private Button StartButton => Document.rootVisualElement.Q<Button>("start-button");
        private Label WaveText => Document.rootVisualElement.Q<Label>("wave-text");
        private VisualElement DinoButtonContainer => Document.rootVisualElement.Q("dino-button-container");
        private VisualElement ResourcesContainer => Document.rootVisualElement.Q("resources-container");
        private Label RoundEndText => Document.rootVisualElement.Q<Label>("win-lose-text");
        private Label DeploymentFeedback => Document.rootVisualElement.Q<Label>("deployment-feedback");
        private Label TerrainFeedback => Document.rootVisualElement.Q<Label>("terrain-feedback");
        private Label PhaseStatus => Document.rootVisualElement.Q<Label>("phase-status");
        private Label FoodStatus => Document.rootVisualElement.Q<Label>("food-status");
        private Label ThreatStatus => Document.rootVisualElement.Q<Label>("threat-status");
        private VisualElement ResultPanel => Document.rootVisualElement.Q("result-panel");
        private Label SessionSummary => Document.rootVisualElement.Q<Label>("session-summary");

        private void Awake()
        {
            Document = GetComponent<UIDocument>();
            ResultActions = GetComponent<GameResultActionController>() ??
                            gameObject.AddComponent<GameResultActionController>();
            ResultCollector = GetComponent<GameResultCollector>() ??
                              gameObject.AddComponent<GameResultCollector>();
            ComparisonPresenter = GetComponent<GameComparisonPresenter>() ??
                                  gameObject.AddComponent<GameComparisonPresenter>();
            RestartService = GameSessionRestartService.Instance;
            LaunchRequest = RestartService != null
                ? RestartService.CurrentLaunchRequest
                : default;
            PreviousPlayerResult = RestartService?.PreviousPlayerResult;
            PpoInference = ResolveInferenceLaunchOption();
            FpoInference = ResolveFpoInferenceLaunchOption();
            ResultCollector.Configure(LaunchRequest, PreviousPlayerResult);
            ResultCollector.ResultFrozen += HandleResultFrozen;
            ResultCollector.ComparisonReady += ComparisonPresenter.PresentComparison;
            ResultCollector.ComparisonRejected += ComparisonPresenter.PresentRejected;
            BindCurrentVisualTree();
        }

        private IEnumerator Start()
        {
            RoundManager.Instance.OnGameStateChange += OnGameStateChange;
            DinoSpawner.Instance.OnSpawnDino += HandleDinoSpawnOrDeath;
            DinoSpawner.Instance.OnDinoDeath += HandleDinoSpawnOrDeath;
            DinoSpawner.Instance.OnDeploymentEvaluated += HandleDeploymentEvaluated;
            if (ZonePresenter != null)
            {
                ZonePresenter.HoverChanged += HandleZoneHoverChanged;
            }
            // UIDocument can rebuild its visual tree after this component's Awake.
            // Rebind every callback and presenter to the live tree after that rebuild.
            OnGameStateChange(RoundManager.Instance.State, RoundManager.Instance.State);
            yield return null;
            BindCurrentVisualTree();
            OnGameStateChange(RoundManager.Instance.State, RoundManager.Instance.State);
            bool restartLaunch = GameSessionRestartService.Instance != null &&
                                 GameSessionRestartService.Instance.IsRestartLaunch;
            SetOpeningVisible(!restartLaunch && RoundManager.Instance.State == GameState.Setup);
        }

        private void BindCurrentVisualTree()
        {
            RegisteredStartButton?.UnregisterCallback<ClickEvent>(HandleStartClick);
            if (RegisteredForfeitButton != null)
            {
                RegisteredForfeitButton.clicked -= HandleForfeitClick;
            }

            VisualElement root = Document.rootVisualElement;
            RegisteredStartButton = root.Q<Button>("start-button");
            RegisteredForfeitButton = root.Q<Button>("forfeit-button");
            if (RegisteredStartButton == null || RegisteredForfeitButton == null)
            {
                Debug.LogError("Runtime UI is missing the Start or Forfeit button in its live visual tree.");
                return;
            }

            RegisteredStartButton.RegisterCallback<ClickEvent>(HandleStartClick);
            RegisteredForfeitButton.clicked += HandleForfeitClick;

            ResultActions.Configure(
                root,
                LaunchRequest,
                PreviousPlayerResult,
                PpoInference,
                RestartService);
            ResultActions.SetStrategyOptions(
                PpoInference,
                FpoInference,
                GameInferenceLaunchOption.Unavailable("A compatible ReinFlow model is not installed yet."));
            ComparisonPresenter.Configure(root);
            ResultActions.SetEndedInteractable(
                RoundManager.Instance != null && RoundManager.Instance.State == GameState.Ended);

            ResourcesContainer.Clear();
            DinoButtonContainer.Clear();
            DinoToButtonDictionary.Clear();
            foreach (ResourceSO resourceSO in ResourceSOs)
            {
                VisualElement resourceElement = new();
                resourceElement.style.width = 225;
                ResourceTemplate.CloneTree(resourceElement);
                resourceElement.dataSource = resourceSO;
                ResourcesContainer.Add(resourceElement);
            }

            foreach (DinoSO dino in Dinos)
            {
                VisualElement dinoElement = new();
                dinoElement.AddToClassList("dino-button-container");
                DinoTemplate.CloneTree(dinoElement);
                dinoElement.dataSource = dino;
                dinoElement.RegisterCallback<ClickEvent, DinoSO>(HandleClickDino, dino);
                DinoButtonContainer.Add(dinoElement);
                DinoToButtonDictionary.Add(dino, dinoElement);
            }

            ResultPanel.style.display = RoundManager.Instance != null &&
                                        RoundManager.Instance.State == GameState.Ended
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            LastKnownFood = int.MinValue;
        }

        private void Update()
        {
            if (DinoSpawner.Instance != null && RoundManager.Instance != null &&
                LastKnownFood != DinoSpawner.Instance.ResourcesToSpend)
            {
                int previousFood = LastKnownFood == int.MinValue ? DinoSpawner.Instance.ResourcesToSpend : LastKnownFood;
                LastKnownFood = DinoSpawner.Instance.ResourcesToSpend;
                FoodStatus.text = RuntimeUIStateFormatter.FormatFood(LastKnownFood, LastKnownFood - previousFood);
                SetDinoButtonStates(RoundManager.Instance.State);
            }

            if (RoundManager.Instance != null)
            {
                ThreatStatus.text = RuntimeUIStateFormatter.FormatPressure(RoundManager.Instance.AliveDefenderCount, 0);
            }

            if (DinoSpawner.Instance == null || !DinoSpawner.Instance.PlayerInputEnabled || Keyboard.current == null)
            {
                return;
            }

            foreach (DinoSO dino in Dinos)
            {
                if (Keyboard.current[dino.Hotkey].wasReleasedThisFrame && DinoToButtonDictionary[dino].enabledSelf)
                {
                    SelectDino(dino);
                    AnimateClick(DinoToButtonDictionary[dino]);
                }
            }
        }

        private void OnDisable()
        {
            RegisteredStartButton?.UnregisterCallback<ClickEvent>(HandleStartClick);
            if (RegisteredForfeitButton != null)
            {
                RegisteredForfeitButton.clicked -= HandleForfeitClick;
            }
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange -= OnGameStateChange;
            }
            if (DinoSpawner.Instance != null)
            {
                DinoSpawner.Instance.OnSpawnDino -= HandleDinoSpawnOrDeath;
                DinoSpawner.Instance.OnDinoDeath -= HandleDinoSpawnOrDeath;
                DinoSpawner.Instance.OnDeploymentEvaluated -= HandleDeploymentEvaluated;
            }
            if (ZonePresenter != null)
            {
                ZonePresenter.HoverChanged -= HandleZoneHoverChanged;
            }
            if (ResultCollector != null)
            {
                ResultCollector.ResultFrozen -= HandleResultFrozen;
                if (ComparisonPresenter != null)
                {
                    ResultCollector.ComparisonReady -= ComparisonPresenter.PresentComparison;
                    ResultCollector.ComparisonRejected -= ComparisonPresenter.PresentRejected;
                }
            }
        }

        private void HandleResultFrozen(GameResultSnapshot snapshot)
        {
            if (snapshot.Controller == GameControllerMode.Human)
            {
                ResultActions.SetPlayerResult(snapshot);
                ComparisonPresenter.Hide();
            }
        }

        private void HandleDinoSpawnOrDeath(Unit.Unit _)
        {
            SetStartButtonInteractable(RoundManager.Instance.State);
            SetDinoButtonStates(RoundManager.Instance.State);
            ThreatStatus.text = RuntimeUIStateFormatter.FormatPressure(RoundManager.Instance.AliveDefenderCount, 0);
        }

        private void SetDinoButtonStates(GameState currentGameState)
        {
            foreach (KeyValuePair<DinoSO, VisualElement> keyValuePair in DinoToButtonDictionary)
            {
                bool interactive = currentGameState == GameState.Setup || currentGameState == GameState.Running;
                keyValuePair.Value.SetEnabled(interactive && keyValuePair.Key.Cost <= DinoSpawner.Instance.ResourcesToSpend);
            }
        }

        private void OnGameStateChange(GameState oldState, GameState newState)
        {
            SetStartButtonInteractable(newState);
            SetDinoButtonStates(newState);
            SetForfeitVisible(newState == GameState.Running);
            PhaseStatus.text = RuntimeUIStateFormatter.FormatPhase(RoundManager.Instance.Round, newState.ToString());

            if (newState == GameState.Ending)
            {
                ComparisonPresenter.Hide();
                TerrainFeedback.text = string.Empty;
                DeploymentFeedback.text = string.Empty;
                DeploymentFeedback.RemoveFromClassList("deployment-error");
                SetOpeningVisible(false);
                ResultPanel.style.display = DisplayStyle.None;
                ResultActions.SetEndedInteractable(false);
            }
            else if (newState == GameState.Ended)
            {
                SetOpeningVisible(false);
                RoundEndText.text = RoundManager.Instance.PlayerWon
                    ? "<color=#58d26f>Victory</color>"
                    : "<color=#e05a4f>Defeat</color>";
                RoundEndText.RemoveFromClassList("hidden");
                RoundEndText.AddToClassList("visible");
                ResultPanel.style.display = DisplayStyle.Flex;
                SessionSummary.text =
                    $"Houses destroyed {RoundManager.Instance.DestroyedHouseCount} · " +
                    $"Meat earned {RoundManager.Instance.HouseRewardMeat} · " +
                    $"Dinos alive {RoundManager.Instance.AliveDinos}";
                ResultActions.SetEndedInteractable(true);
                WaveText.text = "Session · Complete";
            }
            else if (newState == GameState.Setup)
            {
                ComparisonPresenter.Hide();
                ResultPanel.style.display = DisplayStyle.None;
                ResultActions.SetEndedInteractable(false);
                bool restartLaunch = GameSessionRestartService.Instance != null &&
                                     GameSessionRestartService.Instance.IsRestartLaunch;
                SetOpeningVisible(!restartLaunch);
                WaveText.text = "Session · Setup";
            }
            else if (newState == GameState.Running)
            {
                ComparisonPresenter.Hide();
                SetOpeningVisible(false);
                ResultPanel.style.display = DisplayStyle.None;
                ResultActions.SetEndedInteractable(false);
                WaveText.text = "Session · Battle";
            }
            ThreatStatus.text = RuntimeUIStateFormatter.FormatPressure(RoundManager.Instance.AliveDefenderCount, 0);
        }

        private static GameInferenceLaunchOption ResolveInferenceLaunchOption()
        {
            DinoInferenceModelConfig config = DinoInferenceModelConfig.LoadDefault();
            return DinoSentisPolicyRunner.TryValidateConfiguredModel(config, out string reason)
                ? config.ToLaunchOption()
                : GameInferenceLaunchOption.Unavailable(reason);
        }

        private static GameInferenceLaunchOption ResolveFpoInferenceLaunchOption()
        {
            DinoInferenceModelConfig config = DinoInferenceModelConfig.LoadFpo();
            return DinoSentisPolicyRunner.TryValidateConfiguredModel(config, out string reason)
                ? config.ToLaunchOption()
                : GameInferenceLaunchOption.Unavailable(reason);
        }

        private void HandleDeploymentEvaluated(DeploymentResult result)
        {
            DeploymentFeedback.text = result.Allowed ? "✓ Deployed" : RuntimeUIStateFormatter.FormatDeploymentFailure(result.FailureReason);
            DeploymentFeedback.EnableInClassList("deployment-error", !result.Allowed);
            SetDinoButtonStates(RoundManager.Instance.State);
        }

        private void HandleZoneHoverChanged(DeploymentHoverInfo? hover)
        {
            TerrainFeedback.text = hover.HasValue
                ? RuntimeUIStateFormatter.FormatTerrain(
                    hover.Value.DisplayName,
                    SurfaceName(hover.Value.DominantSurface),
                    TerrainSpeedProfile.GetMultiplier(hover.Value.DominantSurface))
                : string.Empty;
        }

        private static string SurfaceName(TerrainSurfaceKind surface) => surface switch
        {
            TerrainSurfaceKind.StoneRoad => "Stone Road",
            TerrainSurfaceKind.ShallowWater => "Shallow Water",
            _ => surface.ToString()
        };

        private void SetStartButtonInteractable(GameState state) => StartButton.SetEnabled(state == GameState.Setup);

        private void HandleStartClick(ClickEvent evt)
        {
            AnimateClick(evt.target as VisualElement);
            RoundManager.Instance.StartRound();
            SetOpeningVisible(false);
        }

        private void HandleForfeitClick()
        {
            if (RoundManager.Instance == null || RoundManager.Instance.State != GameState.Running)
            {
                return;
            }

            AnimateClick(RegisteredForfeitButton);
            RoundManager.Instance.ForfeitSession();
        }

        private void SetForfeitVisible(bool visible)
        {
            if (ForfeitButton != null)
            {
                ForfeitButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                ForfeitButton.SetEnabled(visible);
            }
        }

        private void SetOpeningVisible(bool visible)
        {
            StartButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleClickDino(ClickEvent evt, DinoSO dino)
        {
            SelectDino(dino);
        }

        private void SelectDino(DinoSO dino)
        {
            DinoSpawner.Instance.ChangeActiveDino(dino);
        }

        private void HandleMouseUpOnDino(MouseUpEvent evt)
        {
            VisualElement element = evt.currentTarget as VisualElement;
            element.RemoveFromClassList("out");
            element.AddToClassList("in");
            evt.StopPropagation();
        }

        private void HandleMouseDownOnDino(MouseDownEvent evt)
        {
            VisualElement element = evt.currentTarget as VisualElement;
            element.RemoveFromClassList("in");
            element.AddToClassList("out");
            evt.StopPropagation();
        }

        private void AnimateClick(VisualElement element)
        {
            element.AddToClassList("out");
            element.RemoveFromClassList("in");
            element.RegisterCallback<TransitionEndEvent>(HandlePressTransitionComplete);
        }

        private void HandlePressTransitionComplete(TransitionEndEvent evt)
        {
            if (!evt.AffectsProperty("scale")) return;

            VisualElement element = evt.target as VisualElement;
            element.RemoveFromClassList("out");
            element.AddToClassList("in");
            element.UnregisterCallback<TransitionEndEvent>(HandlePressTransitionComplete);
        }
    }
}
