using System;

namespace WFC.Core
{
    /// <summary>
    /// Operacje na maskach bitowych zapisanych w kolejnych liczbach ulong: bit i oznacza stan o numerze i.
    /// Dla 170 stanów maska ma 3 słowa (192 bity).
    /// </summary>
    public static class BitMask
    {
        public const int BitsPerWord = 64;

        public static int WordCount(int bitCount) => (bitCount + BitsPerWord - 1) / BitsPerWord;

        public static bool Get(ReadOnlySpan<ulong> mask, int bit) => (mask[bit >> 6] & (1UL << (bit & 63))) != 0;

        public static void Set(Span<ulong> mask, int bit) => mask[bit >> 6] |= 1UL << (bit & 63);

        public static void Clear(Span<ulong> mask, int bit) => mask[bit >> 6] &= ~(1UL << (bit & 63));

        public static int Count(ReadOnlySpan<ulong> mask)
        {
            int count = 0;
            foreach (ulong word in mask)
                count += PopCount(word);
            return count;
        }

        public static bool Intersects(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b)
        {
            for (int i = 0; i < a.Length; i++)
                if ((a[i] & b[i]) != 0)
                    return true;
            return false;
        }

        /// <summary>Ustawia bity 0 … bitCount − 1, wyższe bity ostatniego słowa zostają zerami.</summary>
        public static void SetFirst(Span<ulong> mask, int bitCount)
        {
            mask.Clear();
            for (int i = 0; i < bitCount; i++)
                Set(mask, i);
        }

        static int PopCount(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }
    }
}
