using System;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Główny kontroler sceny Generator: ustawienia generacji, przyciski Generuj, Krok, Do końca, Wyczyść
    /// (w inspektorze i w oknie ustawień), uruchomienie rozwiązywacza, sprawdzenie mapy i postawienie jej przez MapBuilder.
    /// W trybie Play klawisz G generuje mapę z następnym ziarnem, a podgląd krok po kroku (StepVisualizer) dodaje się sam.
    /// </summary>
    public sealed class GeneratorController : MonoBehaviour
    {
        [Header("Zestaw i siatka")]
        [SerializeField] TilesetDefinition tileset;
        [Tooltip("x = E, y = piętra, z = N. Baza pomiarów: 20 × 13 × 20.")]
        [SerializeField] Vector3Int gridSize = new Vector3Int(20, 13, 20);

        [Header("Algorytm")]
        [SerializeField] SolverKind solver = SolverKind.Bitset;
        [Tooltip("Piętrami: domyślna, jak w prototypie. Scanline: komórki po kolei od dołu. MRV: komórka z najmniejszą liczbą możliwości. Entropia: najmniejsza entropia Shannona w całej siatce. Losowo: losowa kolejność komórek.")]
        [SerializeField] SelectorKind selector = SelectorKind.LevelFirst;
        [Tooltip("Start od nowa: nowa próba. Cofanie decyzji: powrót do stanu sprzed ostatniej decyzji (do 1000 razy na próbę). Reset lokalny: sześcian 5 × 5 × 5 komórek wokół sprzeczności od nowa (do 100 razy na próbę). Cofanie i reset działają z Naive, Bitset i Burst.")]
        [SerializeField] ContradictionKind contradiction = ContradictionKind.Restart;
        [SerializeField] uint seed = 1;
        [Tooltip("Limit prób jednej mapy przy starcie od nowa. MRV kończy się sprzecznością w około 97% prób.")]
        [SerializeField, Min(1)] int maxAttempts = 1000;

        [Header("Scena")]
        [SerializeField] MapBuilder mapBuilder;
        [SerializeField] bool generateOnStart = true;

        IWfcSolver activeSolver;
        CompiledRules rules;
        StepVisualizer visualizer;

        // Poprzednia udana mapa, do porównania z następną (różnorodność między ziarnami).
        int[] previousMap;
        int3 previousSize;
        TilesetDefinition previousTileset;

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

        public SolverKind Solver
        {
            get => solver;
            set => solver = value;
        }

        public SelectorKind Selector
        {
            get => selector;
            set => selector = value;
        }

        public ContradictionKind Contradiction
        {
            get => contradiction;
            set => contradiction = value;
        }

        public int MaxAttempts
        {
            get => maxAttempts;
            set => maxAttempts = Mathf.Max(1, value);
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

        /// <summary>Podgląd krok po kroku; w trybie Play tworzony przy pierwszym użyciu, poza nim null.</summary>
        public StepVisualizer Visualizer
        {
            get
            {
                if (visualizer == null && Application.isPlaying && !TryGetComponent(out visualizer))
                {
                    visualizer = gameObject.AddComponent<StepVisualizer>();
                    visualizer.Generator = this;
                }
                return visualizer;
            }
        }

        public CompiledRules Rules => rules;

        public GenerationResult LastResult { get; private set; }

        public RuleCheckReport LastCheck { get; private set; }

        public StructureStats LastStats { get; private set; }

        /// <summary>Podsumowanie ostatniej generacji albo postęp w trybie krok po kroku.</summary>
        public string LastReport { get; private set; } = string.Empty;

        void Awake() => _ = Visualizer;

        void Start()
        {
            // Z argumentem -benchmark od razu startuje badanie (BenchmarkRunner), bez mapy na start.
            if (generateOnStart && !CommandLine.IsBenchmark)
                Generate();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame && !UiInput.IsTyping)
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

        /// <summary>
        /// Jedna decyzja z propagacją albo count decyzji naraz (odtwarzanie w StepVisualizer); mapa i raport odświeżają się
        /// raz, po ostatniej. Wywołanie po zakończonej generacji zaczyna nową.
        /// </summary>
        public StepStatus Step(int count = 1)
        {
            if (activeSolver == null || activeSolver.IsFinished)
                Begin();
            StepStatus status = StepStatus.Decided;
            for (int step = 0; step < count && !activeSolver.IsFinished; step++)
                status = activeSolver.Step();
            if (activeSolver.IsFinished)
                Complete();
            else
                ShowProgress(status);
            return status;
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
            if (visualizer != null)
                visualizer.Pause();
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
            activeSolver = WfcFactory.CreateSolver(solver);
            activeSolver.Initialize(rules, GridSize, seed, WfcFactory.CreateSettings(selector, contradiction, maxAttempts));
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
            if (LastResult.Success)
            {
                if (previousMap != null && previousTileset == tileset && math.all(previousSize == LastResult.Size))
                {
                    MapDifference difference = DiversityAnalyzer.Compare(rules, LastResult.Size, previousMap, LastResult.States);
                    LastReport += $"\nInna niż poprzednia mapa: {difference}.";
                }
                previousMap = LastResult.States;
                previousSize = LastResult.Size;
                previousTileset = tileset;
            }
            if (mapBuilder != null && mapBuilder.IsInstanced)
                LastReport += $"\nMapa rysowana instancjami: kostki {mapBuilder.InstanceCount}, wywołania rysowania na klatkę {mapBuilder.InstancedDrawCalls}.";
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
            int floor = activeSolver.LastDecisionCell >= 0 ? grid.Floor(activeSolver.LastDecisionCell) : 0;
            LastReport = $"Ziarno {seed}, próba {metrics.Attempts}: decyzja {metrics.Decisions}, piętro {floor} (0 = parter)" +
                         (status == StepStatus.Contradiction ? $" – sprzeczność, {WfcFactory.Label(contradiction).ToLowerInvariant()}" : string.Empty) +
                         $"; ustalone komórki {decided} z {grid.CellCount}.";
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
                .Append($"siatka {result.Grid}: {status}, próby: {metrics.Attempts}, sprzeczności: {metrics.Contradictions}, cofnięcia: {metrics.Backtracks}.\n")
                .Append($"Czas generacji {metrics.TotalMs:0} ms (wybór komórek {metrics.SelectionMs:0} ms, propagacja {metrics.PropagationMs:0} ms, cofanie {metrics.BacktrackMs:0} ms), ")
                .Append($"stawianie kostek {buildMs:0} ms.\n")
                .Append($"Decyzje {metrics.Decisions}, kroki propagacji {metrics.PropagationSteps}, usunięte możliwości {metrics.RemovedPossibilities}.");
            if (check != null)
                text.Append($"\nNaruszenia zasad: {check.Total}.")
                    .Append(check.DisabledTotal > 0 ? $" Przypadki zasad wyłączonych w tym zestawie: {check.DisabledTotal}." : string.Empty);
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
