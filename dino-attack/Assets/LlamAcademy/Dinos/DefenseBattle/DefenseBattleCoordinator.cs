using System;
using LlamAcademy.Dinos.Inference;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Session;
using UnityEngine;

namespace LlamAcademy.Dinos.DefenseBattle
{
    [DefaultExecutionOrder(40), DisallowMultipleComponent]
    public sealed class DefenseBattleCoordinator : MonoBehaviour
    {
        [SerializeField] private DefensePlacementController Placement;

        private DinoSentisPolicyRunner ActiveRunner;
        private DefenseLayoutSnapshot FrozenLayout;
        private bool DefenderForfeited;

        public DefenseBattlePhase Phase { get; private set; } = DefenseBattlePhase.Setup;
        public DefenseAiStrategy SelectedStrategy { get; private set; } = DefenseAiStrategy.None;
        public GameInferenceLaunchOption PpoOption { get; private set; }
        public GameInferenceLaunchOption FpoOption { get; private set; }
        public GameInferenceLaunchOption PolicyFlowOption { get; private set; }
        public GameInferenceLaunchOption ReinFlowOption { get; private set; }
        public string StatusMessage { get; private set; } = string.Empty;
        public bool DefenderWon => RoundManager.Instance != null && RoundManager.Instance.HasSessionOutcome &&
                                   !RoundManager.Instance.PlayerWon;
        public bool IsRetryLayoutLocked => Placement != null && Placement.IsLocked && Phase == DefenseBattlePhase.Setup;
        public bool CanStart => Phase == DefenseBattlePhase.Setup && Placement != null && Placement.IsComplete &&
                                SelectedStrategy != DefenseAiStrategy.None && IsSelectedStrategyAvailable;
        public bool IsSelectedStrategyAvailable => GetOption(SelectedStrategy).IsAvailable;

        public event Action StateChanged;

        private void Awake()
        {
            Placement ??= FindFirstObjectByType<DefensePlacementController>(FindObjectsInactive.Include);
            PpoOption = ResolvePpoOption();
            FpoOption = ResolveFpoOption();
            var policyFlowConfig = DinoInferenceModelConfig.LoadPolicyFlow();
            PolicyFlowOption = DinoSentisPolicyRunner.TryValidateConfiguredModel(policyFlowConfig, out string reason)
                ? policyFlowConfig.ToLaunchOption() : GameInferenceLaunchOption.Unavailable(reason);
            ReinFlowOption = GameInferenceLaunchOption.Unavailable("尚未安装兼容的 ReinFlow 模型");
        }

        private void Start()
        {
            if (Placement != null)
            {
                Placement.StateChanged += HandlePlacementChanged;
                Placement.PlacementRejected += SetStatus;
            }
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange += HandleGameStateChanged;
            }
            StateChanged?.Invoke();
        }

        public void SelectStrategy(DefenseAiStrategy strategy)
        {
            if (Phase != DefenseBattlePhase.Setup)
            {
                return;
            }

            GameInferenceLaunchOption option = GetOption(strategy);
            if (!option.IsAvailable)
            {
                SelectedStrategy = DefenseAiStrategy.None;
                SetStatus(option.UnavailableReason);
                return;
            }

            SelectedStrategy = strategy;
            SetStatus(string.Empty);
        }

        public bool TryStartBattle()
        {
            string reason = string.Empty;
            if (!CanStart)
            {
                SetStatus(Placement.Inventory.BuildMissingMessage(SelectedStrategy != DefenseAiStrategy.None));
                return false;
            }
            if (!Placement.TryCreateSnapshot(out DefenseLayoutSnapshot snapshot, out reason))
            {
                SetStatus(reason);
                return false;
            }

            GameInferenceLaunchOption option = GetOption(SelectedStrategy);
            DinoInferenceModelConfig config = SelectedStrategy switch
            {
                DefenseAiStrategy.Ppo => DinoInferenceModelConfig.LoadDefault(),
                DefenseAiStrategy.Fpo => DinoInferenceModelConfig.LoadFpo(),
                DefenseAiStrategy.PolicyFlow => DinoInferenceModelConfig.LoadPolicyFlow(),
                _ => null,
            };
            DinoSentisPolicyRunner runner = null;
            if (!DinoSentisPolicyRunner.TryCreate(config, out runner, out reason))
            {
                runner?.Dispose();
                SelectedStrategy = DefenseAiStrategy.None;
                SetStatus(string.IsNullOrEmpty(reason) ? "所选 AI Strategy 当前不可用" : reason);
                return false;
            }

            GameSessionRestartService restart = GameSessionRestartService.Instance;
            int layoutSeed = restart == null ? snapshot.LayoutSeed : restart.CurrentStartSeed;
            GameLaunchRequest inferenceRequest = new(
                GameMapId.DefenseBattlefield,
                layoutSeed,
                GameControllerMode.InferenceAI,
                option.InferenceSeed,
                option.ModelId,
                option.ModelHash,
                option.ProtocolVersion,
                true,
                true);
            DinoInferenceController inference = DinoSpawner.Instance.GetComponent<DinoInferenceController>() ??
                                                DinoSpawner.Instance.gameObject.AddComponent<DinoInferenceController>();
            if (!inference.TryConfigureDeferredStart(inferenceRequest, config, runner, out reason))
            {
                runner.Dispose();
                Destroy(inference);
                SelectedStrategy = DefenseAiStrategy.None;
                SetStatus(reason);
                return false;
            }

            ActiveRunner = runner;
            FrozenLayout = snapshot;
            Placement.LockForBattle();
            DinoSpawner.Instance.SetPlayerInputEnabled(false);
            Phase = DefenseBattlePhase.Battle;
            SetStatus(string.Empty);
            RoundManager.Instance.StartRound();
            StateChanged?.Invoke();
            return true;
        }

