using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Behavior;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using LlamAcademy.Dinos.Utility;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Enemy
{
    [RequireComponent(typeof(NavMeshAgent), typeof(Animator), typeof(BehaviorGraphAgent))]
    [RequireComponent(typeof(Rigidbody))]
    public class Defender : Unit.Unit
    {
        public AIState State =>
            GraphAgent.GetVariable(EnemyGraphConstants.COMMAND, out BlackboardVariable<AIState> stateVariable)
                ? stateVariable.Value
                : AIState.Idle;

        public Vector3 TargetLocation => Agent.enabled && Agent.isOnNavMesh ? Agent.destination : transform.position;
        private IDamageable Target;

        protected override void Start()
        {
            base.Start();
            if (GraphAgent.GetVariable(EnemyGraphConstants.ATTACK_EVENT_CHANNEL, out BlackboardVariable<AttackEventChannel> attackEventChannel))
            {
                // attackEventChannel.Value.Event += HandleAttackEvent;
            }

            NavMeshManager.Instance.OnNavMeshUpdated += OnNavMeshUpdated;
        }

        protected override void OnDisable()
        {
            if (NavMeshManager.Instance != null)
            {
                NavMeshManager.Instance.OnNavMeshUpdated -= OnNavMeshUpdated;
            }
            base.OnDisable();
        }

        private void OnNavMeshUpdated()
        {
            // Recently, SamplePosition is suddenly returning TRUE even when no NavMesh is available at the current position.
            // Find any physics objects that "count" as being not destroyed
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, Agent.radius,
                    new NavMeshQueryFilter() { agentTypeID = Agent.agentTypeID, areaMask = Agent.areaMask }))
                // || Physics.OverlapSphere(transform.position, Agent.radius, LayerMask.GetMask("Enemies")).Count(item => !item.TryGetComponent(out Defender _)) == 0)
            {
                // building has been destroyed
                if (TryBeginDeath())
                {
                    GraphAgent.enabled = false;
                    Agent.enabled = false;
                    if (Rigidbody != null)
                    {
                        Rigidbody.isKinematic = false;
                        Rigidbody.useGravity = true;
                    }

                    Die();
                }
            }
        }

        protected override void OnTargetExit(IDamageable target)
        {
            if (GraphAgent.GetVariable(EnemyGraphConstants.NEARBY_ATTACKABLES,
                    out BlackboardVariable<List<GameObject>> nearbyAttackables))
            {
                nearbyAttackables.Value.Remove(target.Transform.gameObject);
                GraphAgent.SetVariableValue(EnemyGraphConstants.NEARBY_ATTACKABLES, nearbyAttackables.Value);

                // maybe need to go back to patrolling, or idling depending on the unit type?
            }
        }

        protected override void OnTargetEnter(IDamageable target)
        {
            if (GraphAgent.GetVariable(EnemyGraphConstants.NEARBY_ATTACKABLES,
                    out BlackboardVariable<List<GameObject>> nearbyAttackables))
            {
                nearbyAttackables.Value.Add(target.Transform.gameObject);
                GraphAgent.SetVariableValue(EnemyGraphConstants.NEARBY_ATTACKABLES, nearbyAttackables.Value);
            }

            GraphAgent.SetVariableValue(EnemyGraphConstants.COMMAND, AIState.Attacking);
        }

        public void SetDestination(Vector3 position)
        {
            GraphAgent.SetVariableValue(EnemyGraphConstants.TARGET_LOCATION, position);
            GraphAgent.SetVariableValue(EnemyGraphConstants.COMMAND, AIState.CommandedMove);
        }

        public void Patrol(List<Vector3> waypoints)
        {
            GraphAgent.SetVariableValue(EnemyGraphConstants.PATROL_WAYPOINTS, waypoints);
            GraphAgent.SetVariableValue(EnemyGraphConstants.COMMAND, AIState.Patrol);
        }

        public void Idle()
        {
            GraphAgent.SetVariableValue(EnemyGraphConstants.COMMAND, AIState.Idle);
        }

        public void Attack(IDamageable damageable)
        {
            GraphAgent.SetVariableValue(EnemyGraphConstants.TARGET, damageable);
            GraphAgent.SetVariableValue(EnemyGraphConstants.COMMAND, AIState.Attacking);
        }

        public override void Die()
        {
            GraphAgent.SetVariableValue(EnemyGraphConstants.COMMAND, AIState.Idle);
            Animator.SetTrigger(AnimationConstants.DIE_PARAMETER);
            if (TryGetComponent(out Collider collider))
            {
                collider.enabled = false;
            }
            Invoke(nameof(DestroyGO), 2.5f);
        }

        private void DestroyGO()
        {
            Destroy(gameObject);
        }
    }
}
