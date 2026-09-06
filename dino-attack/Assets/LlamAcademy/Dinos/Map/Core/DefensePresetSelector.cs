using System;

namespace LlamAcademy.Dinos.Map
{
    public static class DefensePresetSelector
    {
        public static int SelectIndex(int mapSeed, int round, int presetCount)
        {
            if (presetCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(presetCount), presetCount, "Preset count must be positive.");
            }

            uint hash = unchecked((uint)mapSeed);
            hash ^= unchecked((uint)round) + 0x9E3779B9u + (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;

            return (int)(hash % (uint)presetCount);
        }
    }
}
