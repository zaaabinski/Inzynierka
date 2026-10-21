using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Najprostszy rozwiązywacz, wzorzec poprawności dla pozostałych. Możliwości komórki to List&lt;int&gt;
    /// z numerami stanów rosnąco. Propagacja: dla komórki zdjętej z kolejki i każdego z 6 sąsiadów zostawia
    /// w sąsiedzie tylko stany, które mają co najmniej jeden pasujący stan w komórce (sprawdza pary stanów
    /// jedna po drugiej), aż nic się już nie zmienia.
    /// </summary>
    public sealed class NaiveSolver : IWfcSolver
    {
        CompiledRules rules;
        SolverSettings settings;
        GridShape grid;
        int[] neighbors;
        uint seed;
        List<int>[] possibilities;
        DeterministicRandom random;
        SolverMetrics metrics;
        GenerationResult result;
        int attempt;

        readonly Queue<int> queue = new Queue<int>();
        bool[] queued;
        readonly List<int> changed = new List<int>();
        bool[] isChanged;
        int peakQueue;
        int peakChanged;
        int[] scratch = Array.Empty<int>();

        public string Name => "Naive";

        public bool IsFinished => result != null;

        public GenerationResult Result => result;

        public SolverMetrics Metrics => metrics;

        public GridShape Grid => grid;

        public int LastDecisionCell { get; private set; } = -1;

        public int LastContradictionCell { get; private set; } = -1;

        /// <summary>
        /// Listy możliwości (obiekt listy 32 bajty, tablica 24 bajty nagłówka i 4 bajty na stan), tablica sąsiadów
        /// (6 liczb int na komórkę), 2 tablice znaczników (po 1 bajcie na komórkę), kolejka i lista zmian w szczycie
        /// oraz struktury selektora.
        /// </summary>
        public long PeakMemoryBytes
        {
            get
            {
                if (possibilities == null)
                    return 0;
                long cells = grid.CellCount;
                long lists = cells * (32 + 24 + 4L * rules.StateCount);
                long neighborTable = cells * DirectionExtensions.Count * 4;
                long flags = cells * 2;
                long work = 4L * (peakQueue + peakChanged + rules.StateCount);
                return lists + neighborTable + flags + work + settings.Selector.MemoryBytes;
            }
        }

        public void Initialize(CompiledRules compiledRules, int3 size, uint mapSeed, SolverSettings solverSettings)
        {
            long start = Stopwatch.GetTimestamp();
            rules = compiledRules ?? throw new ArgumentNullException(nameof(compiledRules));
            settings = solverSettings ?? new SolverSettings();
            grid = new GridShape(size);
            seed = mapSeed;
            neighbors = grid.BuildNeighborTable();
            possibilities = new List<int>[grid.CellCount];
            queued = new bool[grid.CellCount];
            isChanged = new bool[grid.CellCount];
            metrics = default;
            result = null;
            attempt = 0;
            peakQueue = 0;
            peakChanged = 0;
            LastDecisionCell = -1;
            LastContradictionCell = -1;

            if (!BeginAttempt())
                HandleContradiction();
            metrics.TotalTicks += Stopwatch.GetTimestamp() - start;
        }

        public StepStatus Step()
        {
            if (result != null)
                return result.Success ? StepStatus.Finished : StepStatus.Failed;
            if (possibilities == null)
                throw new InvalidOperationException("Najpierw wywołaj Initialize.");

            long start = Stopwatch.GetTimestamp();
            try
            {
                if (settings.TimeLimitSeconds > 0 && SolverMetrics.ToMilliseconds(metrics.TotalTicks) > settings.TimeLimitSeconds * 1000.0)
                {
                    Finish(GenerationStatus.TimedOut);
                    return StepStatus.Failed;
                }

                int cell = settings.Selector.SelectNext();
                metrics.SelectionTicks += Stopwatch.GetTimestamp() - start;
                if (cell < 0)
                {
                    Finish(GenerationStatus.Success);
                    return StepStatus.Finished;
                }

                List<int> options = possibilities[cell];
                int state = WeightedChooser.Choose(rules, AsSpan(options), ref random);
                metrics.Decisions++;
                metrics.RemovedPossibilities += options.Count - 1;
                options.Clear();
                options.Add(state);
                LastDecisionCell = cell;
                MarkChanged(cell);
                Enqueue(cell);

                if (!Propagate())
                    return HandleContradiction();
                NotifyChangedCells();
                return StepStatus.Decided;
            }
            finally
            {
                metrics.TotalTicks += Stopwatch.GetTimestamp() - start;
            }
        }

        public GenerationResult Run()
        {
            while (Step() is StepStatus.Decided or StepStatus.Contradiction)
            {
            }
            return result;
        }

        public int GetPossibilityCount(int cell) => possibilities[cell].Count;

        public int GetState(int cell) => possibilities[cell].Count == 1 ? possibilities[cell][0] : -1;

        /// <summary>Nowa próba: stany dozwolone przez krawędzie siatki, losowy dodatek selektora, propagacja.</summary>
        bool BeginAttempt()
        {
            attempt++;
            metrics.Attempts = attempt;
            random = DeterministicRandom.ForAttempt(seed, attempt - 1);
            LastDecisionCell = -1;

            var allowed = new ulong[rules.WordCount];
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                FillAllowedByGridEdges(cell, allowed);
                List<int> list = possibilities[cell] ?? (possibilities[cell] = new List<int>(rules.StateCount));
                list.Clear();
                for (int state = 0; state < rules.StateCount; state++)
                    if (BitMask.Get(allowed, state))
                        list.Add(state);
                metrics.RemovedPossibilities += rules.StateCount - list.Count;
            }

            settings.Selector.Reset(grid, ref random);
            queue.Clear();
            changed.Clear();
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                queued[cell] = false;
                isChanged[cell] = false;
                if (possibilities[cell].Count == 0)
                {
                    LastContradictionCell = cell;
                    return false;
                }
                Enqueue(cell);
            }

            if (!Propagate())
                return false;
            changed.Clear();
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                isChanged[cell] = false;
                Notify(cell);
            }
            return true;
        }

        void FillAllowedByGridEdges(int cell, ulong[] allowed)
        {
            BitMask.SetFirst(allowed, rules.StateCount);
            int floor = grid.Floor(cell);
            if (floor == 0)
                And(allowed, rules.BottomMask);
            if (floor == grid.Floors - 1)
                And(allowed, rules.TopMask);
            foreach (Direction side in DirectionExtensions.Sides)
                if (neighbors[cell * DirectionExtensions.Count + (int)side] < 0)
                    And(allowed, rules.GetSideEdgeMask(side, floor == 0));
        }

        bool Propagate()
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    queued[cell] = false;
                    List<int> source = possibilities[cell];
                    foreach (Direction direction in DirectionExtensions.All)
                    {
                        int neighbor = neighbors[cell * DirectionExtensions.Count + (int)direction];
                        if (neighbor < 0)
                            continue;
                        metrics.PropagationSteps++;
                        List<int> target = possibilities[neighbor];
                        int kept = 0;
                        for (int i = 0; i < target.Count; i++)
                        {
                            int candidate = target[i];
                            bool supported = false;
                            for (int j = 0; j < source.Count && !supported; j++)
                                supported = rules.CanBeNeighbors(source[j], direction, candidate);
                            if (supported)
                                target[kept++] = candidate;
                        }
                        int removed = target.Count - kept;
                        if (removed == 0)
                            continue;
                        target.RemoveRange(kept, removed);
                        metrics.RemovedPossibilities += removed;
                        if (kept == 0)
                        {
                            LastContradictionCell = neighbor;
                            return false;
                        }
                        MarkChanged(neighbor);
                        Enqueue(neighbor);
                    }
                }
                return true;
            }
            finally
            {
                metrics.PropagationTicks += Stopwatch.GetTimestamp() - start;
            }
        }

        StepStatus HandleContradiction()
        {
            while (true)
            {
                metrics.Contradictions++;
                if (settings.ContradictionHandler.OnContradiction(attempt, settings.MaxAttempts) != ContradictionAction.Restart)
                {
                    Finish(GenerationStatus.Failed);
                    return StepStatus.Failed;
                }
                metrics.Restarts++;
                if (BeginAttempt())
                    return StepStatus.Contradiction;
            }
        }

        void Finish(GenerationStatus status)
        {
            var states = new int[grid.CellCount];
            for (int cell = 0; cell < grid.CellCount; cell++)
                states[cell] = status == GenerationStatus.Success ? possibilities[cell][0] : GetState(cell);
            result = new GenerationResult
            {
                Status = status,
                Size = grid.Size,
                Seed = seed,
                TilesetName = rules.TilesetName,
                SolverName = Name,
                SelectorName = settings.Selector.Name,
                ContradictionHandlerName = settings.ContradictionHandler.Name,
                States = states,
                Metrics = metrics,
                LastContradictionCell = LastContradictionCell
            };
        }

        void Enqueue(int cell)
        {
            if (queued[cell])
                return;
            queued[cell] = true;
            queue.Enqueue(cell);
            if (queue.Count > peakQueue)
                peakQueue = queue.Count;
        }

        void MarkChanged(int cell)
        {
            if (isChanged[cell])
                return;
            isChanged[cell] = true;
            changed.Add(cell);
            if (changed.Count > peakChanged)
                peakChanged = changed.Count;
        }

        void NotifyChangedCells()
        {
            foreach (int cell in changed)
            {
                isChanged[cell] = false;
                Notify(cell);
            }
            changed.Clear();
        }

        void Notify(int cell)
        {
            List<int> options = possibilities[cell];
            settings.Selector.OnCellChanged(cell, options.Count, WeightedChooser.Entropy(rules, AsSpan(options)));
        }

        /// <summary>Lista możliwości jako ciągły fragment pamięci dla WeightedChooser.</summary>
        ReadOnlySpan<int> AsSpan(List<int> list)
        {
            if (scratch.Length < list.Count)
                scratch = new int[rules.StateCount];
            list.CopyTo(scratch);
            return new ReadOnlySpan<int>(scratch, 0, list.Count);
        }

        static void And(Span<ulong> target, ReadOnlySpan<ulong> mask)
        {
            for (int i = 0; i < target.Length; i++)
                target[i] &= mask[i];
        }
    }
}
