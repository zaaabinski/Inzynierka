using System;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using WFC.Core;

namespace WFC.Runtime
{
    public enum SolverKind
    {
        Naive
    }

    /// <summary>Heurystyka wyboru komórki (kolejność decyzji).</summary>
    public enum SelectorKind
    {
        [InspectorName("Piętrami, w piętrze najmniejsza entropia")]
        LevelFirst,

        [InspectorName("Scanline: po kolei")]
        Scanline,

        [InspectorName("MRV: najmniej możliwości")]
        MinRemaining,

        [InspectorName("Najmniejsza entropia")]
        Entropy
    }

    public enum ContradictionKind
    {
        Restart
    }

    /// <summary>
    /// Główny kontroler sceny Generator: ustawienia generacji, przyciski Generuj, Krok, Do końca, Wyczyść
    /// (w inspektorze), uruchomienie rozwiązywacza, sprawdzenie mapy i postawienie jej przez MapBuilder.
    /// W trybie Play klawisz G generuje mapę z następnym ziarnem.
    /// </summary>
    public sealed class GeneratorController : MonoBehaviour
    {
        [Header("Zestaw i siatka")]
        [SerializeField] TilesetDefinition tileset;
        [Tooltip("x = E, y = piętra, z = N. Baza pomiarów: 20 × 13 × 20.")]
        [SerializeField] Vector3Int gridSize = new Vector3Int(20, 13, 20);

        [Header("Algorytm")]
        [SerializeField] SolverKind solver = SolverKind.Naive;
        [Tooltip("Piętrami: domyślna, jak w prototypie. Scanline: komórki po kolei od dołu. MRV: komórka z najmniejszą liczbą możliwości. Entropia: najmniejsza entropia Shannona w całej siatce.")]
        [SerializeField] SelectorKind selector = SelectorKind.LevelFirst;
        [SerializeField] ContradictionKind contradiction = ContradictionKind.Restart;
        [SerializeField] uint seed = 1;
        [Tooltip("Limit prób jednej mapy przy starcie od nowa. MRV kończy się sprzecznością w około 97% prób.")]
        [SerializeField, Min(1)] int maxAttempts = 1000;

        [Header("Scena")]
        [SerializeField] MapBuilder mapBuilder;
        [SerializeField] bool generateOnStart = true;

        IWfcSolver activeSolver;
        CompiledRules rules;

        public TilesetDefinition Tileset
        {
            get => tileset;
            set => tileset = value;
        }

        public MapBuilder MapBuilder
        {
            get => mapBuilder;
            set => mapBuilder = value;
        }

        public SelectorKind Selector
        {
            get => selector;
            set => selector = value;
        }

        public uint Seed
        {
            get => seed;
            set => seed = value;
        }

        public int3 GridSize
        {
            get => new int3(gridSize.x, gridSize.y, gridSize.z);
            set => gridSize = new Vector3Int(value.x, value.y, value.z);
        }

        public IWfcSolver ActiveSolver => activeSolver;

        public CompiledRules Rules => rules;

        public GenerationResult LastResult { get; private set; }

        public RuleCheckReport LastCheck { get; private set; }

        public StructureStats LastStats { get; private set; }

        /// <summary>Podsumowanie ostatniej generacji albo postęp w trybie krok po kroku.</summary>
        public string LastReport { get; private set; } = string.Empty;

        void Start()
        {
            if (generateOnStart)
                Generate();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
                NextSeed();
        }

        /// <summary>Nowa generacja z bieżącym ziarnem, od razu do końca.</summary>
        public void Generate()
        {
            Begin();
            RunToEnd();
        }

        public void NextSeed()
        {
            seed++;
            Generate();
        }

        /// <summary>Jedna decyzja z propagacją; pierwsze wywołanie zaczyna nową generację.</summary>
        public void Step()
        {
            if (activeSolver == null || activeSolver.IsFinished)
                Begin();
            StepStatus status = activeSolver.Step();
            if (activeSolver.IsFinished)
                Complete();
            else
                ShowProgress(status);
        }

        public void RunToEnd()
        {
            if (activeSolver == null || activeSolver.IsFinished)
                Begin();
            activeSolver.Run();
            Complete();
        }

        public void Clear()
        {
            activeSolver = null;
            LastResult = null;
            LastCheck = null;
            LastStats = null;
            LastReport = string.Empty;
            if (mapBuilder != null)
                mapBuilder.Clear();
        }

