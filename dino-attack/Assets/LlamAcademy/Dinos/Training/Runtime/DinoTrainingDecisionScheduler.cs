using LlamAcademy.Dinos.RoundManagement;
using Unity.MLAgents;
using UnityEngine;

namespace LlamAcademy.Dinos.Training
{
    public sealed class DinoTrainingDecisionScheduler : MonoBehaviour
    {
        private const int DecisionPeriodAcademySteps = 25;

        [SerializeField] private DinoTrainingAgent Agent;
        private bool HasRequestedDecision;
        private int LastDecisionAcademyStep;

        public void Configure(DinoTrainingAgent agent, float intervalSeconds)
        {
            Agent = agent;
        }

        private void OnEnable()
        {
            Academy.Instance.AgentPreStep += OnAgentPreStep;
        }

        private void OnDisable()
        {
            if (Academy.IsInitialized)
            {
                Academy.Instance.AgentPreStep -= OnAgentPreStep;
            }

            HasRequestedDecision = false;
            LastDecisionAcademyStep = 0;
        }

        private void OnAgentPreStep(int academyStep)
        {
            if (RoundManager.Instance == null)
            {
                return;
            }

            GameState state = RoundManager.Instance.State;
            if (state != GameState.Running)
            {
                TryConsumeDecisionStep(state, academyStep);
                return;
            }

            if (Agent == null || !Agent.AcceptsDecisions ||
                !TryConsumeDecisionStep(state, academyStep))
            {
                return;
            }

            Agent.RequestDecision();
        }

        internal bool TryConsumeDecisionStep(GameState state, int academyStep)
        {
            if (state != GameState.Running)
            {
                HasRequestedDecision = false;
                return false;
            }

            if (!HasRequestedDecision || academyStep - LastDecisionAcademyStep >= DecisionPeriodAcademySteps)
            {
                HasRequestedDecision = true;
                LastDecisionAcademyStep = academyStep;
                return true;
            }

            return false;
        }
    }
}
