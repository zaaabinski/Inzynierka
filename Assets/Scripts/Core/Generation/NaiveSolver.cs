using System;
using System.Collections.Generic;

namespace WFC.Core
{
    /// <summary>
    /// Najprostszy rozwiązywacz, wzorzec poprawności dla pozostałych. Możliwości komórki to List&lt;int&gt;
    /// z numerami stanów rosnąco. Propagacja: dla komórki zdjętej z kolejki i każdego z 6 sąsiadów zostawia
    /// w sąsiedzie tylko stany, które mają co najmniej jeden pasujący stan w komórce (sprawdza pary stanów
    /// jedna po drugiej), aż nic się już nie zmienia.
    /// </summary>
    public sealed class NaiveSolver : SolverBase
    {
        List<int>[] possibilities;
        CellQueue queue;
        ulong[] allowed;
        int[] scratch;

        public override string Name => "Naive";

        public override int GetPossibilityCount(int cell) => possibilities[cell].Count;

        public override int GetState(int cell) => possibilities[cell].Count == 1 ? possibilities[cell][0] : -1;

        /// <summary>
        /// Listy możliwości (obiekt listy 32 bajty, tablica 24 bajty nagłówka i 4 bajty na stan), kolejka propagacji
        /// (5 bajtów na komórkę) i bufor stanów do losowania (4 bajty na stan).
        /// </summary>
        protected override long StructureBytes =>
            (long)Grid.CellCount * (32 + 24 + 4L * Rules.StateCount) + queue.MemoryBytes + 4L * Rules.StateCount;

        protected override void Allocate()
        {
            possibilities = new List<int>[Grid.CellCount];
            for (int cell = 0; cell < possibilities.Length; cell++)
                possibilities[cell] = new List<int>(Rules.StateCount);
            queue = new CellQueue(Grid.CellCount);
            allowed = new ulong[Rules.WordCount];
            scratch = new int[Rules.StateCount];
        }

        protected override void ResetPossibilities()
        {
            for (int cell = 0; cell < possibilities.Length; cell++)
            {
                FillAllowedByGridEdges(cell, allowed);
                List<int> list = possibilities[cell];
                list.Clear();
                for (int state = 0; state < Rules.StateCount; state++)
                    if (BitMask.Get(allowed, state))
                        list.Add(state);
                CountRemoved(Rules.StateCount - list.Count);
            }
            queue.Clear();
        }

        protected override bool PropagateInitial()
        {
            for (int cell = 0; cell < possibilities.Length; cell++)
                queue.Enqueue(cell);
            return PropagateDecision();
        }

        protected override void Collapse(int cell, int state)
        {
            List<int> options = possibilities[cell];
            options.Clear();
            options.Add(state);
            queue.Enqueue(cell);
        }

        protected override bool PropagateDecision()
        {
            long steps = 0;
            try
            {
                while (queue.TryDequeue(out int cell))
                {
                    List<int> source = possibilities[cell];
                    foreach (Direction direction in DirectionExtensions.All)
                    {
                        int neighbor = Neighbors[cell * DirectionExtensions.Count + (int)direction];
                        if (neighbor < 0)
                            continue;
                        steps++;
                        List<int> target = possibilities[neighbor];
                        int kept = 0;
                        for (int i = 0; i < target.Count; i++)
                        {
                            int candidate = target[i];
                            bool supported = false;
                            for (int j = 0; j < source.Count && !supported; j++)
                                supported = Rules.CanBeNeighbors(source[j], direction, candidate);
                            if (supported)
                                target[kept++] = candidate;
                        }
                        int removed = target.Count - kept;
                        if (removed == 0)
                            continue;
                        target.RemoveRange(kept, removed);
                        CountRemoved(removed);
                        if (kept == 0)
                        {
                            LastContradictionCell = neighbor;
                            return false;
                        }
                        Changed.Enqueue(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
                return true;
            }
            finally
            {
                CountPropagationSteps(steps);
            }
        }

        protected override int ChooseState(int cell) => Choose(AsSpan(possibilities[cell]));

        protected override double CellEntropy(int cell) => WeightedChooser.Entropy(Rules, AsSpan(possibilities[cell]));

        /// <summary>Lista możliwości jako ciągły fragment pamięci dla WeightedChooser.</summary>
        ReadOnlySpan<int> AsSpan(List<int> list)
        {
            list.CopyTo(scratch);
            return new ReadOnlySpan<int>(scratch, 0, list.Count);
        }
    }
}
