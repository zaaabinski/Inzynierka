using Unity.Mathematics;

namespace WFC.Core
{
    public enum StepStatus
    {
        /// <summary>Podjęto decyzję i propagacja się udała; generacja trwa.</summary>
        Decided,

        /// <summary>Wystąpiła sprzeczność, a reakcja na nią pozwoliła generować dalej (np. start od nowa).</summary>
        Contradiction,

        /// <summary>Mapa gotowa: każda komórka ma jeden stan.</summary>
        Finished,

        /// <summary>Generacja przerwana: limit prób albo czasu.</summary>
        Failed
    }

    /// <summary>Ustawienia generacji wspólne dla rozwiązywaczy.</summary>
    public sealed class SolverSettings
    {
        public ICellSelector Selector { get; set; } = new LevelFirstSelector();

        public IContradictionHandler ContradictionHandler { get; set; } = new RestartHandler();

        /// <summary>Największa liczba prób jednej mapy przy starcie od nowa.</summary>
        public int MaxAttempts { get; set; } = 1000;

        /// <summary>Limit czasu pracy rozwiązywacza w sekundach; 0 = bez limitu.</summary>
        public double TimeLimitSeconds { get; set; }
    }

    /// <summary>
    /// Wymienny rozwiązywacz WFC. Wszystkie dostają te same CompiledRules; różnią się sposobem przechowywania
    /// możliwości i propagacji. Ten sam rozwiązywacz z tym samym ziarnem i kolejnością wyboru daje tę samą mapę.
    /// </summary>
    public interface IWfcSolver
    {
        string Name { get; }

        /// <summary>Przygotowuje siatkę (x = E, y = piętro, z = N) i wykonuje propagację ograniczeń krawędzi.</summary>
        void Initialize(CompiledRules rules, int3 size, uint seed, SolverSettings settings);

        /// <summary>Jedna decyzja z propagacją (do podglądu krok po kroku).</summary>
        StepStatus Step();

        /// <summary>Kroki aż do końca generacji.</summary>
        GenerationResult Run();

        bool IsFinished { get; }

        /// <summary>Wynik po zakończeniu generacji, wcześniej null.</summary>
        GenerationResult Result { get; }

        SolverMetrics Metrics { get; }

        GridShape Grid { get; }

        /// <summary>Liczba możliwości komórki w bieżącej próbie.</summary>
        int GetPossibilityCount(int cell);

        /// <summary>Stan komórki, jeśli ma jedną możliwość, inaczej −1.</summary>
        int GetState(int cell);

        /// <summary>Komórka ostatniej decyzji albo −1.</summary>
        int LastDecisionCell { get; }

        /// <summary>Komórka ostatniej sprzeczności albo −1.</summary>
        int LastContradictionCell { get; }

        /// <summary>
        /// Największy rozmiar struktur danych rozwiązywacza i selektora w bajtach (bez skompilowanych reguł), szacowany
        /// z rozmiarów tablic. Porównuje sposób przechowywania możliwości niezależnie od środowiska uruchomienia.
        /// </summary>
        long PeakMemoryBytes { get; }
    }
}
