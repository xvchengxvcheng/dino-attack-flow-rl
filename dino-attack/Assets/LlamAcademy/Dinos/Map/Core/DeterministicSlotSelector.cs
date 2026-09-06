using System;

namespace LlamAcademy.Dinos.Map
{
    public static class DeterministicSlotSelector
    {
        public static int[] SelectWithoutReplacement(int count, int take, uint seed)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be positive.");
            }

            if (take < 0 || take > count)
            {
                throw new ArgumentOutOfRangeException(nameof(take), take, "Take must be between zero and count.");
            }

            int[] slots = new int[count];
            for (int index = 0; index < count; index++)
            {
                slots[index] = index;
            }

            uint state = seed;
            for (int index = 0; index < take; index++)
            {
                int swapIndex = index + (int)(Next(ref state) % (uint)(count - index));
                (slots[index], slots[swapIndex]) = (slots[swapIndex], slots[index]);
            }

            int[] selected = new int[take];
            Array.Copy(slots, selected, take);
            return selected;
        }

        private static uint Next(ref uint state)
        {
            state += 0x9E3779B9u;
            uint value = state;
            value = (value ^ (value >> 16)) * 0x85EBCA6Bu;
            value = (value ^ (value >> 13)) * 0xC2B2AE35u;
            return value ^ (value >> 16);
        }
    }
}
