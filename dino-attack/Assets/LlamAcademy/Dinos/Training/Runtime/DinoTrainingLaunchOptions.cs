using System;

namespace LlamAcademy.Dinos.Training
{
    public sealed class DinoTrainingLaunchOptions
    {
        public const int DefaultBaseSeed = 20260829;

        public int BaseSeed { get; }
        public int EnvironmentIndex { get; }
        public int ProcessGeneration { get; }

        private DinoTrainingLaunchOptions(int baseSeed, int environmentIndex, int processGeneration)
        {
            BaseSeed = baseSeed;
            EnvironmentIndex = environmentIndex;
            ProcessGeneration = processGeneration;
        }

        public static bool TryParse(string[] arguments, out DinoTrainingLaunchOptions options)
        {
            options = null;
            if (arguments == null)
            {
                return false;
            }

            int trainingFlagCount = 0;
            foreach (string argument in arguments)
            {
                if (string.Equals(argument, "--dino-training", StringComparison.Ordinal))
                {
                    trainingFlagCount++;
                }
            }

            if (trainingFlagCount != 1)
            {
                return false;
            }

            int baseSeed = DefaultBaseSeed;
            int environmentIndex = 0;
            int processGeneration = 0;
            bool hasBaseSeed = false;
            bool hasEnvironmentIndex = false;
            bool hasProcessGeneration = false;

            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                if (string.Equals(argument, "--dino-base-seed", StringComparison.Ordinal))
                {
                    if (hasBaseSeed || !TryReadInteger(arguments, ref index, out baseSeed)) return false;
                    hasBaseSeed = true;
                }
                else if (string.Equals(argument, "--dino-environment-index", StringComparison.Ordinal))
                {
                    if (hasEnvironmentIndex || !TryReadInteger(arguments, ref index, out environmentIndex)) return false;
                    hasEnvironmentIndex = true;
                }
                else if (string.Equals(argument, "--dino-process-generation", StringComparison.Ordinal))
                {
                    if (hasProcessGeneration || !TryReadInteger(arguments, ref index, out processGeneration)) return false;
                    hasProcessGeneration = true;
                }
            }

            options = new DinoTrainingLaunchOptions(baseSeed, environmentIndex, processGeneration);
            return true;
        }

        private static bool TryReadInteger(string[] arguments, ref int index, out int value)
        {
            value = default;
            int valueIndex = index + 1;
            if (valueIndex >= arguments.Length ||
                !int.TryParse(arguments[valueIndex], out value))
            {
                return false;
            }

            index = valueIndex;
            return true;
        }
    }
}
