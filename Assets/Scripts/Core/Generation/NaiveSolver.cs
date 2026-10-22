using System;
using System.Collections.Generic;

namespace WFC.Core
{
    /// <summary>
    /// Najprostszy rozwiązywacz, wzorzec poprawności dla pozostałych. Możliwości komórki to List&lt;int&gt;
    /// z numerami stanów rosnąco. Propagacja: dla komórki zdjętej z kolejki i każdego z 6 sąsiadów zostawia
    /// w sąsiedzie tylko stany, które mają co najmniej jeden pasujący stan w komórce (sprawdza pary stanów
    /// jedna po drugiej), aż nic się już nie zmienia. Dziennik zmian do cofania: kopia listy komórki przy jej pierwszej
    /// zmianie na danym poziomie.
    /// </summary>
    public sealed class NaiveSolver : SolverBase
    {
        List<int>[] possibilities;
        CellQueue queue;
        ulong[] allowed;
        int[] scratch;

        /// <summary>Numer poziomu, na którym komórkę ostatnio zapisano w dzienniku.</summary>
        int[] savedAt;
        readonly List<int> trailCells = new List<int>();
        readonly List<int> trailStart = new List<int>();
        readonly List<int> trailStates = new List<int>();
        int peakTrailCells;
        int peakTrailStates;

        public override string Name => "Naive";

        public override int GetPossibilityCount(int cell) => possibilities[cell].Count;

        public override int GetState(int cell) => possibilities[cell].Count == 1 ? possibilities[cell][0] : -1;

        /// <summary>
        /// Listy możliwości (obiekt listy 32 bajty, tablica 24 bajty nagłówka i 4 bajty na stan), kolejka propagacji
        /// (5 bajtów na komórkę), bufor stanów do losowania (4 bajty na stan) i dziennik zmian w szczycie
        /// (numer poziomu na komórkę, 8 bajtów na zapisaną komórkę i 4 na zapisany stan).
        /// </summary>
        protected override long StructureBytes =>
            (long)Grid.CellCount * (32 + 24 + 4L * Rules.StateCount) + queue.MemoryBytes + 4L * Rules.StateCount +
            (savedAt?.Length ?? 0) * 4L + peakTrailCells * 8L + peakTrailStates * 4L;

        protected override bool SupportsUndo => true;

        protected override bool SupportsLocalReset => true;

        protected override int TrailPosition => trailCells.Count;

        protected override void Allocate()
        {
            possibilities = new List<int>[Grid.CellCount];
            for (int cell = 0; cell < possibilities.Length; cell++)
                possibilities[cell] = new List<int>(Rules.StateCount);
            queue = new CellQueue(Grid.CellCount);
            allowed = new ulong[Rules.WordCount];
            scratch = new int[Rules.StateCount];
            savedAt = (Settings.ContradictionHandler.Needs & ContradictionNeeds.UndoLog) != 0 ? new int[Grid.CellCount] : null;
            peakTrailCells = 0;
            peakTrailStates = 0;
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
            ClearTrail(0);
        }

        protected override bool PropagateInitial()
        {
            for (int cell = 0; cell < possibilities.Length; cell++)
                queue.Enqueue(cell);
            return PropagateDecision();
        }

        protected override void Collapse(int cell, int state)
        {
            Save(cell);
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
                            else if (kept == i)
                                Save(neighbor); // pierwszy usunięty stan: lista jest jeszcze nienaruszona
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

        protected override void UndoTo(int position)
        {
            for (int entry = trailCells.Count - 1; entry >= position; entry--)
            {
                int cell = trailCells[entry];
                int start = trailStart[entry];
                int end = entry + 1 < trailStart.Count ? trailStart[entry + 1] : trailStates.Count;
                List<int> list = possibilities[cell];
                list.Clear();
                for (int i = start; i < end; i++)
                    list.Add(trailStates[i]);
                Changed.Enqueue(cell);
            }
            ClearTrail(position);
            queue.Clear();
        }

        protected override bool Ban(int cell, int state)
        {
            Save(cell);
            List<int> list = possibilities[cell];
            list.Remove(state);
            CountRemoved(1);
            Changed.Enqueue(cell);
            queue.Enqueue(cell);
            return list.Count > 0;
        }

        protected override void ResetCell(int cell)
        {
            FillAllowedByGridEdges(cell, allowed);
            List<int> list = possibilities[cell];
            list.Clear();
            for (int state = 0; state < Rules.StateCount; state++)
                if (BitMask.Get(allowed, state))
                    list.Add(state);
        }

        protected override void EnqueuePropagation(int cell) => queue.Enqueue(cell);

        protected override int ChooseState(int cell) => Choose(AsSpan(possibilities[cell]));

        protected override double CellEntropy(int cell) => WeightedChooser.Entropy(Rules, AsSpan(possibilities[cell]));

        /// <summary>Zapisuje listę komórki w dzienniku przy jej pierwszej zmianie na bieżącym poziomie.</summary>
        void Save(int cell)
        {
            if (!RecordingUndo || savedAt[cell] == UndoLevel)
                return;
            savedAt[cell] = UndoLevel;
            trailCells.Add(cell);
            trailStart.Add(trailStates.Count);
            trailStates.AddRange(possibilities[cell]);
            peakTrailCells = Math.Max(peakTrailCells, trailCells.Count);
            peakTrailStates = Math.Max(peakTrailStates, trailStates.Count);
        }

        void ClearTrail(int position)
        {
            if (position >= trailCells.Count)
                return;
            trailStates.RemoveRange(trailStart[position], trailStates.Count - trailStart[position]);
            trailStart.RemoveRange(position, trailStart.Count - position);
            trailCells.RemoveRange(position, trailCells.Count - position);
        }

        /// <summary>Lista możliwości jako ciągły fragment pamięci dla WeightedChooser.</summary>
        ReadOnlySpan<int> AsSpan(List<int> list)
        {
            list.CopyTo(scratch);
            return new ReadOnlySpan<int>(scratch, 0, list.Count);
        }
    }
}
