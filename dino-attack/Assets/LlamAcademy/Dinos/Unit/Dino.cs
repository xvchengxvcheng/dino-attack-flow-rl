using System;
using System.Collections;
using System.Collections.Generic;
using LlamAcademy.Dinos.Behavior;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Utility;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

namespace LlamAcademy.Dinos.Unit
{
    [RequireComponent(typeof(NavMeshAgent), typeof(Animator), typeof(BehaviorGraphAgent))]
    [RequireComponent(typeof(Rigidbody))]
    public class Dino : Unit
    {
        [SerializeField] private Material DefaultMaterial;
        [SerializeField] private ChainIKConstraint ChainIKConstraint;
        [SerializeField] private IKConstraintData IKConstraint;
        private GameObject Target;
        private DinoTargetTracker TargetTracker;
        private readonly List<GameObject> LiveTargetBuffer = new();

        private Coroutine IKCoroutine;

        public bool HasTrackedLiveTargets => TargetTracker != null && TargetTracker.HasLiveTarget;

        protected override void Awake()
        {
            base.Awake();
            TargetTracker = new DinoTargetTracker();
        }

        protected override void Start()
        {
            base.Start();
            RoundManager.Instance.OnGameStateChange += InstanceOnOnGameStateChange;

            if (ChainIKConstraint != null && GraphAgent.GetVariable(DinoGraphConstants.ATTACK_EVENT_CHANNEL, out BlackboardVariable<AttackEventChannel> attackEventChannelVariable))
            {
                // In Behavior 1.0.10 simply doing attackEventChannelVariable.Value.Event += HandleAttack; became inaccessible.
                // This is a workaround to that. It is planned to be re-added.
                Action<GameObject, GameObject> target = HandleAttack;
                attackEventChannelVariable.Value.RegisterListener(Delegate.CreateDelegate(typeof(AttackEventChannel.AttackEventChannelEventHandler), target.Target, target.Method));
            }

            RestoreDefaultMaterial();

            if (RoundManager.Instance.State == GameState.Running)
            {
                ActivateGraphForRunning();
            }
            else
            {
                // Disabling the runtime graph clears its blackboard. All dynamic
                // values are therefore restored atomically when the round starts.
                GraphAgent.enabled = false;
            }
        }

        private void HandleAttack(GameObject self, GameObject target)
        {
            Target = target;
        }

        /// <summary>
        /// Animation Event, called at the start of the attack animation.
        /// </summary>
        private void BeginAttack()
        {
            if (ChainIKConstraint != null && Target != null && !Target.transform.TryGetComponent(out Wall _))
            {
                ChainIKConstraint.data.target.position = Target.transform.position + Vector3.up * 2;
                if (IKCoroutine != null)
                {
                    StopCoroutine(IKCoroutine);
                }
                IKCoroutine = StartCoroutine(LerpChainIKWeight());
            }
        }

        private IEnumerator LerpChainIKWeight()
        {
            float time = Time.deltaTime;
            ChainIKConstraint.weight = 0;
            while (time < 1)
            {
                time += Time.deltaTime / IKConstraint.LerpTime;
                ChainIKConstraint.weight = Mathf.Lerp(0, IKConstraint.MaxWeight, time);
                yield return null;
            }

            ChainIKConstraint.weight = IKConstraint.MaxWeight;

            yield return new WaitForSeconds(IKConstraint.FullWeightDuration);
            time = 0;
            while (time < 1)
            {
                time -= Time.deltaTime / IKConstraint.LerpTime;
                ChainIKConstraint.weight -= IKConstraint.MaxWeight * Time.deltaTime;
                yield return null;
            }

            ChainIKConstraint.weight = 0;
        }

        private void InstanceOnOnGameStateChange(GameState oldstate, GameState newstate)
        {
            if (newstate == GameState.Running)
            {
                ActivateGraphForRunning();
            }
        }

        private void ActivateGraphForRunning()
        {
            // Component enablement reconstructs the runtime blackboard. Populate it
            // immediately, before the next graph update can choose a navigation branch.
            GraphAgent.enabled = true;
            GraphAgent.SetVariableValue(
                DinoGraphConstants.POST_ATTACK_COOLDOWN,
                UnitType.AttackConfig.PostAttackCooldown);
            SyncTargetBlackboard();
            if (!HasTrackedLiveTargets)
            {
                EnsureVillageNavigation();
            }
        }

        protected override void OnTargetExit(IDamageable target)
        {
            TargetTracker?.Remove(target);
        }

