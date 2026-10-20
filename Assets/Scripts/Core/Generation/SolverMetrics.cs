using System.Diagnostics;

namespace WFC.Core
{
    /// <summary>
    /// Liczniki i czasy jednej generacji (wszystkie próby razem). Czasy są w taktach Stopwatch i liczą tylko pracę
    /// rozwiązywacza: przerwy między krokami w trybie krok po kroku się nie wliczają.
    /// </summary>
    public struct SolverMetrics
    {
        public long TotalTicks;
        public long SelectionTicks;
        public long PropagationTicks;
        public long BacktrackTicks;

        /// <summary>Rozpoczęte próby (1 = udało się za pierwszym razem).</summary>
        public int Attempts;

        /// <summary>Decyzje: wybór komórki i wylosowanie jej stanu.</summary>
        public int Decisions;

        /// <summary>Kroki propagacji: sprawdzenia jednego sąsiada jednej komórki z kolejki.</summary>
        public long PropagationSteps;

        /// <summary>Usunięte możliwości (stan wykreślony z komórki), także przy decyzjach.</summary>
        public long RemovedPossibilities;

        public int Contradictions;
        public int Restarts;
        public int Backtracks;

        public double TotalMs => ToMilliseconds(TotalTicks);
        public double SelectionMs => ToMilliseconds(SelectionTicks);
        public double PropagationMs => ToMilliseconds(PropagationTicks);
        public double BacktrackMs => ToMilliseconds(BacktrackTicks);

        public static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
