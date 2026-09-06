using System;
using System.Collections.Generic;

namespace LlamAcademy.Dinos.Inference
{
    public sealed class DinoInferenceDecisionPolicy
    {
        public const int DecisionPeriodAcademySteps = 25;
        public const int ActionSize = 4;

        private bool HasConsumedDecision;
        private int LastDecisionAcademyStep;

        public bool TryBeginDecision(bool isRunning, int academyStep)
        {
            if (!isRunning)
            {
                Reset();
                return false;
            }

            if (academyStep < 0)
            {
                return false;
            }

            if (HasConsumedDecision &&
                (long)academyStep - LastDecisionAcademyStep < DecisionPeriodAcademySteps)
            {
                return false;
            }

            HasConsumedDecision = true;
            LastDecisionAcademyStep = academyStep;
            return true;
        }

        public bool TrySanitizeActions(
            IReadOnlyList<float> modelOutput,
            out float[] actions,
            out string failureReason)
        {
            actions = null;
            if (modelOutput == null || modelOutput.Count != ActionSize)
            {
                failureReason = $"Inference output must contain exactly {ActionSize} values.";
                return false;
            }

            float[] sanitized = new float[ActionSize];
            for (int index = 0; index < modelOutput.Count; index++)
            {
                float value = modelOutput[index];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    failureReason = $"Inference output {index} is not finite.";
                    return false;
                }

                sanitized[index] = Math.Max(-1f, Math.Min(1f, value));
            }

            actions = sanitized;
            failureReason = string.Empty;
            return true;
        }

        public void Reset()
        {
            HasConsumedDecision = false;
            LastDecisionAcademyStep = 0;
        }
    }
}
