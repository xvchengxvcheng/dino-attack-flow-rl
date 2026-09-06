using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.Simulation;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.UI;
using LlamAcademy.Dinos.Unit;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.RoundManagement
{
    public enum SessionOutcomeReason
    {
        None,
        TargetRadiusEntered,
        SessionTimeout,
        ResourceExhaustion,
        ManualVictory,
        ManualDefeat
    }

    [DefaultExecutionOrder(0)]
    public partial class RoundManager : MonoBehaviour
    {
        public const float SessionDurationSeconds = 50f;
        private const int MinimumDeployableDinoFood = 25;

        public static RoundManager Instance { get; private set; }

        public int Round { get; private set; }
        [field: SerializeField] public int[] ResourcesPerRound { get; private set; } = new []
        {
            0,
            100,
            200,
            500,
            1000,
            2500,
            5000,
            10_000,
            25_000,
            50_000,
            100_000,
            250_000
        };
        [field: SerializeField] public Transform DinoTarget { get; private set; }
        [SerializeField] private GameState _State;
        public GameState State
        {
            get => _State;
            private set
            {
                if (_State == value)
                {
                    return;
                }
                GameState oldState = _State;
                _State = value;
                OnGameStateChange?.Invoke(oldState, value);
            }
        }

        [SerializeField] private AttackRadius EggRadius;
        public int AliveDinos => ActiveDinos.Count;
        public int AliveDefenderCount => AliveDefenders.Count;
        [SerializeField] private ResourceSO DinoSupplyResource;
        [SerializeField] private VillageHouse[] Houses = System.Array.Empty<VillageHouse>();
        public IReadOnlyList<VillageHouse> SessionHouses => Houses;
        public IReadOnlyList<Dino> ActiveDinoUnits => ActiveDinos;
        public float RunningElapsedSeconds => RunningElapsedSecondsValue;
        public int DestroyedHouseCount => HouseRewards.RewardedHouseCount;
        public int HouseRewardMeat => HouseRewards.TotalMeatAwarded;
        public bool HasSessionOutcome { get; private set; }
        public bool PlayerWon { get; private set; }
        public SessionOutcomeReason OutcomeReason { get; private set; }
        public bool NavigationStartPending { get; private set; }
        public string NavigationReadinessFailure { get; private set; } = string.Empty;

        public delegate void GameStateChangeEvent(GameState oldState, GameState newState);
        public event GameStateChangeEvent OnGameStateChange;

        private List<Dino> ActiveDinos = new();
        private List<Defender> AliveDefenders = new();
        private readonly HashSet<int> RewardedDefenderDeaths = new();
        private readonly HashSet<int> RewardedWallDeaths = new();
        private readonly HouseRewardLedger HouseRewards = new();
        private bool IsEndingRound;
        private float RunningElapsedSecondsValue;
        private long RunningFixedTickCount;
        private SessionOutcomeReason PendingOutcomeReason;
        private IDamageable PendingOutcomeTrigger;

        public void ConfigureLayeredSession(Transform dinoTarget, VillageHouse[] houses, int initialResources)
        {
            DinoTarget = dinoTarget;
            EggRadius = dinoTarget == null ? null : dinoTarget.GetComponentInChildren<AttackRadius>(true);
            Houses = houses ?? System.Array.Empty<VillageHouse>();
            if (ResourcesPerRound == null || ResourcesPerRound.Length == 0)
            {
                ResourcesPerRound = new[] { Mathf.Max(0, initialResources) };
            }
            else
            {
                ResourcesPerRound[0] = Mathf.Max(0, initialResources);
            }
        }

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogError($"Multiple RoundManagers detected. Deleting the second one {name}");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            EggRadius.OnTargetEnter += HandleDinoEnterEggRadius;
            DinoSupplyResource.Amount = 0;
        }

        private IEnumerator Start()
        {
            DinoSpawner.Instance.OnSpawnDino += OnSpawnDino;
            DinoSpawner.Instance.OnDinoDeath += OnDinoDeath;
            EnemyAIController.Instance.OnSpawnDefender += OnSpawnDefender;
            EnemyAIController.Instance.OnDefenderDeath += OnDefenderDeath;
            EnemyAIController.Instance.OnSpawnWall += OnSpawnWall;
            EnemyAIController.Instance.OnWallDeath += OnWallDeath;
            RegisterHouses();
            Round = 1;
            AddRoundResources();

            yield return null;
            if (State != GameState.Running)
            {
                State = GameState.Setup;
            }
            if (GameSessionRestartService.Instance != null &&
                GameSessionRestartService.Instance.IsRestartLaunch &&
                GameSessionRestartService.Instance.CurrentLaunchRequest.MapId != GameMapId.DefenseBattlefield)
            {
                StartRound();
            }
        }

        private void OnDestroy()
        {
            if (EggRadius != null)
            {
                EggRadius.OnTargetEnter -= HandleDinoEnterEggRadius;
            }

            if (DinoSpawner.Instance != null)
            {
                DinoSpawner.Instance.OnSpawnDino -= OnSpawnDino;
                DinoSpawner.Instance.OnDinoDeath -= OnDinoDeath;
            }

            if (EnemyAIController.Instance != null)
            {
                EnemyAIController.Instance.OnSpawnDefender -= OnSpawnDefender;
                EnemyAIController.Instance.OnDefenderDeath -= OnDefenderDeath;
                EnemyAIController.Instance.OnSpawnWall -= OnSpawnWall;
                EnemyAIController.Instance.OnWallDeath -= OnWallDeath;
            }

            foreach (VillageHouse house in Houses)
            {
                if (house != null)
                {
                    house.OnDeath -= OnHouseDeath;
                }
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void RegisterHouses()
        {
            if (Houses == null || Houses.Length == 0)
            {
                Houses = FindObjectsByType<VillageHouse>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }

            foreach (VillageHouse house in Houses)
            {
                if (house == null)
                {
                    continue;
                }

                house.OnDeath -= OnHouseDeath;
                house.OnDeath += OnHouseDeath;
            }
        }

        private void OnHouseDeath(IDamageable damageable)
        {
            if (State != GameState.Running
                || damageable is not VillageHouse house
                || !HouseRewards.TryRecord(house.StableHouseId))
            {
                return;
            }

            DinoSpawner.Instance.ResourcesToSpend += HouseRewardLedger.MeatPerHouse;
        }

        public void StartRound()
        {
            if (State != GameState.Setup)
            {
                return;
            }

            NavigationStartPending = true;
            TryStartNavigationReadyRound();
        }

        private void FixedUpdate()
        {
            if (State == GameState.Setup && NavigationStartPending)
            {
                TryStartNavigationReadyRound();
                return;
            }
            if (State != GameState.Running)
            {
                return;
            }

            RunningFixedTickCount++;
            RunningElapsedSecondsValue = RunningFixedTickCount * Time.fixedDeltaTime;
            if (RunningFixedTickCount >= DinoFixedStepTime.SecondsToTicks(SessionDurationSeconds))
            {
                BeginEndingWithReason(false, SessionOutcomeReason.SessionTimeout, null);
            }
        }

        private void TryStartNavigationReadyRound()
        {
            if (!NavigationStartPending || State != GameState.Setup)
            {
                return;
            }
            if (!DinoEpisodeNavigationReadinessGate.IsReady(
                    this,
                    AliveDefenders,
                    ActiveDinos,
                    out string failureReason))
            {
                NavigationReadinessFailure = failureReason ?? string.Empty;
                return;
            }

            NavigationStartPending = false;
            NavigationReadinessFailure = string.Empty;
            RunningElapsedSecondsValue = 0f;
            RunningFixedTickCount = 0L;
            State = GameState.Running;
            foreach (Dino dino in ActiveDinos)
            {
                dino.SetDestination(DinoTarget.position);
            }
        }

        public void CompleteEnemySetup()
        {
            // Formal Dinos sessions have one setup and one battle. Historical round repair is disabled.
        }

        private void AddRoundResources()
        {
            DinoSpawner.Instance.ResourcesToSpend += ResourcesPerRound[Round - 1];
            EnemyAIController.Instance.ResourcesToSpend += Mathf.CeilToInt(ResourcesPerRound[Round - 1] * GetEnemyDifficultyResourceModifier());
        }

        private void HandleDinoEnterEggRadius(IDamageable target)
        {
            if (State != GameState.Running)
            {
                return;
            }

            if (target is not Dino dino ||
                !DamageableState.IsAlive(dino) ||
                !ActiveDinos.Contains(dino))
            {
                WriteRejectedVictoryTriggerAudit(target);
                return;
            }

            BeginEndingWithReason(true, SessionOutcomeReason.TargetRadiusEntered, dino);
        }

        private void WriteRejectedVictoryTriggerAudit(IDamageable target)
        {
            Transform targetTransform = target?.Transform;
            string targetName = targetTransform == null
                ? "<none>"
                : SanitizeAuditValue(targetTransform.name);
            string targetType = target == null
                ? "<none>"
                : SanitizeAuditValue(target.GetType().FullName);
            int targetInstanceId = target is Object unityObject && unityObject != null
                ? unityObject.GetInstanceID()
                : 0;

            Debug.LogWarning(
                "DINO_VICTORY_TRIGGER_REJECTED " +
                "reason=NotRegisteredLiveDino; " +
                $"trigger_name={targetName}; trigger_type={targetType}; " +
                $"trigger_instance_id={targetInstanceId}",
                this);
        }

        private void BeginEndingWithReason(
            bool playerWon,
            SessionOutcomeReason reason,
            IDamageable trigger)
        {
            if (IsEndingRound || State != GameState.Running)
            {
                return;
            }

            PendingOutcomeReason = reason;
            PendingOutcomeTrigger = trigger;
            try
            {
                BeginEnding(playerWon);
            }
            finally
            {
                PendingOutcomeReason = SessionOutcomeReason.None;
                PendingOutcomeTrigger = null;
            }
        }

        private void BeginEnding(bool playerWon)
        {
            if (IsEndingRound || State != GameState.Running)
            {
                return;
            }

            IsEndingRound = true;
            HasSessionOutcome = true;
            PlayerWon = playerWon;
            OutcomeReason = PendingOutcomeReason != SessionOutcomeReason.None
                ? PendingOutcomeReason
                : playerWon
                    ? SessionOutcomeReason.ManualVictory
                    : SessionOutcomeReason.ManualDefeat;
            WriteOutcomeAudit(PendingOutcomeTrigger);
            State = GameState.Ending;
            if (playerWon)
            {
                KillRemainingDinos();
            }
            CancelInvoke(nameof(FinishSession));
            Invoke(nameof(FinishSession), 2.5f);
        }

        private void WriteOutcomeAudit(IDamageable trigger)
        {
            Transform triggerTransform = trigger?.Transform;
            Object triggerObject = trigger as Object;
            Vector3 triggerPosition = triggerTransform == null
                ? Vector3.zero
                : triggerTransform.position;
            string triggerName = triggerTransform == null
                ? "<none>"
                : SanitizeAuditValue(triggerTransform.name);
            string triggerType = trigger == null
                ? "<none>"
                : SanitizeAuditValue(trigger.GetType().FullName);
            int triggerInstanceId = triggerObject == null
                ? 0
                : triggerObject.GetInstanceID();
            int triggerHealth = trigger == null ? 0 : trigger.Health;
            int triggerMaxHealth = trigger == null ? 0 : trigger.MaxHealth;
            int food = DinoSpawner.Instance == null ? 0 : DinoSpawner.Instance.ResourcesToSpend;
            GameSessionRestartService restartService = GameSessionRestartService.Instance;
            string controller = restartService == null
                ? "<none>"
                : restartService.CurrentLaunchRequest.Controller.ToString();
            string map = restartService == null
                ? SceneManager.GetActiveScene().name
                : restartService.CurrentLaunchRequest.MapId.ToString();
            int layoutSeed = restartService == null
                ? 0
                : restartService.CurrentLaunchRequest.LayoutSeed;

            Debug.Log(
                "DINO_SESSION_OUTCOME " +
                $"won={PlayerWon.ToString().ToLowerInvariant()}; " +
                $"reason={OutcomeReason}; " +
                $"controller={SanitizeAuditValue(controller)}; " +
                $"map={SanitizeAuditValue(map)}; " +
                $"layout_seed={layoutSeed}; " +
                $"scene={SanitizeAuditValue(SceneManager.GetActiveScene().name)}; " +
                $"elapsed_seconds={FormatAuditFloat(RunningElapsedSecondsValue)}; " +
                $"food={food}; active_dinos={ActiveDinos.Count}; " +
                $"trigger_name={triggerName}; trigger_type={triggerType}; " +
                $"trigger_instance_id={triggerInstanceId}; " +
                $"trigger_position_x={FormatAuditFloat(triggerPosition.x)}; " +
                $"trigger_position_y={FormatAuditFloat(triggerPosition.y)}; " +
                $"trigger_position_z={FormatAuditFloat(triggerPosition.z)}; " +
                $"trigger_health={triggerHealth}; trigger_max_health={triggerMaxHealth}",
                this);
        }

        private static string FormatAuditFloat(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

        private static string SanitizeAuditValue(string value) =>
            string.IsNullOrEmpty(value)
                ? "<none>"
                : value.Replace(';', '_').Replace('\r', ' ').Replace('\n', ' ');

        private void KillRemainingDinos()
        {
            Dino[] remaining = ActiveDinos.ToArray();
            foreach (Dino dino in remaining)
            {
                if (dino != null)
                {
                    if (dino.Health > 0)
                    {
                        dino.TakeDamage(int.MaxValue);
                    }
                    else
                    {
                        OnDinoDeath(dino);
                        dino.Die();
                    }
                }
            }
        }

        private void FinishSession()
        {
            if (State == GameState.Ending)
            {
                State = GameState.Ended;
            }
        }

        private void OnDinoDeath(Unit.Unit deadDino)
        {
            if (deadDino is not Dino dino) return;
            ActiveDinos.Remove(dino);
            DinoSupplyResource.Amount = ActiveDinos.Count;

            if (State == GameState.Running &&
                ShouldEndForResourceExhaustion(
                    ActiveDinos.Count,
                    DinoSpawner.Instance.ResourcesToSpend))
            {
                BeginEndingWithReason(false, SessionOutcomeReason.ResourceExhaustion, null);
            }
        }

        internal static bool ShouldEndForResourceExhaustion(int aliveDinos, int food) =>
            aliveDinos == 0 && food < MinimumDeployableDinoFood;

        private void OnSpawnDino(Unit.Unit spawnedDino)
        {
            if (spawnedDino is not Dino dino) return;
            ActiveDinos.Add(dino);
            DinoSupplyResource.Amount = ActiveDinos.Count;
        }

        private void OnDefenderDeath(Defender defender)
        {
            AliveDefenders.Remove(defender);
            if (State == GameState.Running && RewardedDefenderDeaths.Add(defender.GetInstanceID()))
            {
                DinoSpawner.Instance.ResourcesToSpend += Mathf.FloorToInt(defender.UnitType.Cost / 2f);
            }
        }

        private void OnSpawnDefender(Defender defender)
        {
            AliveDefenders.Add(defender);
        }

        private void OnSpawnWall(Wall wall)
        {
            // do we care?
        }

        private void OnWallDeath(Wall wall)
        {
            if (State == GameState.Running && RewardedWallDeaths.Add(wall.GetInstanceID()))
            {
                DinoSpawner.Instance.ResourcesToSpend += Mathf.FloorToInt(wall.UnitType.Cost / 2f);
            }
        }

        private float GetEnemyDifficultyResourceModifier()
        {
            return EnemyAIController.Instance.Difficulty switch
            {
                Difficulty.Normal => 1,
                Difficulty.Hard => 1.5f,
                Difficulty.Insane => 2f,
                _ => 1
            };
        }
    }
}
