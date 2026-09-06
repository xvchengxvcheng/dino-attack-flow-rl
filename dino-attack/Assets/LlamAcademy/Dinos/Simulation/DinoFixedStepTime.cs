using System;
using UnityEngine;

namespace LlamAcademy.Dinos.Simulation
{
    public static class DinoFixedStepTime
    {
        public static long CurrentTick
        {
            get
            {
                double fixedStepSeconds = Time.fixedDeltaTime;
                if (!double.IsFinite(fixedStepSeconds) || fixedStepSeconds <= 0.0)
                {
                    return 0L;
                }

                return Math.Max(0L, (long)Math.Round(
                    Time.fixedTimeAsDouble / fixedStepSeconds,
                    MidpointRounding.AwayFromZero));
            }
        }

        public static long SecondsToTicks(float seconds, float fixedStepSeconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
            if (!float.IsFinite(fixedStepSeconds) || fixedStepSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedStepSeconds));
            }

            double ratio = (double)seconds / fixedStepSeconds;
            double roundingTolerance = Math.Max(1.0, Math.Abs(ratio)) * 1e-7;
            return (long)Math.Ceiling(ratio - roundingTolerance);
        }

        public static long SecondsToTicks(float seconds) =>
            SecondsToTicks(seconds, Time.fixedDeltaTime);
    }
}
