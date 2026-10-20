using Unity.Mathematics;

namespace WFC.Core
{
    public enum GenerationStatus
    {
        /// <summary>Każda komórka ma dokładnie jeden stan.</summary>
        Success,

        /// <summary>Reakcja na sprzeczność zakończyła generację, np. po wyczerpaniu limitu prób.</summary>
        Failed,

        /// <summary>Przekroczony limit czasu z ustawień.</summary>
        TimedOut
    }

    /// <summary>Wynik generacji: stan każdej komórki, ustawienia i metryki.</summary>
    public sealed class GenerationResult
    {
        public GenerationStatus Status { get; set; }

        public bool Success => Status == GenerationStatus.Success;

        public int3 Size { get; set; }

        public uint Seed { get; set; }

        public string TilesetName { get; set; }

        public string SolverName { get; set; }

        public string SelectorName { get; set; }

        public string ContradictionHandlerName { get; set; }

        /// <summary>Stan każdej komórki (numeracja jak w GridShape) albo −1, gdy komórka nie jest ustalona.</summary>
        public int[] States { get; set; }

        public SolverMetrics Metrics { get; set; }

        /// <summary>Komórka ostatniej sprzeczności albo −1.</summary>
        public int LastContradictionCell { get; set; } = -1;

        public GridShape Grid => new GridShape(Size);
    }
}
