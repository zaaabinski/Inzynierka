using System;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Rozwiązywacz z licznikami wsparcia (pomysł algorytmu AC-4, arc consistency 4). Dla każdej komórki, kierunku
    /// i stanu licznik mówi, ile stanów sąsiada z tej strony go dopuszcza. Usunięcie stanu zmniejsza liczniki
    /// stanów, które dopuszczał, u 6 sąsiadów; stan, któremu licznik spadł do zera, też wypada. Propagacja nie sprawdza
    /// więc par stanów od nowa, za to liczniki zajmują 2 bajty × 6 kierunków × liczba stanów na komórkę.
    /// Możliwości komórki to maska bitowa i liczba stanów.
    /// </summary>
    public sealed class SupportCountSolver : SolverBase
    {
        const int Directions = DirectionExtensions.Count;

        int stateCount;
        int words;
        ulong[] possibilities;
        int[] counts;

        /// <summary>Licznik wsparcia: indeks (komórka × 6 + kierunek) × liczba stanów + stan; kierunek od wspierającego sąsiada do komórki.</summary>
        ushort[] support;

        /// <summary>
        /// Początkowe liczniki dla komórki, której sąsiad ma dane położenie przy krawędziach siatki (6 bitów jak
        /// w EdgeClass): indeks kierunek × liczba stanów + stan. Te same w każdej próbie, więc liczone raz.
        /// </summary>
        ushort[][] initialSupport;

        /// <summary>Stany, które dopuszczają stan a w kierunku d: indeks (a × 6 + d) × WordCount.</summary>
        ulong[] supporters;

        /// <summary>Usunięte stany do przetworzenia: komórka × liczba stanów + stan.</summary>
        int[] removals = Array.Empty<int>();
        int removalCount;
        int peakRemovals;

        public override string Name => "SupportCount";

        public override int GetPossibilityCount(int cell) => counts[cell];

        public override int GetState(int cell) => counts[cell] == 1 ? BitMask.First(Mask(cell)) : -1;

        /// <summary>
        /// Liczniki wsparcia (2 bajty × 6 × liczba stanów na komórkę), maski możliwości (8 bajtów na słowo) i liczby
        /// stanów (4 bajty) na komórkę, stos usuniętych stanów w szczycie (4 bajty na wpis) i maski wspierających stanów.
        /// </summary>
        protected override long StructureBytes =>
            (long)Grid.CellCount * (Directions * stateCount * 2 + words * 8 + 4) + 4L * peakRemovals + supporters.Length * 8L;

        protected override void Allocate()
        {
            stateCount = Rules.StateCount;
            words = Rules.WordCount;
            int cells = Grid.CellCount;
            possibilities = new ulong[cells * words];
            counts = new int[cells];
            support = new ushort[cells * Directions * stateCount];
            initialSupport = new ushort[1 << Directions][];
            peakRemovals = 0;
            if (removals.Length < cells)
                removals = new int[cells];

            supporters = new ulong[stateCount * Directions * words];
            for (int state = 0; state < stateCount; state++)
                for (int direction = 0; direction < Directions; direction++)
                    foreach (int neighbor in Rules.GetNeighbors(state, (Direction)direction))
                        BitMask.Set(supporters.AsSpan((neighbor * Directions + direction) * words, words), state);
        }

        protected override void ResetPossibilities()
        {
            for (int cell = 0; cell < counts.Length; cell++)
            {
                Span<ulong> mask = possibilities.AsSpan(cell * words, words);
                FillAllowedByGridEdges(cell, mask);
                counts[cell] = BitMask.Count(mask);
                CountRemoved(stateCount - counts[cell]);
            }
            removalCount = 0;
        }

        /// <summary>
        /// Liczniki z początkowych możliwości sąsiadów, potem usunięcie stanów, którym któryś sąsiad nie daje wsparcia,
        /// i zwykła propagacja z ich listy.
        /// </summary>
        protected override bool PropagateInitial()
        {
            for (int cell = 0; cell < counts.Length; cell++)
                for (int direction = 0; direction < Directions; direction++)
                {
                    int from = Neighbors[cell * Directions + (int)((Direction)direction).Opposite()];
                    if (from >= 0)
                        Array.Copy(InitialSupport(from), direction * stateCount, support, (cell * Directions + direction) * stateCount, stateCount);
                }

            for (int cell = 0; cell < counts.Length; cell++)
            {
                Span<ulong> mask = Mask(cell);
                for (int word = 0; word < words; word++)
                    for (ulong bits = mask[word]; bits != 0; bits &= bits - 1)
                    {
                        int state = word * BitMask.BitsPerWord + math.tzcnt(bits);
                        if (!IsSupported(cell, state))
                            Remove(cell, state);
                    }
                if (counts[cell] == 0)
                {
                    LastContradictionCell = cell;
                    return false;
                }
            }
            return PropagateDecision();
        }

        protected override void Collapse(int cell, int state)
        {
            Span<ulong> mask = Mask(cell);
            for (int word = 0; word < words; word++)
                for (ulong bits = mask[word]; bits != 0; bits &= bits - 1)
                {
                    int other = word * BitMask.BitsPerWord + math.tzcnt(bits);
                    if (other != state)
                        PushRemoval(cell, other);
                }
            mask.Clear();
            BitMask.Set(mask, state);
            counts[cell] = 1;
        }

        protected override bool PropagateDecision()
        {
            long steps = 0;
            long removed = 0;
            try
            {
                while (removalCount > 0)
                {
                    int removal = removals[--removalCount];
                    int cell = removal / stateCount;
                    int state = removal - cell * stateCount;
                    for (int direction = 0; direction < Directions; direction++)
                    {
                        int neighbor = Neighbors[cell * Directions + direction];
                        if (neighbor < 0)
                            continue;
                        steps++;
                        int start = (neighbor * Directions + direction) * stateCount;
                        foreach (int candidate in Rules.GetNeighbors(state, (Direction)direction))
                        {
                            if (--support[start + candidate] != 0 || !Has(neighbor, candidate))
                                continue;
                            BitMask.Clear(Mask(neighbor), candidate);
                            removed++;
                            if (--counts[neighbor] == 0)
                            {
                                LastContradictionCell = neighbor;
                                return false;
                            }
                            Changed.Enqueue(neighbor);
                            PushRemoval(neighbor, candidate);
                        }
                    }
                }
                return true;
            }
            finally
            {
                CountPropagationSteps(steps);
                CountRemoved(removed);
            }
        }

        protected override int ChooseState(int cell) => Choose(Mask(cell));

        protected override double CellEntropy(int cell) => WeightedChooser.Entropy(Rules, Mask(cell));

        Span<ulong> Mask(int cell) => possibilities.AsSpan(cell * words, words);

        bool Has(int cell, int state) => (possibilities[cell * words + (state >> 6)] & (1UL << (state & 63))) != 0;

        /// <summary>Czy każdy istniejący sąsiad ma co najmniej jeden stan, który dopuszcza ten stan komórki.</summary>
        bool IsSupported(int cell, int state)
        {
            for (int direction = 0; direction < Directions; direction++)
                if (Neighbors[cell * Directions + (int)((Direction)direction).Opposite()] >= 0 &&
                    support[(cell * Directions + direction) * stateCount + state] == 0)
                    return false;
            return true;
        }

        /// <summary>Usuwa stan przy starcie próby (brak wsparcia) i dodaje go do propagacji.</summary>
        void Remove(int cell, int state)
        {
            BitMask.Clear(Mask(cell), state);
            counts[cell]--;
            CountRemoved(1);
            Changed.Enqueue(cell);
            PushRemoval(cell, state);
        }

        void PushRemoval(int cell, int state)
        {
            if (removalCount == removals.Length)
                Array.Resize(ref removals, removals.Length * 2);
            removals[removalCount++] = cell * stateCount + state;
            if (removalCount > peakRemovals)
                peakRemovals = removalCount;
        }

        /// <summary>
        /// Położenie komórki przy krawędziach siatki: parter, ostatnie piętro i boki bez sąsiada. Od niego zależą
        /// początkowe możliwości (FillAllowedByGridEdges).
        /// </summary>
        int EdgeClass(int cell)
        {
            int floor = Grid.Floor(cell);
            int edgeClass = (floor == 0 ? 1 : 0) | (floor == Grid.Floors - 1 ? 2 : 0);
            foreach (Direction side in DirectionExtensions.Sides)
                if (Neighbors[cell * Directions + (int)side] < 0)
                    edgeClass |= 4 << (int)side;
            return edgeClass;
        }

        /// <summary>Liczniki, które daje komórka from swoim sąsiadom na początku próby, dla każdego kierunku.</summary>
        ushort[] InitialSupport(int from)
        {
            int edgeClass = EdgeClass(from);
            ushort[] counters = initialSupport[edgeClass];
            if (counters != null)
                return counters;
            Span<ulong> allowed = stackalloc ulong[words];
            FillAllowedByGridEdges(from, allowed);
            counters = new ushort[Directions * stateCount];
            for (int direction = 0; direction < Directions; direction++)
                for (int state = 0; state < stateCount; state++)
                {
                    ReadOnlySpan<ulong> mask = supporters.AsSpan((state * Directions + direction) * words, words);
                    int count = 0;
                    for (int word = 0; word < words; word++)
                        count += math.countbits(allowed[word] & mask[word]);
                    counters[direction * stateCount + state] = (ushort)count;
                }
            return initialSupport[edgeClass] = counters;
        }
    }
}