        public void ExitSetupToMapSelect()
        {
            if (Phase != DefenseBattlePhase.Setup) return;
            DefenseBattleReplayContext.Clear();
            TrySchedule(GameLaunchContext.CreateMapSelectionRequest());
        }

        public void ForfeitBattle()
        {
            if (Phase != DefenseBattlePhase.Battle || RoundManager.Instance == null) return;
            DefenderForfeited = true;
            RoundManager.Instance.ForfeitDefenderSession();
        }

        public void RetrySameLayout()
        {
            if (Phase != DefenseBattlePhase.Result || FrozenLayout == null) return;
            DefenseBattleReplayContext.PublishRetry(FrozenLayout);
            if (!TrySchedule(GameLaunchRequest.CreateHuman(GameMapId.DefenseBattlefield, FrozenLayout.LayoutSeed)))
            {
                DefenseBattleReplayContext.Clear();
            }
        }

        public void CreateNewLayout()
        {
            if (Phase != DefenseBattlePhase.Result) return;
            DefenseBattleReplayContext.Clear();
            int currentSeed = GameSessionRestartService.Instance == null
                ? FrozenLayout?.LayoutSeed ?? 0
                : GameSessionRestartService.Instance.CurrentStartSeed;
            TrySchedule(GameLaunchRequest.CreateHuman(
                GameMapId.DefenseBattlefield,
                GameLaunchContext.DeriveNextLayoutSeed(currentSeed)));
        }

        public void ReturnToMapSelect()
        {
            DefenseBattleReplayContext.Clear();
            TrySchedule(GameLaunchContext.CreateMapSelectionRequest());
        }

        public string BuildMissingMessage() => Placement == null
            ? "布防组件不可用"
            : Placement.Inventory.BuildMissingMessage(SelectedStrategy != DefenseAiStrategy.None);

        public string StrategyDisplayName => SelectedStrategy switch
        {
            DefenseAiStrategy.Ppo => "PPO",
            DefenseAiStrategy.Fpo => "FPO",
            DefenseAiStrategy.PolicyFlow => "PolicyFlow",
            DefenseAiStrategy.ReinFlow => "ReinFlow",
            _ => "未选择",
        };

        private void HandlePlacementChanged() => StateChanged?.Invoke();

        private void HandleGameStateChanged(GameState oldState, GameState newState)
        {
            if (newState == GameState.Ended)
            {
                Phase = DefenseBattlePhase.Result;
                SetStatus(DefenderForfeited ? "已退出战斗，本局判定失败" : string.Empty);
            }
        }

        private GameInferenceLaunchOption GetOption(DefenseAiStrategy strategy) => strategy switch
        {
            DefenseAiStrategy.Ppo => PpoOption,
            DefenseAiStrategy.Fpo => FpoOption,
            DefenseAiStrategy.PolicyFlow => PolicyFlowOption,
            DefenseAiStrategy.ReinFlow => ReinFlowOption,
            _ => GameInferenceLaunchOption.Unavailable("请选择 AI Strategy"),
        };

        private void SetStatus(string value)
        {
            StatusMessage = value ?? string.Empty;
            StateChanged?.Invoke();
        }

        private bool TrySchedule(GameLaunchRequest request)
        {
            GameSessionRestartService service = GameSessionRestartService.Instance;
            bool accepted = service != null && service.TryScheduleLaunch(request);
            if (!accepted) SetStatus("目标场景当前不可用");
            return accepted;
        }

        private static GameInferenceLaunchOption ResolvePpoOption()
        {
            DinoInferenceModelConfig config = DinoInferenceModelConfig.LoadDefault();
            return DinoSentisPolicyRunner.TryValidateConfiguredModel(config, out string reason)
                ? config.ToLaunchOption()
                : GameInferenceLaunchOption.Unavailable(reason);
        }

        private static GameInferenceLaunchOption ResolveFpoOption()
        {
            DinoInferenceModelConfig config = DinoInferenceModelConfig.LoadFpo();
            return DinoSentisPolicyRunner.TryValidateConfiguredModel(config, out string reason)
                ? config.ToLaunchOption()
                : GameInferenceLaunchOption.Unavailable(reason);
        }

        private void OnDestroy()
        {
            if (Placement != null)
            {
                Placement.StateChanged -= HandlePlacementChanged;
                Placement.PlacementRejected -= SetStatus;
            }
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange -= HandleGameStateChanged;
            }
            ActiveRunner?.Dispose();
            ActiveRunner = null;
        }
    }
}
