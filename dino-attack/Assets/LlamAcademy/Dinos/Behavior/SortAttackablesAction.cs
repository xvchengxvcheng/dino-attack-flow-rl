using System;
using System.Collections.Generic;
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
    [NodeDescription(name: "Sort Attackables", story: "Sort [NearbyAttackables] by closest to [Self] following [AttackConfig] priority.", category: "Action", id: "36069df22dc1b1da3032feed5ea3865d")]
    public partial class SortAttackablesAction : Action
    {
        [SerializeReference] public BlackboardVariable<List<GameObject>> NearbyAttackables;
        [SerializeReference] public BlackboardVariable<GameObject> Self;
        [SerializeReference] public BlackboardVariable<AttackConfigSO> AttackConfig;
        [SerializeReference] public BlackboardVariable<GameObject> ClosestAttackable;
        [SerializeReference] public BlackboardVariable<bool> HasLiveTarget;

        protected override Status OnStart()
        {
            ClosestAttackable.Value = null;
            HasLiveTarget.Value = false;

            if (Self.Value == null
                || AttackConfig.Value == null
                || !Self.Value.TryGetComponent(out NavMeshAgent agent))
                return Status.Failure;

            if (NearbyAttackables.Value == null || NearbyAttackables.Value.Count == 0)
                return Status.Success;

            List<GameObject> attackables = NearbyAttackables.Value;
            if (!DinoTargetSelector.TrySelect(Self.Value, attackables, AttackConfig.Value, new NavMeshQueryFilter()
            {
                agentTypeID = agent.agentTypeID,
                areaMask = agent.areaMask
            }, out GameObject selected))
            {
                return Status.Success;
            }

            attackables.Remove(selected);
            attackables.Insert(0, selected);
            NearbyAttackables.Value = attackables;
            ClosestAttackable.Value = selected;
            HasLiveTarget.Value = true;

            return Status.Success;
        }
    }
}
