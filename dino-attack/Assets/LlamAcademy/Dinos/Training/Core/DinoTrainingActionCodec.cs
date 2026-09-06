using System;
using System.Collections.Generic;
using UnityEngine;

namespace LlamAcademy.Dinos.Training
{
    public readonly struct DecodedDinoTrainingAction
    {
        public bool IsWait { get; }
        public int ZoneIndex { get; }
        public int DinoIndex { get; }
        public Vector2 RelativeUv { get; }

        public DecodedDinoTrainingAction(bool isWait, int zoneIndex, int dinoIndex, Vector2 relativeUv)
        {
            IsWait = isWait;
            ZoneIndex = zoneIndex;
            DinoIndex = dinoIndex;
            RelativeUv = relativeUv;
        }

        // These aliases keep the already-imported runtime code compiling until its dedicated
        // migration task consumes IsWait, ZoneIndex, and RelativeUv.
        public bool ShouldPlace => !IsWait;
        public Vector2 NormalizedPosition => RelativeUv;
    }

    public static class DinoTrainingActionCodec
    {
        public const int ActionSize = 4;
        private const int ZoneCount = 5;
        private const int DinoChoiceCount = 4;
        private static readonly float[] ZoneThresholds = { -0.6f, -0.2f, 0.2f, 0.6f };
        private static readonly float[] DinoChoiceThresholds = { -0.5f, 0f, 0.5f };

        public static bool TryDecode(IReadOnlyList<float> action, out DecodedDinoTrainingAction decoded)
        {
            decoded = default;
            if (action == null || action.Count != ActionSize)
            {
                return false;
            }

            for (int i = 0; i < action.Count; i++)
            {
                if (float.IsNaN(action[i]) || float.IsInfinity(action[i]))
                {
                    return false;
                }
            }

            int zoneIndex = DecodeBin(action[0], ZoneCount);
            int choice = DecodeBin(action[3], DinoChoiceCount);
            bool isWait = choice == 0;
            decoded = isWait
                ? new DecodedDinoTrainingAction(true, zoneIndex, -1, Vector2.zero)
                : new DecodedDinoTrainingAction(
                    false,
                    zoneIndex,
                    choice - 1,
                    new Vector2(
                        Mathf.Clamp01((action[1] + 1f) * 0.5f),
                        Mathf.Clamp01((action[2] + 1f) * 0.5f)));
            return true;
        }

        private static int DecodeBin(float value, int count)
        {
            float clamped = Mathf.Clamp(value, -1f, 1f);
            if (count == ZoneCount)
            {
                return DecodeBin(clamped, ZoneThresholds);
            }

            if (count == DinoChoiceCount)
            {
                return DecodeBin(clamped, DinoChoiceThresholds);
            }

            throw new ArgumentOutOfRangeException(nameof(count));
        }

        private static int DecodeBin(float value, float[] thresholds)
        {
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (value < thresholds[i])
                {
                    return i;
                }
            }

            return thresholds.Length;
        }
    }
}
