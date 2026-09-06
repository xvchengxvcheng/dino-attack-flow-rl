using System;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Utility;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

namespace LlamAcademy.Dinos.Behavior
{
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Move to Target or Edge of NavMesh", story: "[Self] moves toward [Target] until it hits the edge of the NavMesh.", category: "Action/Navigation", id: "6cc955bc9d4b3d64025d8f887a89367b")]
    public partial class MoveToTargetOrEdgeOfNavMeshAction : Action
    {
        [SerializeReference] public BlackboardVariable<GameObject> Self;
        [SerializeReference] public BlackboardVariable<GameObject> Target;
        [SerializeReference] public BlackboardVariable<AttackConfigSO> AttackConfig;

        private NavMeshAgent Agent;
        private Transform SelfTransform;
        private Transform TargetTransform;
        private NavMeshQueryFilter QueryFilter;
        private bool TargetIsInRange;

        protected override Status OnStart()
        {
            if (Self.Value == null || Target.Value == null || !Self.Value.TryGetComponent(out Agent)) return Status.Failure;

            SelfTransform = Self.Value.transform;
            TargetTransform = Target.Value.transform;
            QueryFilter = new NavMeshQueryFilter() { agentTypeID = Agent.agentTypeID, areaMask = Agent.areaMask };


            if (NavMesh.Raycast(SelfTransform.position, TargetTransform.position, out NavMeshHit edgeHit, QueryFilter))
            {
                Agent.SetDestinationImmediate(edgeHit.position, Agent.radius + Agent.stoppingDistance + Agent.height);
            }
            else
            {
                Agent.SetDestinationImmediate(TargetTransform.position,
                    Agent.radius + Agent.stoppingDistance + Agent.height
                );
            }

            return Status.Running;
        }

        protected override Status OnUpdate()
        {
            if (TargetTransform == null) return Status.Failure;

            Vector3 xzTargetPosition = new (TargetTransform.position.x, SelfTransform.position.y, TargetTransform.position.z);
            TargetIsInRange = Vector3.Distance(xzTargetPosition, SelfTransform.position) <= AttackConfig.Value.GetMinAttackRange();

            if (TargetIsInRange) return Status.Success;
            if (Agent.hasPath && Agent.remainingDistance > Agent.stoppingDistance + Agent.radius) return Status.Running;

            if (NavMesh.SamplePosition(TargetTransform.position, out NavMeshHit hit, AttackConfig.Value.GetMinAttackRange(), QueryFilter))
            {
                Agent.SetDestinationImmediate(hit.position);
            }
            else if (NavMesh.Raycast(SelfTransform.position, TargetTransform.position, out NavMeshHit edgeHit, QueryFilter))
            {
                Agent.SetDestinationImmediate(edgeHit.position);
            }
            else if (!edgeHit.hit)
            {
                Vector3 directionTowardsEnemy = GetDirectionToTarget();
                Agent.velocity = directionTowardsEnemy * Agent.speed;
            }

            return Status.Running;
        }

        protected override void OnEnd()
        {
            if (Agent != null && Agent.enabled && Agent.isOnNavMesh)
            {
                Agent.ResetPath();
            }
        }

        private Vector3 GetDirectionToTarget()
        {
            Vector3 directionToTarget = TargetTransform.position - SelfTransform.position;
            directionToTarget.y = 0;
            directionToTarget.Normalize();
            return directionToTarget;
        }

    }
}