        protected override void OnTargetEnter(IDamageable target)
        {
            TargetTracker?.Add(target);
        }

        protected override void OnEnable()
        {
            if (TargetTracker == null)
            {
                TargetTracker = new DinoTargetTracker();
            }
            TargetTracker.Changed -= SyncTargetBlackboard;
            TargetTracker.Changed += SyncTargetBlackboard;
            Agent.Warp(transform.position);
            Agent.enabled = true;
            IgnoreOtherDinoBodyCollisions();
            base.OnEnable();
        }

        private void IgnoreOtherDinoBodyCollisions()
        {
            Collider[] ownColliders = GetComponentsInChildren<Collider>(true);
            Dino[] activeDinos = FindObjectsByType<Dino>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Dino other in activeDinos)
            {
                if (other == this) continue;
                DinoCrowdCollisionPolicy.IgnoreMutualBodyCollisions(
                    ownColliders,
                    other.GetComponentsInChildren<Collider>(true));
            }
        }

        protected override void OnDisable()
        {
            TargetTracker.Changed -= SyncTargetBlackboard;
            TargetTracker.Clear();
            Agent.enabled = false;
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange -= InstanceOnOnGameStateChange;
            }
            base.OnDisable();
        }

        public override void Die()
        {
            if (Animator != null)
            {
                Animator.SetTrigger(AnimationConstants.DIE_PARAMETER);
            }

            if (Agent != null)
            {
                Agent.enabled = false;
            }

            if (GraphAgent != null)
            {
                GraphAgent.enabled = false;
            }

            Invoke(nameof(DestroyGO), 2.5f);
        }

        private void DestroyGO()
        {
            Destroy(gameObject);
        }

        public void SetDestination(Vector3 target)
        {
            if (GraphAgent != null)
            {
                GraphAgent.SetVariableValue(DinoGraphConstants.EGG_LOCATION, target);
            }
        }

        public bool EnsureVillageNavigation()
        {
            if (IsDead
                || RoundManager.Instance == null
                || RoundManager.Instance.State != GameState.Running
                || RoundManager.Instance.DinoTarget == null
                || Agent == null
                || !Agent.enabled
                || !Agent.isOnNavMesh)
            {
                return false;
            }

            Vector3 villageTarget = RoundManager.Instance.DinoTarget.position;
            SetDestination(villageTarget);
            Agent.isStopped = false;
            bool destinationNeedsRefresh = !Agent.hasPath
                || Agent.pathStatus != NavMeshPathStatus.PathComplete
                || Vector3.SqrMagnitude(Agent.destination - villageTarget) > 1f;
            return !destinationNeedsRefresh || Agent.SetDestinationImmediate(villageTarget, 3f);
        }

        private void SyncTargetBlackboard()
        {
            if (TargetTracker == null || GraphAgent == null)
            {
                return;
            }

            TargetTracker.CopyLiveTargets(LiveTargetBuffer);
            if (GraphAgent.GetVariable(
                    DinoGraphConstants.NEARBY_ATTACKABLES,
                    out BlackboardVariable<List<GameObject>> nearbyAttackables) &&
                nearbyAttackables.Value != null)
            {
                nearbyAttackables.Value.Clear();
                nearbyAttackables.Value.AddRange(LiveTargetBuffer);
                GraphAgent.SetVariableValue(
                    DinoGraphConstants.NEARBY_ATTACKABLES,
                    nearbyAttackables.Value);
            }
            else
            {
                GraphAgent.SetVariableValue(
                    DinoGraphConstants.NEARBY_ATTACKABLES,
                    new List<GameObject>(LiveTargetBuffer));
            }
            if (GraphAgent.GetVariable(
                    DinoGraphConstants.HAS_LIVE_TARGET,
                    out BlackboardVariable<bool> hasLiveTarget))
            {
                hasLiveTarget.Value = LiveTargetBuffer.Count > 0;
                GraphAgent.SetVariableValue(DinoGraphConstants.HAS_LIVE_TARGET, hasLiveTarget.Value);
            }

            if (LiveTargetBuffer.Count == 0)
            {
                EnsureVillageNavigation();
            }
        }

        private void RestoreDefaultMaterial()
        {
            GetComponentInChildren<Renderer>().material = DefaultMaterial;
        }

        [Serializable]
        private struct IKConstraintData
        {
            [field: SerializeField] public float LerpTime { get; private set; }
            [field: SerializeField] public float MaxWeight { get; private set; }
            [field: SerializeField] public float FullWeightDuration { get; private set; }
        }
    }
}
