using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Simulation
{
    [DisallowMultipleComponent]
    public sealed class DinoFixedSnare : MonoBehaviour
    {
        private NavMeshAgent Agent;
        private long ReleaseTick = -1L;
        private bool WasStoppedBeforeSnare;
        private bool OwnsStop;

        private void Awake() => Agent = GetComponent<NavMeshAgent>();

        public void Apply(float durationSeconds)
        {
            if (Agent == null)
            {
                Agent = GetComponent<NavMeshAgent>();
            }
            if (Agent == null || !Agent.enabled || !Agent.isOnNavMesh)
            {
                return;
            }

            long requestedRelease = DinoFixedStepTime.CurrentTick +
                                    DinoFixedStepTime.SecondsToTicks(durationSeconds);
            if (!OwnsStop)
            {
                WasStoppedBeforeSnare = Agent.isStopped;
                OwnsStop = true;
            }
            ReleaseTick = System.Math.Max(ReleaseTick, requestedRelease);
            SetStopped(true);
        }

        private void FixedUpdate()
        {
            if (!OwnsStop || DinoFixedStepTime.CurrentTick < ReleaseTick)
            {
                return;
            }

            SetStopped(WasStoppedBeforeSnare);
            OwnsStop = false;
            ReleaseTick = -1L;
        }

        private void OnDisable()
        {
            if (OwnsStop)
            {
                SetStopped(WasStoppedBeforeSnare);
                OwnsStop = false;
                ReleaseTick = -1L;
            }
        }

        private void SetStopped(bool stopped)
        {
            if (Agent != null && Agent.enabled && Agent.isOnNavMesh)
            {
                Agent.isStopped = stopped;
            }
        }
    }
}
