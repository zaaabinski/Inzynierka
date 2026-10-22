using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Wspólny przebieg generacji dla wszystkich rozwiązywaczy: próby, decyzje, reakcja na sprzeczność (start od nowa,
    /// cofanie decyzji, reset lokalny), pomiar czasu i powiadamianie heurystyki. Rozwiązywacze różnią się tylko
    /// przechowywaniem możliwości, propagacją i dziennikiem zmian.
    /// <para>
    /// Mapy wychodzą identyczne we wszystkich rozwiązywaczach, bo: generator liczb losowych zależy tylko od ziarna i numeru
    /// próby; propagacja zawsze dochodzi do tego samego stanu (największego zbioru możliwości zgodnego z sąsiadami),
    /// niezależnie od kolejności; heurystyka dostaje liczbę możliwości i entropię liczoną po stanach rosnąco,
    /// a jej wybór nie zależy od kolejności powiadomień. Sprzeczność pojawia się wtedy, gdy ten stan ma pustą komórkę,
    /// więc też w tej samej próbie. Reset lokalny zależy jeszcze od komórki sprzeczności, czyli od kolejności propagacji:
    /// Naive, Bitset i Burst mają tę samą kolejkę FIFO i kolejność kierunków, więc też trafiają na tę samą komórkę.
    /// </para>
    /// </summary>
    public abstract class SolverBase : IWfcSolver
    {
        /// <summary>Decyzja na stosie do cofania: komórka, wylosowany stan, początek jej wpisów w dzienniku i numer poziomu.</summary>
        readonly struct Decision
        {
            public Decision(int cell, int state, int trailPosition, int level)
            {
                Cell = cell;
                State = state;
                TrailPosition = trailPosition;
                Level = level;
            }

            public int Cell { get; }
            public int State { get; }
            public int TrailPosition { get; }
            public int Level { get; }
        }

        /// <summary>Wpis stosu decyzji: 4 liczby int.</summary>
        const int DecisionBytes = 16;

        readonly List<Decision> decisions = new List<Decision>();
        DeterministicRandom random;
        SolverMetrics metrics;
        GenerationResult result;
        CellQueue changed;
        uint seed;
        int attempt;
        int levelCounter;
        int backtracksInAttempt;
        int localResetsInAttempt;
        int peakDecisions;
        bool keepUndoLog;
        bool initialized;

        protected CompiledRules Rules { get; private set; }

        protected SolverSettings Settings { get; private set; }

        /// <summary>Sąsiedzi: indeks komórka × 6 + kierunek, −1 poza siatką.</summary>
        protected int[] Neighbors { get; private set; }

        /// <summary>Komórki zmienione od ostatniego powiadomienia heurystyki.</summary>
        protected CellQueue Changed => changed;

        /// <summary>
        /// Czy zapisywać zmiany w dzienniku: reakcja na sprzeczność cofa decyzje, a w próbie jest co najmniej jedna decyzja.
        /// Zmian sprzed pierwszej decyzji nie trzeba cofać, bo cofnięcie wszystkiego to start od nowa.
        /// </summary>
        protected bool RecordingUndo { get; private set; }

        /// <summary>
        /// Numer bieżącego poziomu (decyzji) w dzienniku, inny dla każdej decyzji w całej generacji. Komórkę zapisuje się
        /// w dzienniku przy pierwszej zmianie na danym poziomie, więc rozwiązywacz pamięta przy niej numer poziomu.
        /// </summary>
        protected int UndoLevel { get; private set; }

        public abstract string Name { get; }

        public bool IsFinished => result != null;

        public GenerationResult Result => result;

        public SolverMetrics Metrics => metrics;

        public GridShape Grid { get; private set; }

        public int LastDecisionCell { get; private set; } = -1;

        public int LastContradictionCell { get; protected set; } = -1;

        /// <summary>
        /// Struktury rozwiązywacza (StructureBytes, razem z dziennikiem zmian), tablica sąsiadów (6 liczb int na komórkę),
        /// lista zmienionych komórek (5 bajtów na komórkę), stos decyzji w szczycie i struktury heurystyki.
        /// </summary>
        public long PeakMemoryBytes =>
            initialized
                ? StructureBytes + (long)Grid.CellCount * DirectionExtensions.Count * 4 + changed.MemoryBytes +
                  (long)peakDecisions * DecisionBytes + Settings.Selector.MemoryBytes
                : 0;

        /// <summary>Cofanie decyzji wymaga dziennika zmian, reset lokalny – przywracania komórek; start od nowa działa zawsze.</summary>
        public bool Supports(IContradictionHandler handler)
        {
            ContradictionNeeds needs = handler?.Needs ?? ContradictionNeeds.None;
            return ((needs & ContradictionNeeds.UndoLog) == 0 || SupportsUndo) &&
                   ((needs & ContradictionNeeds.LocalReset) == 0 || SupportsLocalReset);
        }

        public void Initialize(CompiledRules compiledRules, int3 size, uint mapSeed, SolverSettings solverSettings)
        {
            long start = Stopwatch.GetTimestamp();
            Rules = compiledRules ?? throw new ArgumentNullException(nameof(compiledRules));
            Settings = solverSettings ?? new SolverSettings();
            if (!Supports(Settings.ContradictionHandler))
                throw new NotSupportedException($"{Name} nie obsługuje reakcji na sprzeczność {Settings.ContradictionHandler.Name}: " +
                                                "cofanie decyzji i reset lokalny działają z Naive, Bitset i Burst.");
            Grid = new GridShape(size);
            seed = mapSeed;
            Neighbors = Grid.BuildNeighborTable();
            changed = new CellQueue(Grid.CellCount);
            keepUndoLog = (Settings.ContradictionHandler.Needs & ContradictionNeeds.UndoLog) != 0;
            metrics = default;
            result = null;
            attempt = 0;
            levelCounter = 0;
            peakDecisions = 0;
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
                if (keepUndoLog)
                    PushDecision(cell, state);
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
        /// zliczone przez CountRemoved; puste kolejki propagacji i dziennik zmian.
        /// </summary>
        protected abstract void ResetPossibilities();

        /// <summary>Propagacja z wszystkich komórek na początku próby. Fałsz, gdy któraś komórka nie ma już możliwości.</summary>
        protected abstract bool PropagateInitial();

        /// <summary>Zostawia komórce tylko wylosowany stan (przy RecordingUndo najpierw zapis w dzienniku) i przygotowuje propagację od niej.</summary>
        protected abstract void Collapse(int cell, int state);

        /// <summary>Propagacja z kolejki (po decyzji, cofnięciu albo resecie). Fałsz, gdy któraś komórka nie ma już możliwości.</summary>
        protected abstract bool PropagateDecision();

        /// <summary>Losuje stan komórki przez Choose (stany rosnąco, z wagami).</summary>
        protected abstract int ChooseState(int cell);

        /// <summary>Entropia Shannona komórki z wagami, liczona po stanach rosnąco (WeightedChooser.Entropy).</summary>
        protected abstract double CellEntropy(int cell);

        /// <summary>Struktury możliwości i propagacji rozwiązywacza w bajtach.</summary>
        protected abstract long StructureBytes { get; }

        /// <summary>Czy rozwiązywacz prowadzi dziennik zmian (TrailPosition, UndoTo, Ban).</summary>
        protected virtual bool SupportsUndo => false;

        /// <summary>Czy rozwiązywacz umie przywrócić początkowe możliwości komórki (ResetCell, EnqueuePropagation).</summary>
        protected virtual bool SupportsLocalReset => false;

        /// <summary>Liczba wpisów w dzienniku zmian.</summary>
        protected virtual int TrailPosition => 0;

        /// <summary>
        /// Przywraca z dziennika, od końca, komórki zapisane od pozycji position, dodaje je do Changed i czyści kolejkę
        /// propagacji (stan sprzed decyzji był już zgodny z sąsiadami).
        /// </summary>
        protected virtual void UndoTo(int position) => throw new NotSupportedException();

        /// <summary>Wykreśla stan z komórki (przy RecordingUndo najpierw zapis w dzienniku) i dodaje ją do propagacji. Fałsz, gdy komórka została bez możliwości.</summary>
        protected virtual bool Ban(int cell, int state) => throw new NotSupportedException();

        /// <summary>Przywraca komórce możliwości z początku próby (FillAllowedByGridEdges).</summary>
        protected virtual void ResetCell(int cell) => throw new NotSupportedException();

        /// <summary>Dodaje komórkę do kolejki propagacji.</summary>
        protected virtual void EnqueuePropagation(int cell) => throw new NotSupportedException();

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
            decisions.Clear();
            RecordingUndo = false;
            UndoLevel = 0;
            backtracksInAttempt = 0;
            localResetsInAttempt = 0;

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

        void PushDecision(int cell, int state)
        {
            decisions.Add(new Decision(cell, state, TrailPosition, ++levelCounter));
            if (decisions.Count > peakDecisions)
                peakDecisions = decisions.Count;
            UndoLevel = levelCounter;
            RecordingUndo = true;
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
                var context = new ContradictionContext(attempt, Settings.MaxAttempts, decisions.Count, backtracksInAttempt, localResetsInAttempt, LastContradictionCell);
                ContradictionAction action = Settings.ContradictionHandler.OnContradiction(context);
                if ((action == ContradictionAction.Backtrack && decisions.Count == 0) || (action == ContradictionAction.LocalReset && LastContradictionCell < 0))
                    action = context.RestartOrAbort;
                switch (action)
                {
                    case ContradictionAction.Backtrack:
                        if (!Backtrack())
                            continue;
                        NotifyChangedCells();
                        return StepStatus.Contradiction;
                    case ContradictionAction.LocalReset:
                        if (!ResetAround(LastContradictionCell))
                            continue;
                        NotifyChangedCells();
                        return StepStatus.Contradiction;
                    case ContradictionAction.Restart:
                        metrics.Restarts++;
                        if (BeginAttempt())
                            return StepStatus.Contradiction;
                        continue;
                    default:
                        Finish(GenerationStatus.Failed);
                        return StepStatus.Failed;
                }
            }
        }

        /// <summary>
        /// Cofa ostatnią decyzję: stan sprzed niej z dziennika, potem wykreślenie wylosowanego stanu na poziomie decyzji
        /// wcześniejszej (żeby jej cofnięcie przywróciło też ten stan) i propagacja. Fałsz przy kolejnej sprzeczności.
        /// </summary>
        bool Backtrack()
        {
            long start = Stopwatch.GetTimestamp();
            metrics.Backtracks++;
            backtracksInAttempt++;
            Decision last = decisions[decisions.Count - 1];
            decisions.RemoveAt(decisions.Count - 1);
            UndoTo(last.TrailPosition);
            RecordingUndo = decisions.Count > 0;
            UndoLevel = RecordingUndo ? decisions[decisions.Count - 1].Level : 0;
            bool left = Ban(last.Cell, last.State);
            metrics.BacktrackTicks += Stopwatch.GetTimestamp() - start;
            if (!left)
            {
                LastContradictionCell = last.Cell;
                return false;
            }
            return Propagate(false);
        }

        /// <summary>
        /// Reset lokalny: komórki w sześcianie o promieniu ResetRadius wokół komórki center dostają możliwości z początku próby;
        /// propagacja z nich i z ich sąsiadów spoza sześcianu. Fałsz przy kolejnej sprzeczności.
        /// </summary>
        bool ResetAround(int center)
        {
            long start = Stopwatch.GetTimestamp();
            metrics.Backtracks++;
            localResetsInAttempt++;
            int radius = Settings.ContradictionHandler.ResetRadius;
            int3 middle = Grid.Coordinates(center);
            int3 from = math.max(middle - radius, 0);
            int3 to = math.min(middle + radius, Grid.Size - 1);
            for (int floor = from.y; floor <= to.y; floor++)
                for (int z = from.z; z <= to.z; z++)
                    for (int x = from.x; x <= to.x; x++)
                    {
                        int cell = Grid.Index(x, floor, z);
                        ResetCell(cell);
                        changed.Enqueue(cell);
                        EnqueuePropagation(cell);
                    }
            for (int floor = from.y; floor <= to.y; floor++)
                for (int z = from.z; z <= to.z; z++)
                    for (int x = from.x; x <= to.x; x++)
                    {
                        int cell = Grid.Index(x, floor, z);
                        for (int direction = 0; direction < DirectionExtensions.Count; direction++)
                        {
                            int neighbor = Neighbors[cell * DirectionExtensions.Count + direction];
                            if (neighbor >= 0 && math.any(Grid.Coordinates(neighbor) < from | Grid.Coordinates(neighbor) > to))
                                EnqueuePropagation(neighbor);
                        }
                    }
            metrics.BacktrackTicks += Stopwatch.GetTimestamp() - start;
            return Propagate(false);
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
