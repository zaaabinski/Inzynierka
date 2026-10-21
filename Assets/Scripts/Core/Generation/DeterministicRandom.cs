using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Generator liczb losowych z ziarnem: Unity.Mathematics.Random, więc ten sam ciąg liczb w zwykłym C# i w Burst.
    /// Każda próba generacji dostaje własny generator z ziarna mapy i numeru próby.
    /// </summary>
    public struct DeterministicRandom
    {
        Random random;

        public DeterministicRandom(uint seed)
        {
            random = Random.CreateFromIndex(seed);
        }

        /// <summary>Generator dla próby attempt (0 = pierwsza) mapy o ziarnie seed.</summary>
        public static DeterministicRandom ForAttempt(uint seed, int attempt) =>
            new DeterministicRandom(math.hash(new uint2(seed, (uint)attempt)));

        /// <summary>Liczba z przedziału [0, 1).</summary>
        public double NextDouble() => random.NextDouble();

        /// <summary>Liczba całkowita z przedziału [0, max).</summary>
        public int NextInt(int max) => random.NextInt(max);

        public uint State => random.state;
    }
}
