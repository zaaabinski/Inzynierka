using System;

namespace WFC.Core
{
    /// <summary>
    /// Rozwiązywacz na maskach bitowych: możliwości komórki to WordCount liczb ulong (3 dla 170 stanów), a propagacja
    /// usuwa naraz wszystkie stany sąsiada bez wsparcia (BitsetPropagation). Baza eksperymentów E2–E6. Dziennik zmian
    /// do cofania: maska komórki przy jej pierwszej zmianie na danym poziomie.
    /// </summary>
    public class BitsetSolver : SolverBase
    {
        int words;
        ulong[] possibilities;
        CellQueue queue;

        /// <summary>Numer poziomu, na którym komórkę ostatnio zapisano w dzienniku; null bez cofania decyzji.</summary>
        int[] savedAt;
        int[] trailCells = Array.Empty<int>();
        ulong[] trailMasks = Array.Empty<ulong>();
        int trailCount;

        public override string Name => "Bitset";

        public override int GetPossibilityCount(int cell) => BitMask.Count(Mask(cell));

        public override int GetState(int cell)
        {
            Span<ulong> mask = Mask(cell);
            return BitMask.Count(mask) == 1 ? BitMask.First(mask) : -1;
        }

        /// <summary>
        /// Maski możliwości (8 bajtów na słowo na komórkę), kolejka propagacji (5 bajtów na komórkę) i dziennik zmian
        /// (numer poziomu na komórkę, komórka i maska na wpis, według zajętego miejsca).
        /// </summary>
        protected override long StructureBytes =>
            possibilities.Length * 8L + queue.MemoryBytes + (savedAt?.Length ?? 0) * 4L + trailCells.Length * 4L + trailMasks.Length * 8L;

        protected override bool SupportsUndo => true;

        protected override bool SupportsLocalReset => true;

        protected override int TrailPosition => trailCount;

        protected override void Allocate()
        {
            words = Rules.WordCount;
            if (words > BitsetPropagation.MaxWords)
                throw new NotSupportedException($"{Name}: najwyżej {BitsetPropagation.MaxWords * BitMask.BitsPerWord} stanów, zestaw ma {Rules.StateCount}.");
            possibilities = new ulong[Grid.CellCount * words];
            queue = new CellQueue(Grid.CellCount);
            bool undo = (Settings.ContradictionHandler.Needs & ContradictionNeeds.UndoLog) != 0;
            savedAt = undo ? new int[Grid.CellCount] : null;
            trailCells = undo ? new int[Grid.CellCount] : Array.Empty<int>();
            trailMasks = undo ? new ulong[Grid.CellCount * words] : Array.Empty<ulong>();
            trailCount = 0;
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
            trailCount = 0;
        }

        protected override bool PropagateInitial()
        {
            for (int cell = 0; cell < Grid.CellCount; cell++)
                queue.Enqueue(cell);
            return PropagateDecision();
        }

        protected override void Collapse(int cell, int state)
        {
            Save(cell);
            Span<ulong> mask = Mask(cell);
            mask.Clear();
            BitMask.Set(mask, state);
            queue.Enqueue(cell);
        }

        protected override unsafe bool PropagateDecision()
        {
            CellQueue changed = Changed;
            while (true)
            {
                fixed (ulong* cellMasks = possibilities)
                fixed (int* neighbors = Neighbors)
                fixed (ulong* neighborMasks = Rules.NeighborMaskArray)
                fixed (int* queued = queue.Items)
                fixed (byte* queuedFlags = queue.Flags)
                fixed (int* changedCells = changed.Items)
                fixed (byte* changedFlags = changed.Flags)
                fixed (int* saved = savedAt)
                fixed (int* savedCells = trailCells)
                fixed (ulong* savedMasks = trailMasks)
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
                        ChangedPeak = changed.Peak,
                        RecordUndo = RecordingUndo ? 1 : 0,
                        UndoLevel = UndoLevel,
                        SavedAt = saved,
                        TrailCells = savedCells,
                        TrailMasks = savedMasks,
                        TrailCount = trailCount,
                        TrailCapacity = trailCells.Length
                    };
                    RunPropagation(ref data);

                    queue.Head = data.QueueHead;
                    queue.Count = data.QueueCount;
                    queue.Peak = data.QueuePeak;
                    changed.Count = data.ChangedCount;
                    changed.Peak = data.ChangedPeak;
                    trailCount = data.TrailCount;
                    CountPropagationSteps(data.PropagationSteps);
                    CountRemoved(data.RemovedPossibilities);
                    if (data.NeedsTrailSpace == 0)
                    {
                        if (data.ContradictionCell < 0)
                            return true;
                        LastContradictionCell = data.ContradictionCell;
                        return false;
                    }
                }
                GrowTrail();
            }
        }

        /// <summary>Propagacja jako zwykły C#; BurstSolver podmienia ją na wersję skompilowaną przez Burst.</summary>
        protected virtual void RunPropagation(ref BitsetPropagationData data) => BitsetPropagation.Run(ref data);

        protected override void UndoTo(int position)
        {
            for (int entry = trailCount - 1; entry >= position; entry--)
            {
                int cell = trailCells[entry];
                trailMasks.AsSpan(entry * words, words).CopyTo(Mask(cell));
                Changed.Enqueue(cell);
            }
            trailCount = position;
            queue.Clear();
        }

        protected override bool Ban(int cell, int state)
        {
            Save(cell);
            Span<ulong> mask = Mask(cell);
            BitMask.Clear(mask, state);
            CountRemoved(1);
            Changed.Enqueue(cell);
            queue.Enqueue(cell);
            return BitMask.Count(mask) > 0;
        }

        protected override void ResetCell(int cell) => FillAllowedByGridEdges(cell, Mask(cell));

        protected override void EnqueuePropagation(int cell) => queue.Enqueue(cell);

        protected override int ChooseState(int cell) => Choose(Mask(cell));

        protected override double CellEntropy(int cell) => WeightedChooser.Entropy(Rules, Mask(cell));

        Span<ulong> Mask(int cell) => possibilities.AsSpan(cell * words, words);

        /// <summary>Zapisuje maskę komórki w dzienniku przy jej pierwszej zmianie na bieżącym poziomie (decyzja albo wykreślenie).</summary>
        void Save(int cell)
        {
            if (!RecordingUndo || savedAt[cell] == UndoLevel)
                return;
            if (trailCount == trailCells.Length)
                GrowTrail();
            savedAt[cell] = UndoLevel;
            trailCells[trailCount] = cell;
            Mask(cell).CopyTo(trailMasks.AsSpan(trailCount * words, words));
            trailCount++;
        }

        void GrowTrail()
        {
            int capacity = Math.Max(trailCells.Length * 2, DirectionExtensions.Count * 2);
            Array.Resize(ref trailCells, capacity);
            Array.Resize(ref trailMasks, capacity * words);
        }
    }
}