        void Begin()
        {
            if (tileset == null)
                throw new InvalidOperationException("GeneratorController: wybierz zestaw kafelków.");
            rules = RuleCompiler.Compile(tileset.ToTilesetData());
            activeSolver = CreateSolver();
            activeSolver.Initialize(rules, GridSize, seed, new SolverSettings
            {
                Selector = CreateSelector(),
                ContradictionHandler = CreateContradictionHandler(),
                MaxAttempts = maxAttempts
            });
            LastResult = null;
            LastCheck = null;
            LastStats = null;
            if (mapBuilder != null)
                mapBuilder.Clear();
        }

        void Complete()
        {
            LastResult = activeSolver.Result;
            if (LastResult.Success)
            {
                LastCheck = RuleChecker.Check(rules, LastResult.Size, LastResult.States);
                LastStats = StructureAnalyzer.Analyze(rules, LastResult.Size, LastResult.States);
            }
            if (mapBuilder != null)
                mapBuilder.Show(tileset, rules, activeSolver.Grid, LastResult.States, seed);
            LastReport = Describe(LastResult, LastCheck, LastStats, mapBuilder != null ? mapBuilder.LastBuildMs : 0);
            if (LastResult.Success && LastCheck.Total == 0)
                Debug.Log(LastReport, this);
            else
                Debug.LogWarning(LastReport, this);
        }

        void ShowProgress(StepStatus status)
        {
            GridShape grid = activeSolver.Grid;
            var states = new int[grid.CellCount];
            int decided = 0;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                states[cell] = activeSolver.GetState(cell);
                decided += states[cell] >= 0 ? 1 : 0;
            }
            if (mapBuilder != null)
                mapBuilder.Show(tileset, rules, grid, states, seed);
            SolverMetrics metrics = activeSolver.Metrics;
            LastReport = $"Ziarno {seed}, próba {metrics.Attempts}: decyzja {metrics.Decisions}" +
                         (status == StepStatus.Contradiction ? " – sprzeczność, start od nowa" : string.Empty) +
                         $"; ustalone komórki {decided} z {grid.CellCount}.";
        }

        IWfcSolver CreateSolver()
        {
            switch (solver)
            {
                case SolverKind.Naive: return new NaiveSolver();
                default: throw new ArgumentOutOfRangeException(nameof(solver), solver, null);
            }
        }

        ICellSelector CreateSelector()
        {
            switch (selector)
            {
                case SelectorKind.LevelFirst: return new LevelFirstSelector();
                case SelectorKind.Scanline: return new ScanlineSelector();
                case SelectorKind.MinRemaining: return new MinRemainingSelector();
                case SelectorKind.Entropy: return new EntropySelector();
                default: throw new ArgumentOutOfRangeException(nameof(selector), selector, null);
            }
        }

        IContradictionHandler CreateContradictionHandler()
        {
            switch (contradiction)
            {
                case ContradictionKind.Restart: return new RestartHandler();
                default: throw new ArgumentOutOfRangeException(nameof(contradiction), contradiction, null);
            }
        }

        /// <summary>Opis wyniku po polsku, do inspektora i konsoli.</summary>
        public static string Describe(GenerationResult result, RuleCheckReport check, StructureStats stats, double buildMs)
        {
            SolverMetrics metrics = result.Metrics;
            var text = new StringBuilder();
            string status = result.Status switch
            {
                GenerationStatus.Success => "udana",
                GenerationStatus.TimedOut => "przerwana (limit czasu)",
                _ => "nieudana (limit prób)"
            };
            text.Append($"Ziarno {result.Seed}, {result.SolverName} + {result.SelectorName} + {result.ContradictionHandlerName}, ")
                .Append($"siatka {result.Grid}: {status} po {metrics.Attempts} próbach (sprzeczności: {metrics.Contradictions}).\n")
                .Append($"Czas generacji {metrics.TotalMs:0} ms (wybór komórek {metrics.SelectionMs:0} ms, propagacja {metrics.PropagationMs:0} ms), ")
                .Append($"stawianie kostek {buildMs:0} ms.\n")
                .Append($"Decyzje {metrics.Decisions}, kroki propagacji {metrics.PropagationSteps}, usunięte możliwości {metrics.RemovedPossibilities}.");
            if (check != null)
                text.Append($"\nNaruszenia zasad: {check.Total}.");
            if (stats != null)
                text.Append($"\nWieże {stats.Towers}, donżony {stats.Keeps}, mosty {stats.Bridges}, schody {stats.Stairs}, ")
                    .Append($"zamknięte dziedzińce {stats.EnclosedCourtyards}, teren dostępny z krawędzi {stats.ReachableTerrainPercent:0}%.");
            return text.ToString();
        }

        void OnDrawGizmos()
        {
            // Obrys siatki (GranicaSiatki z rozdziału 5).
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Vector3 size = new Vector3(gridSize.x, gridSize.y, gridSize.z) * UnityGrid.CellSize;
            Gizmos.DrawWireCube(size / 2f, size);
        }
    }
}
