using System;

namespace WFC.Core
{
    /// <summary>
    /// Rozwiązywacz na maskach bitowych: możliwości komórki to WordCount liczb ulong (3 dla 170 stanów), a propagacja
    /// usuwa naraz wszystkie stany sąsiada bez wsparcia (BitsetPropagation). Baza eksperymentów E2–E6.
    /// </summary>
    public class BitsetSolver : SolverBase
    {
        int words;
        ulong[] possibilities;
        CellQueue queue;

        public override string Name => "Bitset";

        public override int GetPossibilityCount(int cell) => BitMask.Count(Mask(cell));

        public override int GetState(int cell)
        {
            Span<ulong> mask = Mask(cell);
            return BitMask.Count(mask) == 1 ? BitMask.First(mask) : -1;
        }

        /// <summary>Maski możliwości (8 bajtów na słowo na komórkę) i kolejka propagacji (5 bajtów na komórkę).</summary>
        protected override long StructureBytes => possibilities.Length * 8L + queue.MemoryBytes;

        protected override void Allocate()
        {
            words = Rules.WordCount;
            if (words > BitsetPropagation.MaxWords)
                throw new NotSupportedException($"{Name}: najwyżej {BitsetPropagation.MaxWords * BitMask.BitsPerWord} stanów, zestaw ma {Rules.StateCount}.");
            possibilities = new ulong[Grid.CellCount * words];
            queue = new CellQueue(Grid.CellCount);
        }

        protected override void ResetPossibilities()
        {
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                Span<ulong> mask = Mask(cell);
                FillAllowedByGridEdges(cell, mask);
                CountRemoved(Rules.StateCount - BitMask.Count(mask));
            }
            queue.Clear();
        }

        protected override bool PropagateInitial()
        {
            for (int cell = 0; cell < Grid.CellCount; cell++)
                queue.Enqueue(cell);
            return PropagateDecision();
        }

        protected override void Collapse(int cell, int state)
        {
            Span<ulong> mask = Mask(cell);
            mask.Clear();
            BitMask.Set(mask, state);
            queue.Enqueue(cell);
        }

        protected override unsafe bool PropagateDecision()
        {
            CellQueue changed = Changed;
            fixed (ulong* cellMasks = possibilities)
            fixed (int* neighbors = Neighbors)
            fixed (ulong* neighborMasks = Rules.NeighborMaskArray)
            fixed (int* queued = queue.Items)
            fixed (byte* queuedFlags = queue.Flags)
            fixed (int* changedCells = changed.Items)
            fixed (byte* changedFlags = changed.Flags)
            {
                var data = new BitsetPropagationData
                {
                    Possibilities = cellMasks,
                    Neighbors = neighbors,
                    NeighborMasks = neighborMasks,
                    Words = words,
                    CellCount = Grid.CellCount,
                    Queue = queued,
                    Queued = queuedFlags,
                    QueueHead = queue.Head,
                    QueueCount = queue.Count,
                    QueuePeak = queue.Peak,
                    Changed = changedCells,
                    ChangedFlags = changedFlags,
                    ChangedHead = changed.Head,
                    ChangedCount = changed.Count,
                    ChangedPeak = changed.Peak
                };
                RunPropagation(ref data);

                queue.Head = data.QueueHead;
                queue.Count = data.QueueCount;
                queue.Peak = data.QueuePeak;
                changed.Count = data.ChangedCount;
                changed.Peak = data.ChangedPeak;
                CountPropagationSteps(data.PropagationSteps);
                CountRemoved(data.RemovedPossibilities);
                if (data.ContradictionCell < 0)
                    return true;
                LastContradictionCell = data.ContradictionCell;
                return false;
            }
        }

        /// <summary>Propagacja jako zwykły C#; BurstSolver podmienia ją na wersję skompilowaną przez Burst.</summary>
        protected virtual void RunPropagation(ref BitsetPropagationData data) => BitsetPropagation.Run(ref data);

        protected override int ChooseState(int cell) => Choose(Mask(cell));

        protected override double CellEntropy(int cell) => WeightedChooser.Entropy(Rules, Mask(cell));

        Span<ulong> Mask(int cell) => possibilities.AsSpan(cell * words, words);
    }
}
