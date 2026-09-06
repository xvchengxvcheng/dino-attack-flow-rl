using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Simulation
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class DinoFixedBehaviorGraphDriver : MonoBehaviour
    {
        private BehaviorGraphAgent GraphAgent;
        private NavMeshAgent NavigationAgent;

        public long FixedTickCount { get; private set; }

        public void Configure(BehaviorGraphAgent graphAgent)
        {
            GraphAgent = graphAgent;
            NavigationAgent = GetComponent<NavMeshAgent>();
            DisableFrameDrivenGraph();
        }

        private void OnEnable() => DisableFrameDrivenGraph();

        private void FixedUpdate()
        {
            if (GraphAgent == null ||
                (NavigationAgent != null &&
                 (!NavigationAgent.enabled || !NavigationAgent.isOnNavMesh)))
            {
                return;
            }

            DisableFrameDrivenGraph();
            GraphAgent.Update();
            FixedTickCount++;
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
