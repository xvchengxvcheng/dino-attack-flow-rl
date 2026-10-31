using Unity.Behavior;
using UnityEngine;

namespace LlamAcademy.Dinos.Companion
{
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class CompanionFixedGraphDriver : MonoBehaviour
    {
        private BehaviorGraphAgent GraphAgent;
        private CompanionController Controller;

        public void Configure(BehaviorGraphAgent graphAgent, CompanionController controller)
        {
            GraphAgent = graphAgent;
            Controller = controller;
            DisableFrameDrivenGraph();
        }

        private void Awake()
        {
            GraphAgent = GetComponent<BehaviorGraphAgent>();
            Controller = GetComponent<CompanionController>();
        }

        private void OnEnable() => DisableFrameDrivenGraph();

        private void FixedUpdate()
        {
            if (Controller == null || !Controller.IsBehaviourActive)
            {
                return;
            }

            if (GraphAgent != null && GraphAgent.Graph != null)
            {
                DisableFrameDrivenGraph();
                GraphAgent.Update();
            }
            else
            {
                Controller.TickBehaviour();
            }

            Controller.SyncAnimation();
        }

        private void DisableFrameDrivenGraph()
        {
            if (GraphAgent != null && GraphAgent.enabled)
            {
                GraphAgent.enabled = false;
            }
        }
    }
}
