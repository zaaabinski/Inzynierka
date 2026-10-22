using System;
using System.Diagnostics;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Wspólny przebieg generacji dla wszystkich rozwiązywaczy: próby, decyzje, reakcja na sprzeczność, pomiar czasu
    /// i powiadamianie heurystyki. Rozwiązywacze różnią się tylko przechowywaniem możliwości i propagacją.
    /// <para>
    /// Mapy wychodzą identyczne we wszystkich rozwiązywaczach, bo: generator liczb losowych zależy tylko od ziarna i numeru
    /// próby; propagacja zawsze dochodzi do tego samego stanu (największego zbioru możliwości zgodnego z sąsiadami),
    /// niezależnie od kolejności; heurystyka dostaje liczbę możliwości i entropię liczoną po stanach rosnąco,
    /// a jej wybór nie zależy od kolejności powiadomień. Sprzeczność pojawia się wtedy, gdy ten stan ma pustą komórkę,
    /// więc też w tej samej próbie.
    /// </para>
    /// </summary>
    public abstract class SolverBase : IWfcSolver
    {
        DeterministicRandom random;
        SolverMetrics metrics;
        GenerationResult result;
        CellQueue changed;
        uint seed;
        int attempt;
        bool initialized;

        protected CompiledRules Rules { get; private set; }

        protected SolverSettings Settings { get; private set; }

        /// <summary>Sąsiedzi: indeks komórka × 6 + kierunek, −1 poza siatką.</summary>
        protected int[] Neighbors { get; private set; }

        /// <summary>Komórki zmienione od ostatniego powiadomienia heurystyki.</summary>
        protected CellQueue Changed => changed;

        public abstract string Name { get; }

        public bool IsFinished => result != null;

        public GenerationResult Result => result;

        public SolverMetrics Metrics => metrics;

        public GridShape Grid { get; private set; }

        public int LastDecisionCell { get; private set; } = -1;

        public int LastContradictionCell { get; protected set; } = -1;

        /// <summary>
        /// Struktury rozwiązywacza (StructureBytes), tablica sąsiadów (6 liczb int na komórkę), lista zmienionych komórek
        /// (5 bajtów na komórkę) i struktury heurystyki.
        /// </summary>
        public long PeakMemoryBytes =>
            initialized
                ? StructureBytes + (long)Grid.CellCount * DirectionExtensions.Count * 4 + changed.MemoryBytes + Settings.Selector.MemoryBytes
                : 0;

        public void Initialize(CompiledRules compiledRules, int3 size, uint mapSeed, SolverSettings solverSettings)
        {
            long start = Stopwatch.GetTimestamp();
            Rules = compiledRules ?? throw new ArgumentNullException(nameof(compiledRules));
            Settings = solverSettings ?? new SolverSettings();
            Grid = new GridShape(size);
            seed = mapSeed;
            Neighbors = Grid.BuildNeighborTable();
            changed = new CellQueue(Grid.CellCount);
            metrics = default;
            result = null;
            attempt = 0;
            LastDecisionCell = -1;
            LastContradictionCell = -1;
            Allocate();
            initialized = true;

            if (!BeginAttempt())
                HandleContradiction();
            metrics.TotalTicks += Stopwatch.GetTimestamp() - start;
        }

        public StepStatus Step()
        {
            if (result != null)
                return result.Success ? StepStatus.Finished : StepStatus.Failed;
            if (!initialized)
                throw new InvalidOperationException("Najpierw wywołaj Initialize.");

            long start = Stopwatch.GetTimestamp();
            try
            {
                if (Settings.TimeLimitSeconds > 0 && SolverMetrics.ToMilliseconds(metrics.TotalTicks) > Settings.TimeLimitSeconds * 1000.0)
                {
                    Finish(GenerationStatus.TimedOut);
                    return StepStatus.Failed;
                }

                int cell = Settings.Selector.SelectNext();
                metrics.SelectionTicks += Stopwatch.GetTimestamp() - start;
                if (cell < 0)
                {
                    Finish(GenerationStatus.Success);
                    return StepStatus.Finished;
                }

                int state = ChooseState(cell);
                metrics.Decisions++;
                metrics.RemovedPossibilities += GetPossibilityCount(cell) - 1;
                Collapse(cell, state);
                LastDecisionCell = cell;
                changed.Enqueue(cell);

                if (!Propagate(false))
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

        public abstract int GetPossibilityCount(int cell);

        public abstract int GetState(int cell);

        /// <summary>Tworzy tablice na możliwości po ustawieniu Rules, Grid i Neighbors.</summary>
        protected abstract void Allocate();

        /// <summary>
        /// Początek próby: każdej komórce stany dozwolone przez krawędzie siatki (FillAllowedByGridEdges), usunięte
        /// zliczone przez CountRemoved; puste kolejki propagacji.
        /// </summary>
        protected abstract void ResetPossibilities();

        /// <summary>Propagacja z wszystkich komórek na początku próby. Fałsz, gdy któraś komórka nie ma już możliwości.</summary>
        protected abstract bool PropagateInitial();

        /// <summary>Zostawia komórce tylko wylosowany stan i przygotowuje propagację od niej.</summary>
        protected abstract void Collapse(int cell, int state);

        /// <summary>Propagacja po decyzji. Fałsz, gdy któraś komórka nie ma już możliwości.</summary>
        protected abstract bool PropagateDecision();

        /// <summary>Losuje stan komórki przez Choose (stany rosnąco, z wagami).</summary>
        protected abstract int ChooseState(int cell);

        /// <summary>Entropia Shannona komórki z wagami, liczona po stanach rosnąco (WeightedChooser.Entropy).</summary>
        protected abstract double CellEntropy(int cell);

        /// <summary>Struktury możliwości i propagacji rozwiązywacza w bajtach.</summary>
        protected abstract long StructureBytes { get; }

        protected int Choose(ReadOnlySpan<int> states) => WeightedChooser.Choose(Rules, states, ref random);

        protected int Choose(ReadOnlySpan<ulong> mask) => WeightedChooser.Choose(Rules, mask, ref random);

        protected void CountRemoved(long possibilities) => metrics.RemovedPossibilities += possibilities;

        protected void CountPropagationSteps(long steps) => metrics.PropagationSteps += steps;

        /// <summary>Stany dozwolone w komórce przez krawędzie siatki: dół, góra i boki bez sąsiada.</summary>
        protected void FillAllowedByGridEdges(int cell, Span<ulong> allowed)
        {
            BitMask.SetFirst(allowed, Rules.StateCount);
            int floor = Grid.Floor(cell);
            if (floor == 0)
                And(allowed, Rules.BottomMask);
            if (floor == Grid.Floors - 1)
                And(allowed, Rules.TopMask);
            foreach (Direction side in DirectionExtensions.Sides)
                if (Neighbors[cell * DirectionExtensions.Count + (int)side] < 0)
                    And(allowed, Rules.GetSideEdgeMask(side, floor == 0));
        }

        /// <summary>Nowa próba: stany dozwolone przez krawędzie siatki, losowy dodatek heurystyki, propagacja.</summary>
        bool BeginAttempt()
        {
            attempt++;
            metrics.Attempts = attempt;
            random = DeterministicRandom.ForAttempt(seed, attempt - 1);
            LastDecisionCell = -1;

            ResetPossibilities();
            Settings.Selector.Reset(Grid, ref random);
            changed.Clear();
            for (int cell = 0; cell < Grid.CellCount; cell++)
                if (GetPossibilityCount(cell) == 0)
                {
                    LastContradictionCell = cell;
                    return false;
                }

            if (!Propagate(true))
                return false;
            changed.Clear();
            for (int cell = 0; cell < Grid.CellCount; cell++)
                Notify(cell);
            return true;
        }

        bool Propagate(bool initial)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                return initial ? PropagateInitial() : PropagateDecision();
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
                if (Settings.ContradictionHandler.OnContradiction(attempt, Settings.MaxAttempts) != ContradictionAction.Restart)
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
            var states = new int[Grid.CellCount];
            for (int cell = 0; cell < Grid.CellCount; cell++)
                states[cell] = GetState(cell);
            result = new GenerationResult
            {
                Status = status,
                Size = Grid.Size,
                Seed = seed,
                TilesetName = Rules.TilesetName,
                SolverName = Name,
                SelectorName = Settings.Selector.Name,
                ContradictionHandlerName = Settings.ContradictionHandler.Name,
                States = states,
                Metrics = metrics,
                LastContradictionCell = LastContradictionCell
            };
        }

        void NotifyChangedCells()
        {
            while (changed.TryDequeue(out int cell))
                Notify(cell);
        }

        void Notify(int cell) => Settings.Selector.OnCellChanged(cell, GetPossibilityCount(cell), CellEntropy(cell));

        static void And(Span<ulong> target, ReadOnlySpan<ulong> mask)
        {
            for (int i = 0; i < target.Length; i++)
                target[i] &= mask[i];
        }
    }
}
