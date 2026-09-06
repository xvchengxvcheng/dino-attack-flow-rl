using System;
using System.Linq;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Unit;
using LlamAcademy.Dinos.Utility;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

namespace LlamAcademy.Dinos.Behavior
{
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Move to Closest Attackable on XZ Plane", story: "[Self] moves to [TargetObject] on XZ Plane.", category: "Action/GameObject", id: "f485374e7293e9f1c75776ff7efd865f")]
    public partial class MoveToClosestAttackableOnXzPlaneAction : Action
    {
        [SerializeReference] public BlackboardVariable<GameObject> Self;
        [SerializeReference] public BlackboardVariable<GameObject> TargetObject;
        [SerializeReference] public BlackboardVariable<AttackConfigSO> AttackConfig;

        private Vector3 TargetLocation;
        private AttackTypeSO MinAttackDistanceAttackType;
        private NavMeshAgent Agent;
        private IDamageable TargetDamageable;
        private Collider TargetCollider;
        private bool ReachedAttackRange;

        protected override Status OnStart()
        {
            ReachedAttackRange = false;
            if (Self.Value == null || !Self.Value.TryGetComponent(out Agent) || TargetObject.Value == null)
                return Status.Failure;

            TargetCollider = TargetObject.Value.GetComponentInChildren<Collider>();
            TargetDamageable = TargetObject.Value.GetComponent<IDamageable>();
            if (!TargetIsAlive()) return Status.Failure;

            if (AttackConfig.Value != null)
            {
                MinAttackDistanceAttackType = GetMinAttackDistanceAttackType();
            }

            TargetLocation = GetTargetLocation();

            Agent.SetDestinationImmediate(TargetLocation, Agent.radius + Agent.stoppingDistance + Agent.height);
            return Status.Running;
        }

        private AttackTypeSO GetMinAttackDistanceAttackType()
        {
            AttackTypeSO minAttackDistanceAttackType = AttackConfig.Value.AttackTypes.First();
            for (int i = 1; i < AttackConfig.Value.AttackTypes.Length; i++)
            {
                if (minAttackDistanceAttackType.GetMinAttackRange(TargetDamageable) > AttackConfig.Value.AttackTypes[i].GetMinAttackRange(TargetDamageable))
                {
                    minAttackDistanceAttackType = AttackConfig.Value.AttackTypes[i];
                }
            }

            return minAttackDistanceAttackType;
        }

        private Vector3 GetTargetLocation()
        {
            Vector3 targetLocation;
            if (TargetCollider != null)
            {
                targetLocation = TargetCollider.ClosestPoint(Self.Value.transform.position);
            }
            else
            {
                targetLocation = TargetObject.Value.transform.position;
            }

            targetLocation.y = Self.Value.transform.position.y;

            NavMeshQueryFilter queryFilter = new () { agentTypeID = Agent.agentTypeID, areaMask = Agent.areaMask };
            NavMesh.Raycast(Self.Value.transform.position, targetLocation, out NavMeshHit raycastHit, queryFilter);
            NavMesh.SamplePosition(targetLocation, out NavMeshHit samplePositionHit, Agent.radius + Agent.stoppingDistance, queryFilter);

            if (raycastHit.hit || samplePositionHit.hit)
            {
                NavMeshHit hit = GetClosestHit(raycastHit, samplePositionHit, targetLocation);
                targetLocation = hit.position - (hit.position - Self.Value.transform.position).normalized * Agent.radius;
            }

            return targetLocation;
        }

        protected override Status OnUpdate()
        {
            if (!TargetIsAlive()) return Status.Failure;

            Vector3 newTargetLocation = GetTargetLocation();
            if (Vector3.Distance(newTargetLocation, TargetLocation) >= Agent.stoppingDistance)
            {
                Agent.SetDestinationImmediate(newTargetLocation, Agent.radius + Agent.stoppingDistance + Agent.height);
                TargetLocation = newTargetLocation;
                return Status.Running;
            }

            if (AttackConfig.Value != null)
            {
                if (XZDistanceIsLessThanMaxAttackDistance(MinAttackDistanceAttackType) ||
                    NavMeshUtilities.IsAtTargetLocation(Agent, TargetLocation))
                {
                    ReachedAttackRange = true;
                    return Status.Success;
                }

                return Status.Running;
            }

            if (NavMeshUtilities.IsAtTargetLocation(Agent, TargetLocation))
            {
                ReachedAttackRange = true;
                return Status.Success;
            }

            return Status.Running;
        }

        protected override void OnEnd()
        {
            if (ReachedAttackRange && Agent != null && Agent.enabled && Agent.isOnNavMesh)
            {
                Agent.ResetPath();
            }
        }

        private bool XZDistanceIsLessThanMaxAttackDistance(AttackTypeSO attack)
        {
            Vector3 xzTargetPosition = new (TargetObject.Value.transform.position.x, Agent.transform.position.y, TargetObject.Value.transform.position.z);
            return Vector3.Distance(xzTargetPosition, Agent.transform.position) <= attack.GetMinAttackRange(TargetDamageable);
        }

        private bool TargetIsAlive()
        {
            return DamageableState.IsAlive(TargetDamageable);
        }

        private NavMeshHit GetClosestHit(NavMeshHit hit1, NavMeshHit hit2, Vector3 target)
        {
            if (hit1.hit && hit2.hit)
            {
                return Vector3.Distance(hit1.position, target) <= Vector3.Distance(hit2.position, target)
                    ? hit1
                    : hit2;
            }

            if (hit1.hit && !hit2.hit)
            {
                return hit1;
            }

            return hit2;
        }
    }
}
