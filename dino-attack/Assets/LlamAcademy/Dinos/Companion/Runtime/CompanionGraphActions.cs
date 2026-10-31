using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

namespace LlamAcademy.Dinos.Companion
{
    /// <summary>
    /// Reads the controller's currently approved order before the Switch node
    /// runs.  The action returns Success intentionally: the Start node is a
    /// repeating root, so this is evaluated again on the next graph tick and a
    /// changed order can select a different Switch branch.
    /// </summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Select Companion Activity", story: "[Companion] writes the approved order to [Activity].",
        category: "Companion", id: "a8e9f2b5e0de4b72a3561158cc0c1f11")]
    public partial class CompanionSelectActivityAction : Action
    {
        [SerializeReference] public BlackboardVariable<GameObject> Companion;
        [SerializeReference] public BlackboardVariable<CompanionMode> Activity;

        protected override Status OnStart()
        {
            CompanionController controller = Companion?.Value?.GetComponent<CompanionController>();
            if (controller == null)
                return Status.Failure;

            CompanionMode mode = controller.CurrentOrder == null
                ? CompanionMode.FollowPlayer
                : controller.CurrentOrder.Mode;
            if (Activity != null)
                Activity.Value = mode;
            return Status.Success;
        }
    }

    /// <summary>
    /// Base implementation shared by the visible Switch branches.  Each
    /// derived node has its own NodeDescription, so the graph remains readable
    /// and designers can replace or tune one activity without collapsing the
    /// graph into a single opaque Tick action.
    /// </summary>
    public abstract class CompanionActivityBranchAction : Action
    {
        [SerializeReference] public BlackboardVariable<GameObject> Companion;
        private CompanionMode EnteredMode;

        protected override Status OnStart()
        {
            CompanionController controller = Companion?.Value?.GetComponent<CompanionController>();
            if (controller == null || !controller.IsBehaviourActive)
                return Status.Failure;

            EnteredMode = controller.CurrentOrder == null
                ? CompanionMode.FollowPlayer
                : controller.CurrentOrder.Mode;
            controller.TickBehaviour();
            return Status.Running;
        }

        protected override Status OnUpdate()
        {
            CompanionController controller = Companion?.Value?.GetComponent<CompanionController>();
            if (controller == null || !controller.IsBehaviourActive)
                return Status.Failure;
            CompanionMode currentMode = controller.CurrentOrder == null
                ? CompanionMode.FollowPlayer
                : controller.CurrentOrder.Mode;
            if (currentMode != EnteredMode)
                return Status.Success;
            controller.TickBehaviour();
            return Status.Running;
        }
    }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Follow Companion", story: "Follow the player and stay within the approved leash.",
        category: "Companion/Activities", id: "2d5e8b9d9d74430a9ce3e4df0c1aa101")]
    public partial class CompanionFollowAction : CompanionActivityBranchAction { }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Combat Companion", story: "Protect the player and attack the nearest approved threat.",
        category: "Companion/Activities", id: "2d5e8b9d9d74430a9ce3e4df0c1aa102")]
    public partial class CompanionCombatAction : CompanionActivityBranchAction { }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Recover Companion", story: "Regroup near the player while health or stress is unsafe.",
        category: "Companion/Activities", id: "2d5e8b9d9d74430a9ce3e4df0c1aa103")]
    public partial class CompanionRecoverAction : CompanionActivityBranchAction { }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Autonomous Defense", story: "Leave the player when approved and patrol village threats.",
        category: "Companion/Activities", id: "2d5e8b9d9d74430a9ce3e4df0c1aa104")]
    public partial class CompanionAutonomousDefenseAction : CompanionActivityBranchAction { }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Hold Position", story: "Hold the recorded position and defend its radius.",
        category: "Companion/Activities", id: "2d5e8b9d9d74430a9ce3e4df0c1aa105")]
    public partial class CompanionHoldAction : CompanionActivityBranchAction { }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Rally Companion", story: "Regroup with the player after a rally command.",
        category: "Companion/Activities", id: "2d5e8b9d9d74430a9ce3e4df0c1aa106")]
    public partial class CompanionRallyAction : CompanionActivityBranchAction { }
}
