using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.DefenseBattle;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.Unit;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Companion
{
    [DefaultExecutionOrder(65)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class CompanionController : MonoBehaviour, IDamageable
    {
        [Header("Scene references")]
        [SerializeField] private FirstPersonDefender Player;
        [SerializeField] private DefenseBattleCoordinator Coordinator;
        [SerializeField] private GameObject CharacterModel;
        [SerializeField] private UnitSO DamageType;
        [SerializeField] private Transform Staff;
        [SerializeField] private Animator CharacterAnimator;
        [SerializeField] private BehaviorGraphAgent GraphAgent;

        // Presentation is created on the runtime instance so old scene/prefab
        // overrides remain compatible while the weapon follows the animated hand.
        private CompanionCombatPresentation CombatPresentation;

        [Header("Combat")]
        [SerializeField] private int MaxHealthValue = 100;
        [SerializeField] private int AttackDamage = 18;
        [SerializeField] private float AttackRange = 8f;
        [SerializeField] private float AttackCooldown = 0.85f;
        [SerializeField] private float MovementSpeed = 4.6f;
        [SerializeField] private float FollowDistance = 5.5f;
        [SerializeField] private float LeashRadius = 13f;
        [SerializeField] private float ThreatRefreshSeconds = 0.25f;
        [SerializeField] private Vector3 SpawnPosition = new(3f, 0f, -40f);

        private NavMeshAgent Agent;
        private CompanionFixedGraphDriver FixedGraphDriver;
        private CompanionJevClient JevClient;
        private CompanionProfileData ProfileData;
        private CompanionOrder CurrentOrderValue;
        private float NextThreatRefresh;
        private float NextAttackTime;
        private float OrderExpiresAt;
        private Vector3 HoldPositionAnchor;
        private bool HasHoldPositionAnchor;
        private bool StartedBattle;
        private bool DemoMode;
        private bool Dead;
        private bool IsConfigured;
        private readonly List<Dino> ThreatBuffer = new();
        private readonly List<Dino> DemoThreatBuffer = new();

        public int MaxHealth { get; set; }
        public int Health { get; set; }
        public Transform Transform => transform;
        public UnitSO UnitType => DamageType;
        public bool IsBehaviourActive => IsConfigured && StartedBattle && !Dead &&
                                         (DemoMode || RoundManager.Instance != null &&
                                          RoundManager.Instance.State == GameState.Running);
        public CompanionProfileData Profile => ProfileData;
        public CompanionOrder CurrentOrder => CurrentOrderValue;
        public string StatusMessage { get; private set; } = "等待战斗开始";
        public string BehaviorLabel { get; private set; } = "待机";
        public float PlayerHealthRatio => Player == null || Player.MaxHealth <= 0
            ? 1f
            : Mathf.Clamp01(Player.Health / (float)Player.MaxHealth);
        public float HealthRatio => MaxHealth <= 0 ? 0f : Mathf.Clamp01(Health / (float)MaxHealth);
        public event IDamageable.TakeDamageEvent OnTakeDamage;
        public event IDamageable.DeathEvent OnDeath;
        public event System.Action StateChanged;
        // Dialogue replies are separate from combat/path/profile status updates.
        public event Action<string, OrderAcceptance> ReplyReceived;
        public event Action<int, Vector3> AttackDamageDealt;

        public void SyncAnimation()
        {
            if (CharacterAnimator == null)
            {
                return;
            }

            float speed = Agent == null || !Agent.enabled || Dead
                ? 0f
                : Mathf.Clamp01(Agent.velocity.magnitude / Mathf.Max(0.1f, MovementSpeed)) * 1.57f;
            CharacterAnimator.SetFloat("Speed", speed, 0.08f, Time.fixedDeltaTime);
        }

        public void Configure(FirstPersonDefender player, DefenseBattleCoordinator coordinator,
            GameObject characterModel, UnitSO damageType)
        {
            Player = player;
            Coordinator = coordinator;
            CharacterModel = characterModel;
            DamageType = damageType;
            CharacterAnimator = CharacterAnimator != null
                ? CharacterAnimator
                : CharacterModel == null ? null : CharacterModel.GetComponentInChildren<Animator>(true);
            if (Application.isPlaying)
            {
                InitializeCombatPresentation();
            }
            IsConfigured = Player != null && Coordinator != null && CharacterModel != null;
        }

        private void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            GraphAgent ??= GetComponent<BehaviorGraphAgent>();
            FixedGraphDriver = GetComponent<CompanionFixedGraphDriver>() ?? gameObject.AddComponent<CompanionFixedGraphDriver>();
            FixedGraphDriver.Configure(GraphAgent, this);
            JevClient = GetComponent<CompanionJevClient>() ?? gameObject.AddComponent<CompanionJevClient>();
            MaxHealth = Mathf.Max(1, MaxHealthValue);
            Health = MaxHealth;
            ProfileData = CompanionSaveService.Load(out string loadReason);
            if (!string.IsNullOrEmpty(loadReason) && !loadReason.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "已使用默认关系档案";
            }
            Agent.speed = MovementSpeed;
            Agent.stoppingDistance = 1.2f;
            Agent.angularSpeed = 540f;
            Agent.acceleration = 20f;
            Agent.enabled = false;
            CharacterModel?.SetActive(false);
            InitializeCombatPresentation();
            MaxHealth = Mathf.Max(1, MaxHealthValue);
            Health = MaxHealth;
            // Scene references are serialized on the Map 4 instance. Rebuild
            // the runtime gate after deserialization; Configure() is only used
            // by the editor scene builder and is not called during play mode.
            IsConfigured = Player != null && Coordinator != null && CharacterModel != null;
            CurrentOrderValue = CompanionOrder.Default(CompanionMode.FollowPlayer);
        }

        private void Start()
        {
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange += HandleGameStateChanged;
            }

            if (RoundManager.Instance != null && RoundManager.Instance.State == GameState.Running)
            {
                BeginBattle();
            }
        }

        private void OnDestroy()
        {
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange -= HandleGameStateChanged;
            }
            JevClient?.CancelPending();
        }

        private void HandleGameStateChanged(GameState oldState, GameState newState)
        {
            if (newState == GameState.Running)
            {
                BeginBattle();
            }
            else if (newState != GameState.Setup)
            {
                EndBattle();
            }
        }

        private void BeginBattle()
        {
            if (!IsConfigured || StartedBattle && !Dead)
            {
                return;
            }

            Dead = false;
            Health = MaxHealth;
            if (!TryFindSpawn(out Vector3 position))
            {
                StatusMessage = "没有安全出生点，继续在村庄内待机";
                position = SpawnPosition;
            }

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                position = hit.position;
            }

            transform.SetPositionAndRotation(position, Quaternion.identity);
            Agent.enabled = true;
            Agent.Warp(position);
            CharacterModel?.SetActive(true);
            InitializeCombatPresentation();
            StartedBattle = true;
            StatusMessage = "我在这里，和你一起守住村庄";
            BehaviorLabel = "跟随玩家";
            CurrentOrderValue = CompanionOrder.Default(CompanionMode.FollowPlayer);
            GraphAgent?.SetVariableValue("Companion", gameObject);
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Starts the companion in the standalone JEV demonstration scene. The
        /// normal round manager remains in Setup so the demonstration units do
        /// not enter the production attack wave, while the companion's graph
        /// still receives a live threat list.
        /// </summary>
        public void BeginDemoBattle(FirstPersonDefender player, DefenseBattleCoordinator coordinator,
            IReadOnlyList<Dino> passiveThreats)
        {
            if (player != null) Player = player;
            if (coordinator != null) Coordinator = coordinator;
            DemoMode = true;
            DemoThreatBuffer.Clear();
            if (passiveThreats != null)
            {
                foreach (Dino dino in passiveThreats)
                {
                    if (dino != null) DemoThreatBuffer.Add(dino);
                }
            }

            IsConfigured = Player != null && Coordinator != null && CharacterModel != null;
            if (!IsConfigured)
            {
                StatusMessage = "演示队友引用不完整，无法启动行为图";
                StateChanged?.Invoke();
                return;
            }

            StartedBattle = false;
            Dead = false;
            BeginBattle();
        }

        private void EndBattle()
        {
            StartedBattle = false;
            if (Agent != null) Agent.enabled = false;
            if (CharacterModel != null) CharacterModel.SetActive(false);
            GraphAgent?.SetVariableValue("Companion", gameObject);
            StateChanged?.Invoke();
        }

        private bool TryFindSpawn(out Vector3 position)
        {
            Vector3 origin = Player == null ? SpawnPosition : Player.transform.position + Vector3.left * 3f;
            if (NavMesh.SamplePosition(origin, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                position = hit.position + Vector3.up * 0.05f;
                return true;
            }
            position = origin;
            return false;
        }

        public void TickBehaviour()
        {
            if (!IsBehaviourActive || Agent == null || !Agent.enabled || !Agent.isOnNavMesh)
            {
                return;
            }

            float now = Time.time;
            if (now >= NextThreatRefresh)
            {
                NextThreatRefresh = now + ThreatRefreshSeconds;
                RefreshThreats();
            }

            if (HealthRatio <= 0.25f)
            {
                BehaviorLabel = "撤退恢复";
                MoveTo(Player == null ? transform.position : Player.transform.position, 2.2f);
                return;
            }

            CompanionMode mode = CurrentOrderValue == null ? CompanionMode.FollowPlayer : CurrentOrderValue.Mode;
            if (OrderExpiresAt > 0f && now >= OrderExpiresAt)
            {
                ApplyLocalMode(CompanionMode.FollowPlayer, "命令已到期，回到玩家身边");
                mode = CompanionMode.FollowPlayer;
            }

            Dino target = SelectThreat(mode);
            if (target != null)
            {
                float distance = Vector3.Distance(transform.position, target.transform.position);
                if (distance <= AttackRange && HasLineOfSight(target))
                {
                    Agent.isStopped = true;
                    Face(target.transform.position);
                    TryAttack(target, now);
                    BehaviorLabel = mode == CompanionMode.AutonomousDefense ? "自主防卫·攻击" : "保护玩家·攻击";
                    return;
                }

                if (mode == CompanionMode.AutonomousDefense || mode == CompanionMode.ProtectPlayer ||
                    distance <= Mathf.Max(FollowDistance, 7f))
                {
                    BehaviorLabel = "前往威胁点";
                    MoveTo(target.transform.position, Mathf.Max(1.1f, AttackRange - 1.5f));
                    return;
                }
            }

            Vector3 anchor = ResolveAnchor(mode);
            float anchorDistance = Vector3.Distance(transform.position, anchor);
            if (anchorDistance > 1.6f)
            {
                BehaviorLabel = mode switch
                {
                    CompanionMode.AutonomousDefense => "自主防卫·巡逻",
                    CompanionMode.HoldPosition => "守住位置",
                    CompanionMode.Regroup => "集合中",
                    _ => "跟随玩家"
                };
                MoveTo(anchor, mode == CompanionMode.FollowPlayer ? FollowDistance : 1.3f);
            }
            else
            {
                Agent.isStopped = true;
                BehaviorLabel = mode == CompanionMode.AutonomousDefense ? "自主防卫·观察" : "待机警戒";
            }
        }

        private void RefreshThreats()
        {
            ThreatBuffer.Clear();
            if (DemoMode)
            {
                foreach (Dino dino in DemoThreatBuffer)
                {
                    if (dino != null && !dino.IsDead && dino.gameObject.activeInHierarchy)
                        ThreatBuffer.Add(dino);
                }
                return;
            }
            if (RoundManager.Instance == null) return;
            foreach (Dino dino in RoundManager.Instance.ActiveDinoUnits)
            {
                if (dino != null && !dino.IsDead && dino.gameObject.activeInHierarchy)
                {
                    ThreatBuffer.Add(dino);
                }
            }
        }

        private Dino SelectThreat(CompanionMode mode)
        {
            if (ThreatBuffer.Count == 0) return null;
            Vector3 anchor = ResolveAnchor(mode);
            return ThreatBuffer
                .Where(dino => dino != null && !dino.IsDead)
                .OrderBy(dino => mode == CompanionMode.ProtectPlayer && Player != null
                    ? Vector3.Distance(dino.transform.position, Player.transform.position)
                    : Vector3.Distance(dino.transform.position, anchor))
                .FirstOrDefault(dino =>
                    Vector3.Distance(dino.transform.position, anchor) <=
                    (mode == CompanionMode.AutonomousDefense ? LeashRadius + AttackRange : LeashRadius));
        }

        private Vector3 ResolveAnchor(CompanionMode mode)
        {
            if (mode == CompanionMode.HoldPosition && HasHoldPositionAnchor)
            {
                return HoldPositionAnchor;
            }
            if (mode == CompanionMode.AutonomousDefense && RoundManager.Instance != null)
            {
                VillageHouse house = RoundManager.Instance.SessionHouses
                    .Where(candidate => candidate != null)
                    .OrderBy(candidate => Player == null
                        ? Vector3.Distance(candidate.transform.position, transform.position)
                        : Vector3.Distance(candidate.transform.position, Player.transform.position))
                    .FirstOrDefault();
                if (house != null) return house.transform.position;
                if (RoundManager.Instance.DinoTarget != null) return RoundManager.Instance.DinoTarget.position;
            }
            if (Player != null)
            {
                Vector3 behind = Player.transform.position - Player.transform.forward * FollowDistance;
                return NavMesh.SamplePosition(behind, out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position : behind;
            }
            return transform.position;
        }

        private void MoveTo(Vector3 position, float stoppingDistance)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 4f, Agent.areaMask))
            {
                Agent.isStopped = true;
                StatusMessage = "我找不到那里的路，先守在可达位置";
                return;
            }
            Agent.stoppingDistance = stoppingDistance;
            Agent.isStopped = false;
            if (!Agent.hasPath || Vector3.Distance(Agent.destination, hit.position) > 1.5f)
            {
                Agent.SetDestination(hit.position);
            }
        }

        private void TryAttack(Dino target, float now)
        {
            if (now < NextAttackTime || target == null || target.IsDead) return;
            NextAttackTime = now + AttackCooldown;
            // Trigger the cast presentation for every valid attack attempt. The
            // controller remains the gameplay authority; VFX cannot deal damage.
            CharacterAnimator?.ResetTrigger("Cast");
            CharacterAnimator?.SetTrigger("Cast");
            CombatPresentation?.PlayAttack(target.transform.position + Vector3.up * 1.1f);
            int before = target.Health;
            target.TakeDamage(Mathf.Max(1, AttackDamage));
            int applied = Mathf.Clamp(before - target.Health, 0, before);
            if (applied > 0)
            {
                AttackDamageDealt?.Invoke(applied, target.transform.position + Vector3.up * 2f);
            }
            if (applied > 0)
            {
                StatusMessage = $"施法命中：-{applied}";
                StateChanged?.Invoke();
            }
        }

        private void InitializeCombatPresentation()
        {
            if (CharacterModel == null || CharacterAnimator == null)
            {
                return;
            }

            CombatPresentation = GetComponent<CompanionCombatPresentation>() ??
                                 gameObject.AddComponent<CompanionCombatPresentation>();
            CombatPresentation.Initialize(CharacterModel, CharacterAnimator);
            Staff = CombatPresentation.StaffTransform;
        }

        private bool HasLineOfSight(Dino target)
        {
            Vector3 origin = transform.position + Vector3.up * 1.55f;
            Vector3 delta = target.transform.position + Vector3.up * 1.1f - origin;
            if (delta.sqrMagnitude < 0.01f) return true;
            if (!Physics.Raycast(origin, delta.normalized, out RaycastHit hit, delta.magnitude,
                    ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.transform.IsChildOf(target.transform);
        }

        private void Face(Vector3 target)
        {
            Vector3 direction = target - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), 540f * Time.fixedDeltaTime);
            }
        }

        public void SubmitText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            CompanionSituation situation = BuildSituation();
            if (JevClient == null)
            {
                ApplyIntent(CompanionLocalIntentParser.Parse(text), "本地意图解析");
                return;
            }
            StartCoroutine(JevClient.Request(text, ProfileData, situation, (intent, reason) => ApplyIntent(intent, reason)));
        }

        public void SubmitMode(CompanionMode mode)
        {
            ApplyIntent(new CompanionIntent
            {
                Mode = mode,
                Confidence = 1f,
                ExplicitAutonomy = mode == CompanionMode.AutonomousDefense,
                NeedsClarification = false
            }, "快捷命令");
        }

        private void ApplyIntent(CompanionIntent intent, string source)
        {
            if (intent == null)
            {
                StatusMessage = "我没有听清，请再说一次";
                StateChanged?.Invoke();
                ReplyReceived?.Invoke(StatusMessage, OrderAcceptance.Rejected);
                return;
            }
            CompanionOrder order = CompanionOrderEvaluator.Evaluate(ProfileData, intent, BuildSituation());
            ApplyOrder(order, source);
            ReplyReceived?.Invoke(StatusMessage, order.Acceptance);
        }

        public void ApplyOrder(CompanionOrder order, string source = "本地规则")
        {
            if (order == null) return;
            StatusMessage = string.IsNullOrEmpty(order.Message) ? source : order.Message;
            ProfileData.Familiarity = Mathf.Clamp(ProfileData.Familiarity + 0.25f, 0f, 100f);
            if (order.Acceptance == OrderAcceptance.Rejected || order.Acceptance == OrderAcceptance.Deferred)
            {
                // Report the decision without replacing the active order or its execution state.
                ProfileData.Stress = Mathf.Clamp(ProfileData.Stress + 0.5f, 0f, 100f);
                StateChanged?.Invoke();
                return;
            }

            CurrentOrderValue = order;
            OrderExpiresAt = order.DurationSeconds > 0f ? Time.time + order.DurationSeconds : 0f;
            if (order.Mode == CompanionMode.HoldPosition)
            {
                HoldPositionAnchor = transform.position;
                HasHoldPositionAnchor = true;
            }
            else
            {
                HasHoldPositionAnchor = false;
            }
            BehaviorLabel = ModeLabel(order.Mode);
            StateChanged?.Invoke();
        }

        private void ApplyLocalMode(CompanionMode mode, string message)
        {
            CompanionOrder order = CompanionOrder.Default(mode);
            order.Message = message;
            ApplyOrder(order, "本地安全规则");
        }

        private CompanionSituation BuildSituation() => new()
        {
            RoundRunning = DemoMode || RoundManager.Instance != null && RoundManager.Instance.State == GameState.Running,
            SelfHealthRatio = HealthRatio,
            PlayerHealthRatio = PlayerHealthRatio,
            PlayerThreatened = ThreatBuffer.Any(dino => Player != null &&
                Vector3.Distance(dino.transform.position, Player.transform.position) < AttackRange + 2f),
            VillageThreat = ThreatBuffer.Count == 0 ? 0f : Mathf.Clamp01(ThreatBuffer.Count / 5f),
            DestinationReachable = Agent == null || Agent.isOnNavMesh
        };

        public void ApplyProfile(CompanionProfileData profile)
        {
            ProfileData = (profile ?? CompanionProfileData.CreateDefault()).Clone();
            ProfileData.Sanitize();
            StateChanged?.Invoke();
        }

        public bool SaveRelationship() => CompanionSaveService.Save(ProfileData, out _);

        public void ResetRelationship()
        {
            ApplyProfile(CompanionProfileData.CreateDefault());
            SaveRelationship();
        }

        public void TakeDamage(int damage)
        {
            if (Dead || damage <= 0) return;
            int applied = Mathf.Min(damage, Health);
            Health -= applied;
            OnTakeDamage?.Invoke(this, applied);
            if (Health <= 0 && !Dead)
            {
                Dead = true;
                Health = 0;
                Agent.enabled = false;
                CharacterModel?.SetActive(false);
                GraphAgent?.SetVariableValue("Companion", gameObject);
                StatusMessage = "我倒下了，但村庄防线仍在继续";
                OnDeath?.Invoke(this);
                StateChanged?.Invoke();
            }
        }

        public void ReviveForNextRound()
        {
            Dead = false;
            StartedBattle = false;
            BeginBattle();
        }

        public void SetPlayer(FirstPersonDefender player) => Player = player;
        public void SetCoordinator(DefenseBattleCoordinator coordinator) => Coordinator = coordinator;
        public string ModeLabel(CompanionMode mode) => mode switch
        {
            CompanionMode.AutonomousDefense => "自主防卫",
            CompanionMode.HoldPosition => "守住位置",
            CompanionMode.ProtectPlayer => "保护玩家",
            CompanionMode.Regroup => "集合",
            _ => "跟随玩家"
        };

        public bool IsDead => Dead;
    }
}
