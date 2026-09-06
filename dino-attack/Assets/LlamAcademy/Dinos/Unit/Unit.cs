using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.UI;
using LlamAcademy.Dinos.Simulation;
using LlamAcademy.Dinos.Utility;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Unit
{
    public abstract class Unit : MonoBehaviour, IDamageable
    {
        [field: SerializeField] public int MaxHealth { get; set; }
        [field: SerializeField] public int Health { get; set; }

        public Transform Transform => transform;
        [field: SerializeField] public UnitSO UnitType { get; set; }
        [SerializeField] protected AttackRadius AttackRadius;
        [SerializeField] protected HealthBar HealthBar;
        public event IDamageable.TakeDamageEvent OnTakeDamage;
        public event IDamageable.DeathEvent OnDeath;

        public NavMeshAgent Agent { get; protected set; }
        protected Animator Animator;
        protected Rigidbody Rigidbody;
        protected BehaviorGraphAgent GraphAgent;
        private DinoFixedBehaviorGraphDriver FixedGraphDriver;
        private float LastSpeed;
        private bool deathStarted;
        private bool hasStarted;
        private bool sensorSubscribed;

        public bool IsDead => deathStarted || Health <= 0;

        protected virtual void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            Animator = GetComponent<Animator>();
            GraphAgent = GetComponent<BehaviorGraphAgent>();
            if (GraphAgent != null)
            {
                FixedGraphDriver = GetComponent<DinoFixedBehaviorGraphDriver>();
                if (FixedGraphDriver == null)
                {
                    FixedGraphDriver = gameObject.AddComponent<DinoFixedBehaviorGraphDriver>();
                }
                FixedGraphDriver.Configure(GraphAgent);
            }
            Rigidbody = GetComponent<Rigidbody>();
            MaxHealth = UnitType.Health;
            Health = UnitType.Health;
        }

        protected virtual void Start()
        {
            if (Agent != null)
            {
                Agent.enabled = true;
            }

            hasStarted = true;
            SubscribeToSensorAndReplayCurrentTargets();

            if (HealthBar == null)
            {
                HealthBar = GetComponentInChildren<HealthBar>();
            }
            if (HealthBar != null)
            {
                HealthBarCanvas.Instance.Register(HealthBar, this);
            }

            if (GraphAgent != null)
            {
                FixedGraphDriver.Configure(GraphAgent);
                FixedGraphDriver.enabled = true;
            }
        }

        protected virtual void OnEnable()
        {
            if (hasStarted)
            {
                SubscribeToSensorAndReplayCurrentTargets();
            }
        }

        protected virtual void OnDisable()
        {
            if (AttackRadius == null || !sensorSubscribed)
            {
                return;
            }

            AttackRadius.OnTargetEnter -= OnTargetEnter;
            AttackRadius.OnTargetExit -= OnTargetExit;
            sensorSubscribed = false;
        }

        private void SubscribeToSensorAndReplayCurrentTargets()
        {
            if (AttackRadius == null)
            {
                return;
            }

            if (!sensorSubscribed)
            {
                AttackRadius.OnTargetEnter += OnTargetEnter;
                AttackRadius.OnTargetExit += OnTargetExit;
                sensorSubscribed = true;
            }

            AttackRadius.ReplayCurrentTargets(OnTargetEnter);
        }

        protected abstract void OnTargetEnter(IDamageable target);
        protected abstract void OnTargetExit(IDamageable target);

        public virtual void TakeDamage(int damage)
        {
            if (deathStarted || damage <= 0 || Health <= 0)
            {
                return;
            }

            int appliedDamage = Mathf.Min(damage, Health);
            Health -= appliedDamage;
            if (HealthBar != null)
            {
                HealthBar.SetProgress(MaxHealth > 0 ? (float)Health / MaxHealth : 0f);
            }

            RaiseDamageEvent(appliedDamage);
            if (Health == 0 && TryBeginDeath())
            {
                Die();
            }
        }

        protected bool TryBeginDeath()
        {
            if (deathStarted)
            {
                return false;
            }

            deathStarted = true;
            Health = 0;
            RaiseDeathEvent();
            return true;
        }

        protected virtual void Update()
        {
            if (Animator == null) return;

            Animator.SetFloat(AnimationConstants.SPEED_PARAMETER, Agent.enabled ? Agent.velocity.magnitude : 0);
        }

        public abstract void Die();

        protected void RaiseDamageEvent(int damage) => OnTakeDamage?.Invoke(this, damage);
        protected void RaiseDeathEvent() => OnDeath?.Invoke(this);

    }
}
