using Unity.Burst;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Tablice i liczniki jednej propagacji na maskach bitowych, przekazywane przez wskaźniki, żeby ten sam kod działał
    /// jako zwykły C# (BitsetSolver) i po kompilacji przez Burst (BurstSolver). Kolejki mają układ jak w CellQueue.
    /// </summary>
    public unsafe struct BitsetPropagationData
    {
        /// <summary>Maski możliwości: komórka × Words słów.</summary>
        public ulong* Possibilities;

        /// <summary>Sąsiedzi: komórka × 6 + kierunek, −1 poza siatką.</summary>
        public int* Neighbors;

        /// <summary>Maski sąsiedztwa: (stan × 6 + kierunek) × Words.</summary>
        public ulong* NeighborMasks;

        public int Words;
        public int CellCount;

        public int* Queue;
        public byte* Queued;
        public int QueueHead;
        public int QueueCount;
        public int QueuePeak;

        public int* Changed;
        public byte* ChangedFlags;
        public int ChangedHead;
        public int ChangedCount;
        public int ChangedPeak;

        public long PropagationSteps;
        public long RemovedPossibilities;

        /// <summary>Komórka, która straciła ostatnią możliwość, albo −1.</summary>
        public int ContradictionCell;
    }

    /// <summary>
    /// Propagacja na maskach bitowych: dla komórki zdjętej z kolejki suma (OR) masek sąsiedztwa jej stanów daje
    /// w każdym kierunku zbiór stanów dozwolonych u sąsiada, a iloczyn (AND) z maską sąsiada usuwa resztę naraz.
    /// Zamiast sprawdzać pary stanów jedna po drugiej, jak NaiveSolver, przetwarza 64 stany w jednej operacji.
    /// </summary>
    public static unsafe class BitsetPropagation
    {
        /// <summary>Najwięcej słów na maskę (512 stanów); bufor sum leży na stosie.</summary>
        public const int MaxWords = 8;

        const int Directions = DirectionExtensions.Count;

        public static void Run(ref BitsetPropagationData data)
        {
            int words = data.Words;
            int span = Directions * words;
            ulong* allowed = stackalloc ulong[Directions * MaxWords];
            data.ContradictionCell = -1;

            while (data.QueueCount > 0)
            {
                int cell = data.Queue[data.QueueHead];
                data.QueueHead = data.QueueHead + 1 == data.CellCount ? 0 : data.QueueHead + 1;
                data.QueueCount--;
                data.Queued[cell] = 0;

                for (int i = 0; i < span; i++)
                    allowed[i] = 0;
                ulong* source = data.Possibilities + cell * words;
                for (int word = 0; word < words; word++)
                    for (ulong bits = source[word]; bits != 0; bits &= bits - 1)
                    {
                        ulong* masks = data.NeighborMasks + (word * BitMask.BitsPerWord + math.tzcnt(bits)) * span;
                        for (int i = 0; i < span; i++)
                            allowed[i] |= masks[i];
                    }

                int* around = data.Neighbors + cell * Directions;
                for (int direction = 0; direction < Directions; direction++)
                {
                    int neighbor = around[direction];
                    if (neighbor < 0)
                        continue;
                    data.PropagationSteps++;
                    ulong* target = data.Possibilities + neighbor * words;
                    ulong* mask = allowed + direction * words;
                    int removed = 0;
                    ulong left = 0;
                    for (int word = 0; word < words; word++)
                    {
                        ulong kept = target[word] & mask[word];
                        removed += math.countbits(target[word] ^ kept);
                        target[word] = kept;
                        left |= kept;
                    }
                    if (removed == 0)
                        continue;
                    data.RemovedPossibilities += removed;
                    if (left == 0)
                    {
                        data.ContradictionCell = neighbor;
                        return;
                    }
                    Push(data.Changed, data.ChangedFlags, data.ChangedHead, ref data.ChangedCount, ref data.ChangedPeak, data.CellCount, neighbor);
                    Push(data.Queue, data.Queued, data.QueueHead, ref data.QueueCount, ref data.QueuePeak, data.CellCount, neighbor);
                }
            }
        }

        static void Push(int* items, byte* flags, int head, ref int count, ref int peak, int capacity, int cell)
        {
            if (flags[cell] != 0)
                return;
            flags[cell] = 1;
            int tail = head + count;
            items[tail < capacity ? tail : tail - capacity] = cell;
            count++;
            if (count > peak)
                peak = count;
        }
    }

    /// <summary>
    /// Ta sama propagacja skompilowana przez Burst. Wywołanie z C# trafia wprost do kodu maszynowego Burst;
    /// bez Burst (np. wyłączony w edytorze albo poza Unity) wykonuje się zwykły C#.
    /// </summary>
    [BurstCompile]
    public static unsafe class BurstPropagation
    {
        [BurstCompile(CompileSynchronously = true)]
        public static void Run(ref BitsetPropagationData data) => BitsetPropagation.Run(ref data);

        /// <summary>Zostawia managed = 1 tylko w zwykłym C#: w kodzie Burst metoda z BurstDiscard znika.</summary>
        [BurstCompile(CompileSynchronously = true)]
        public static void Probe(ref int managed)
        {
            managed = 0;
            MarkManaged(ref managed);
        }

        [BurstDiscard]
        static void MarkManaged(ref int managed) => managed = 1;
    }
}
