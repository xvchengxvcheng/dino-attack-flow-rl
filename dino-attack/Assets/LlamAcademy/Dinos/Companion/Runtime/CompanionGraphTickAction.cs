using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

namespace LlamAcademy.Dinos.Companion
{
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Companion Tick", story: "[Companion] evaluates its approved order and acts.",
        category: "Companion", id: "3a3cc3b34c704efc9cf6f2dc99da8c24")]
    public partial class CompanionGraphTickAction : Action
    {
        [SerializeReference] public BlackboardVariable<GameObject> Companion;
        private CompanionController Controller;
        private CompanionMode EnteredMode;

        protected override Status OnStart()
        {
            Controller = Companion?.Value == null
                ? null
                : Companion.Value.GetComponent<CompanionController>();
            if (Controller == null)
                return Status.Failure;
            EnteredMode = Controller.CurrentOrder == null
                ? CompanionMode.FollowPlayer
                : Controller.CurrentOrder.Mode;
            return Status.Running;
        }

        protected override Status OnUpdate()
        {
            if (Controller == null || !Controller.IsBehaviourActive)
            {
                return Status.Failure;
            }

            CompanionMode currentMode = Controller.CurrentOrder == null
                ? CompanionMode.FollowPlayer
                : Controller.CurrentOrder.Mode;
            if (currentMode != EnteredMode)
                return Status.Success;

            Controller.TickBehaviour();
            return Status.Running;
        }

        protected override void OnEnd() => Controller = null;
    }
}
