using System;
using System.Collections.Generic;
using UnityEngine;

namespace WFC.Runtime
{
    /// <summary>Jedno ustawienie w eksperymencie: rozwiązywacz, heurystyka, reakcja na sprzeczność, zestaw i rozmiar siatki.</summary>
    [Serializable]
    public struct BenchmarkVariant
    {
        public SolverKind Solver;
        public SelectorKind Selector;
        public ContradictionKind Contradiction;
        public TilesetDefinition Tileset;

        /// <summary>x = E, y = piętra, z = N.</summary>
        public Vector3Int Size;

        public override string ToString() =>
            $"{Solver} + {Selector} + {Contradiction}, {(Tileset != null ? Tileset.name : "brak zestawu")}, {Size.x} × {Size.z} × {Size.y}";
    }

    /// <summary>
    /// Eksperyment (rozdział 7 handoffu): wszystkie kombinacje list rozwiązywaczy, heurystyk, reakcji na sprzeczność,
    /// zestawów i rozmiarów, każda dla tych samych ziaren i tej samej liczby powtórzeń. Przed każdym wariantem
    /// kilka uruchomień na rozgrzewkę, które nie trafiają do pliku wyników.
    /// </summary>
    [CreateAssetMenu(menuName = "WFC/Eksperyment", fileName = "Eksperyment")]
    public sealed class BenchmarkConfig : ScriptableObject
    {
        [SerializeField] string experimentName = "E1";
        [SerializeField, TextArea] string description;

        [Header("Warianty (wszystkie kombinacje)")]
        [SerializeField] List<SolverKind> solvers = new List<SolverKind> { SolverKind.Naive };
        [SerializeField] List<SelectorKind> selectors = new List<SelectorKind> { SelectorKind.LevelFirst };
        [SerializeField] List<ContradictionKind> contradictionHandlers = new List<ContradictionKind> { ContradictionKind.Restart };
        [SerializeField] List<TilesetDefinition> tilesets = new List<TilesetDefinition>();
        [Tooltip("x = E, y = piętra, z = N.")]
        [SerializeField] List<Vector3Int> gridSizes = new List<Vector3Int> { new Vector3Int(20, 13, 20) };

        [Header("Ziarna, powtórzenia, limity")]
        [SerializeField, Min(0)] int firstSeed = 1;
        [SerializeField, Min(1)] int seedCount = 100;
        [Tooltip("Ile razy powtórzyć każde ziarno (ta sama mapa, osobny pomiar czasu).")]
        [SerializeField, Min(1)] int repetitions = 1;
        [Tooltip("Uruchomienia na rozgrzewkę przed każdym wariantem; nie trafiają do wyników.")]
        [SerializeField, Min(0)] int warmupRuns = 5;
        [SerializeField, Min(1)] int maxAttempts = 1000;
        [Tooltip("Limit czasu jednej mapy w sekundach; 0 = bez limitu. Przekroczenie zapisuje się jako wynik.")]
        [SerializeField, Min(0f)] float timeLimitSeconds;
        [Tooltip("W oknie: pokazuj co N-tą mapę (1 = każdą). Czas stawiania kostek nie wlicza się do czasu generacji.")]
        [SerializeField, Min(1)] int showEveryNth = 1;

        public string ExperimentName { get => experimentName; set => experimentName = value; }
        public string Description { get => description; set => description = value; }
        public List<SolverKind> Solvers => solvers;
        public List<SelectorKind> Selectors => selectors;
        public List<ContradictionKind> ContradictionHandlers => contradictionHandlers;
        public List<TilesetDefinition> Tilesets => tilesets;
        public List<Vector3Int> GridSizes => gridSizes;
        public uint FirstSeed { get => (uint)firstSeed; set => firstSeed = (int)value; }
        public int SeedCount { get => seedCount; set => seedCount = Mathf.Max(1, value); }
        public int Repetitions { get => repetitions; set => repetitions = Mathf.Max(1, value); }
        public int WarmupRuns { get => warmupRuns; set => warmupRuns = Mathf.Max(0, value); }
        public int MaxAttempts { get => maxAttempts; set => maxAttempts = Mathf.Max(1, value); }
        public float TimeLimitSeconds { get => timeLimitSeconds; set => timeLimitSeconds = Mathf.Max(0f, value); }
        public int ShowEveryNth { get => showEveryNth; set => showEveryNth = Mathf.Max(1, value); }

        /// <summary>
        /// Wszystkie kombinacje w kolejności: zestaw, rozmiar, rozwiązywacz, heurystyka, reakcja na sprzeczność. Pomija
        /// rozwiązywacze, które nie umieją danej reakcji (SupportCount z cofaniem albo resetem lokalnym).
        /// </summary>
        public List<BenchmarkVariant> Variants()
        {
            var variants = new List<BenchmarkVariant>();
            foreach (TilesetDefinition tileset in tilesets)
                foreach (Vector3Int size in gridSizes)
                    foreach (SolverKind solver in solvers)
                        foreach (SelectorKind selector in selectors)
                            foreach (ContradictionKind contradiction in contradictionHandlers)
                                if (WfcFactory.IsSupported(solver, contradiction))
                                    variants.Add(new BenchmarkVariant
                                    {
                                        Tileset = tileset,
                                        Size = size,
                                        Solver = solver,
                                        Selector = selector,
                                        Contradiction = contradiction
                                    });
            return variants;
        }

        /// <summary>Kombinacje pominięte, bo rozwiązywacz nie umie danej reakcji na sprzeczność.</summary>
        public int SkippedVariants =>
            tilesets.Count * gridSizes.Count * solvers.Count * selectors.Count * contradictionHandlers.Count - Variants().Count;

        /// <summary>Uruchomienia zapisywane do pliku: warianty × ziarna × powtórzenia.</summary>
        public int MeasuredRuns => Variants().Count * seedCount * repetitions;

        /// <summary>Wszystkie uruchomienia razem z rozgrzewką.</summary>
        public int TotalRuns => Variants().Count * (seedCount * repetitions + warmupRuns);

        /// <summary>Kopia do zmiany w oknie bez ruszania zasobu eksperymentu.</summary>
        public BenchmarkConfig Clone()
        {
            BenchmarkConfig copy = Instantiate(this);
            copy.name = name;
            return copy;
        }

        public string Summary()
        {
            int skipped = SkippedVariants;
            return $"{experimentName}: warianty {Variants().Count} × ziarna {seedCount} (od {firstSeed}) × powtórzenia {repetitions}, " +
                   $"uruchomienia do zapisu: {MeasuredRuns}, rozgrzewka: {warmupRuns} na wariant." +
                   (skipped > 0 ? $" Pominięte warianty (rozwiązywacz nie umie tej reakcji na sprzeczność): {skipped}." : string.Empty);
        }
    }
}
