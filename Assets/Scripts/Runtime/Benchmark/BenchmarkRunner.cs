using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Prowadzi badanie w scenie Generator. W oknie: jedno uruchomienie na klatkę, mapy widoczne (co N-ta), a po każdej
    /// pokazanej mapie pomiar kosztu jej rysowania z kilku klatek (RenderStats). Pauza i Stop działają po bieżącym
    /// uruchomieniu. Z argumentem -benchmark badanie startuje samo: z oknem pokazuje mapy i zamyka aplikację na końcu,
    /// a z -batchmode -nographics liczy wszystko bez okna i bez pomiaru rysowania.
    /// Argumenty: -config E1 albo -config wszystkie, -out plik.csv, opcjonalnie -seeds, -firstSeed, -repetitions, -warmup,
    /// -showEvery, -display, -batching.
    /// </summary>
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        [Tooltip("Eksperymenty do wyboru w oknie i z wiersza poleceń (-config E1).")]
        [SerializeField] List<BenchmarkConfig> experiments = new List<BenchmarkConfig>();
        [SerializeField] MapBuilder mapBuilder;
        [SerializeField] GeneratorController generator;

        /// <summary>Wartość -config: wszystkie eksperymenty po kolei.</summary>
        public const string AllExperimentsKey = "wszystkie";

        /// <summary>Klatki po postawieniu mapy, zanim liczniki i czasy pokażą ją bez kosztu stawiania.</summary>
        const int SettleFrames = 5;

        /// <summary>Klatki na jedną wartość na żywo w oknie.</summary>
        const int LiveFrames = 30;

        BenchmarkSession session;
        bool quitWhenDone;
        RenderStatsRecorder renderStats;
        BenchmarkRunResult pendingRun;
        int settleLeft;
        double shownAt;
        readonly Queue<BenchmarkConfig> queuedSeries = new Queue<BenchmarkConfig>();
        readonly List<string> finishedSeries = new List<string>();
        string queueFolder;
        int queueTotal;
        bool renderingSaved;
        MapDisplay displayBeforeSeries;
        RenderBatching batchingBeforeSeries;

        public List<BenchmarkConfig> Experiments => experiments;

        public MapBuilder MapBuilder
        {
            get => mapBuilder;
            set => mapBuilder = value;
        }

        public GeneratorController Generator
        {
            get => generator;
            set => generator = value;
        }

        public BenchmarkSession Session => session;

        public bool IsRunning => session != null && !session.IsFinished;

        public bool IsPaused { get; private set; }

        public BenchmarkRunResult LastRun { get; private set; }

        /// <summary>Opis postępu albo wyniku serii po polsku.</summary>
        public string Status { get; private set; } = "Badanie nie trwa.";

        /// <summary>Plik ostatniej albo bieżącej serii.</summary>
        public string OutputPath { get; private set; }

        /// <summary>Wykonane uruchomienia (z rozgrzewką) bieżącej albo ostatniej serii.</summary>
        public int CompletedRuns { get; private set; }

        /// <summary>Wszystkie uruchomienia (z rozgrzewką) bieżącej albo ostatniej serii.</summary>
        public int TotalRuns { get; private set; }

        /// <summary>Ustawienia bieżącej albo ostatniej serii.</summary>
        public BenchmarkConfig CurrentConfig { get; private set; }

        /// <summary>Koszt rysowania na żywo, odświeżany co pół sekundy przy 60 klatkach; null bez okna.</summary>
        public RenderStats? LiveRenderStats { get; private set; }

        /// <summary>Przy wszystkich eksperymentach po kolei: który teraz trwa; inaczej pusty tekst.</summary>
        public string QueueLabel => queueTotal > 0 ? $"Eksperyment {queueTotal - queuedSeries.Count} z {queueTotal}: " : string.Empty;

        void OnEnable()
        {
            if (!Application.isBatchMode)
                renderStats = new RenderStatsRecorder();
        }

        void OnDisable()
        {
            renderStats?.Dispose();
            renderStats = null;
            RestoreRendering();
            RenderBatchingSwitch.RestoreProjectSetting();
        }

        void Start()
        {
            if (!CommandLine.IsBenchmark)
                return;
            if (mapBuilder != null && Enum.TryParse(CommandLine.Get("-display"), true, out MapDisplay display))
                mapBuilder.Display = display;
            if (Enum.TryParse(CommandLine.Get("-batching"), true, out RenderBatching batching))
                RenderBatchingSwitch.Apply(batching);
            if (string.Equals(CommandLine.Get("-config"), AllExperimentsKey, StringComparison.OrdinalIgnoreCase))
            {
                List<BenchmarkConfig> all = experiments.Where(experiment => experiment != null).Select(WithCommandLine).ToList();
                if (Application.isBatchMode)
                {
                    Application.Quit(RunAllNow(all));
                    return;
                }
                quitWhenDone = true;
                StartAllSeries(all);
                return;
            }
            BenchmarkConfig config = ConfigFromCommandLine();
            if (config == null)
            {
                Debug.LogError($"[BenchmarkRunner] Nie ma eksperymentu „{CommandLine.Get("-config")}”. Dostępne: {string.Join(", ", experiments.ConvertAll(e => e.ExperimentName))}.");
                Application.Quit(2);
                return;
            }
            string output = CommandLine.Get("-out") ?? BenchmarkSession.DefaultOutputPath(config.ExperimentName);
            if (Application.isBatchMode)
            {
                Application.Quit(RunAllNow(config, output));
                return;
            }
            quitWhenDone = true;
            StartSeries(config, output);
        }

        public BenchmarkConfig FindExperiment(string experimentName) =>
            experiments.Find(experiment => experiment != null &&
                                           (string.Equals(experiment.ExperimentName, experimentName, StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(experiment.name, experimentName, StringComparison.OrdinalIgnoreCase)));

        /// <summary>Kopia eksperymentu z -config z poprawkami z wiersza poleceń albo null.</summary>
        public BenchmarkConfig ConfigFromCommandLine()
        {
            BenchmarkConfig preset = FindExperiment(CommandLine.Get("-config") ?? "E1");
            return preset != null ? WithCommandLine(preset) : null;
        }

        static BenchmarkConfig WithCommandLine(BenchmarkConfig preset)
        {
            BenchmarkConfig config = preset.Clone();
            config.SeedCount = CommandLine.GetInt("-seeds", config.SeedCount);
            config.FirstSeed = (uint)CommandLine.GetInt("-firstSeed", (int)config.FirstSeed);
            config.Repetitions = CommandLine.GetInt("-repetitions", config.Repetitions);
            config.WarmupRuns = CommandLine.GetInt("-warmup", config.WarmupRuns);
            config.ShowEveryNth = CommandLine.GetInt("-showEvery", config.ShowEveryNth);
            return config;
        }

        public void StartSeries(BenchmarkConfig config, string outputPath = null)
        {
            Stop();
            BeginSeries(config, outputPath);
        }

        /// <summary>
        /// Eksperymenty jeden po drugim, każdy z własnym plikiem wyników. Te z limitem czasu na mapę (najdłuższe) idą na końcu,
        /// a błąd w jednym nie zatrzymuje pozostałych.
        /// </summary>
        /// <param name="folder">Folder wyników; domyślnie BenchmarkSession.ResultsFolder.</param>
        public void StartAllSeries(IEnumerable<BenchmarkConfig> configs, string folder = null)
        {
            Stop();
            finishedSeries.Clear();
            queueFolder = folder;
            foreach (BenchmarkConfig config in configs.OrderBy(config => config.TimeLimitSeconds > 0 ? 1 : 0))
                queuedSeries.Enqueue(config);
            queueTotal = queuedSeries.Count;
            StartNextQueued();
        }

        void StartNextQueued()
        {
            while (queuedSeries.Count > 0)
            {
                BenchmarkConfig next = queuedSeries.Dequeue();
                try
                {
                    BeginSeries(next, queueFolder != null ? Path.Combine(queueFolder, $"{next.ExperimentName}.csv") : null);
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    finishedSeries.Add($"{next.ExperimentName}: błąd, {exception.Message}");
                }
            }
            Status = "Wszystkie eksperymenty zakończone.\n" + string.Join("\n", finishedSeries);
            Debug.Log($"[BenchmarkRunner] {Status}");
            queueTotal = 0;
            if (quitWhenDone)
                Application.Quit(0);
        }

        void BeginSeries(BenchmarkConfig config, string outputPath)
        {
            if (generator != null)
                generator.Clear();
            session = new BenchmarkSession(config, outputPath ?? BenchmarkSession.DefaultOutputPath(config.ExperimentName), MachineInfo.Current());
            SaveRendering();
            OutputPath = session.OutputPath;
            CurrentConfig = config;
            CompletedRuns = 0;
            TotalRuns = session.TotalRuns;
            IsPaused = false;
            LastRun = null;
            Status = $"Start. {config.Summary()}";
            Debug.Log($"[BenchmarkRunner] {Status} Wyniki: {OutputPath}");
            WarnIfBurstDisabled(config);
        }

        public void TogglePause()
        {
            if (IsRunning)
                IsPaused = !IsPaused;
        }

        public void Stop()
        {
            queuedSeries.Clear();
            queueTotal = 0;
            if (session == null)
                return;
            if (pendingRun != null)
            {
                session.CompleteRun(pendingRun);
                pendingRun = null;
            }
            Status = $"Przerwano. Zapisane uruchomienia: {session.CompletedMeasuredRuns} z {session.MeasuredRuns}.";
            session.Dispose();
            session = null;
            IsPaused = false;
            RestoreRendering();
        }

        void Update()
        {
            SampleRenderStats();
            Advance();
        }

        /// <summary>Jeden krok serii: w oknie wywoływany raz na klatkę. Błąd kończy tę serię, nie zawiesza aplikacji.</summary>
        public void Advance()
        {
            if (!IsRunning || IsPaused)
                return;
            try
            {
                if (pendingRun != null)
                {
                    pendingRun.FirstFrameMs ??= (Time.realtimeSinceStartupAsDouble - shownAt) * 1000.0;
                    MeasurePendingRun();
                    return;
                }
                BenchmarkRunResult run = session.StartNext(Show);
                if (run.BuildMs != null && renderStats != null)
                {
                    pendingRun = run;
                    settleLeft = SettleFrames;
                }
                else
                {
                    CompleteRun(run, null);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Status = $"Błąd w serii {session.Config.ExperimentName}: {exception.Message}";
                finishedSeries.Add($"{session.Config.ExperimentName}: błąd, {exception.Message}");
                pendingRun = null;
                session.Dispose();
                session = null;
                RestoreRendering();
                if (queueTotal > 0)
                    StartNextQueued();
                else if (quitWhenDone)
                    Application.Quit(3);
            }
        }

        void SampleRenderStats()
        {
            if (renderStats == null)
                return;
            renderStats.Sample();
            if (pendingRun == null && renderStats.Frames >= LiveFrames)
            {
                LiveRenderStats = renderStats.Result();
                renderStats.Reset();
            }
        }

        /// <summary>Mapa stoi: najpierw klatki na ustabilizowanie, potem pomiar, a na końcu zapis wiersza.</summary>
        void MeasurePendingRun()
        {
            if (settleLeft > 0)
            {
                if (--settleLeft == 0)
                    renderStats.Reset();
                return;
            }
            if (renderStats.Frames < session.Config.MeasuredFrames)
                return;
            BenchmarkRunResult run = pendingRun;
            pendingRun = null;
            RenderStats stats = renderStats.Result();
            LiveRenderStats = stats;
            renderStats.Reset();
            CompleteRun(run, stats);
        }

        void CompleteRun(BenchmarkRunResult run, RenderStats? stats)
        {
            session.CompleteRun(run, stats);
            LastRun = run;
            CompletedRuns = session.CompletedRuns;
            Status = session.Describe(run);
            if (session.IsFinished)
                Finish();
        }

        double? Show(BenchmarkRunResult run)
        {
            if (mapBuilder == null || Application.isBatchMode)
                return null;
            // Rozgrzewka też stawia mapę, żeby koszt pierwszego użycia (odczyt modeli, kompilacja zadania Burst) nie trafił do pierwszego wiersza wyników.
            if (!run.Warmup && session.CompletedMeasuredRuns % session.Config.ShowEveryNth != 0)
                return null;
            // Od zera, jak przycisk Generuj: inaczej powtórzone ziarno nie stawiałoby nic, a inne ziarno tylko różnicę między mapami.
            if (run.Variant.Display is MapDisplay display)
                mapBuilder.Display = display;
            if (run.Variant.Batching is RenderBatching batching)
                RenderBatchingSwitch.Apply(batching);
            mapBuilder.Clear();
            mapBuilder.Show(run.Variant.Tileset, run.Rules, run.Result.Grid, run.Result.States, run.Seed);
            run.Display = mapBuilder.Display.ToString();
            run.Batching = RenderBatchingSwitch.Current.ToString();
            run.ChunkSize = mapBuilder.Combined?.ChunkSize;
            run.MeshBytes = mapBuilder.Combined?.MeshBytes;
            shownAt = Time.realtimeSinceStartupAsDouble;
            return mapBuilder.LastBuildMs;
        }

        void Finish()
        {
            Status = $"Gotowe. Zapisane uruchomienia: {session.CompletedMeasuredRuns}, bez mapy: {session.FailedRuns}, naruszenia zasad: {session.Violations}." +
                     DescribeDifference(session) + session.DescribeCorrelation();
            Debug.Log($"[BenchmarkRunner] {Status} Wyniki: {OutputPath}");
            finishedSeries.Add($"{session.Config.ExperimentName}: {Status}");
            session.Dispose();
            session = null;
            RestoreRendering();
            if (queueTotal > 0)
            {
                StartNextQueued();
                return;
            }
            if (quitWhenDone)
                Application.Quit(0);
        }

        void SaveRendering()
        {
            if (renderingSaved)
                return;
            renderingSaved = true;
            displayBeforeSeries = mapBuilder != null ? mapBuilder.Display : default;
            batchingBeforeSeries = RenderBatchingSwitch.Current;
        }

        /// <summary>Po serii wraca sposób rysowania i łączenie sprzed niej, czyli to, co pokazuje okno.</summary>
        void RestoreRendering()
        {
            if (!renderingSaved)
                return;
            renderingSaved = false;
            if (mapBuilder != null)
                mapBuilder.Display = displayBeforeSeries;
            RenderBatchingSwitch.Apply(batchingBeforeSeries);
        }

        /// <summary>Cała seria od razu, bez okna. Zwraca kod wyjścia: 0 bez naruszeń zasad, 1 gdy były naruszenia.</summary>
        public int RunAllNow(BenchmarkConfig config, string outputPath)
        {
            if (config.RequiresWindow)
            {
                Debug.LogError($"[BenchmarkRunner] Eksperyment {config.ExperimentName} porównuje rysowanie, więc działa tylko w oknie: uruchom go bez -batchmode -nographics.");
                return 2;
            }
            using (var batch = new BenchmarkSession(config, outputPath, MachineInfo.Current()))
            {
                OutputPath = batch.OutputPath;
                Debug.Log($"[BenchmarkRunner] Start. {config.Summary()} Wyniki: {OutputPath}");
                WarnIfBurstDisabled(config);
                int lastVariant = -1;
                while (!batch.IsFinished)
                {
                    BenchmarkRunResult run = batch.RunNext();
                    if (run.VariantIndex != lastVariant || batch.CompletedRuns % 10 == 0 || batch.IsFinished)
                        Debug.Log($"[BenchmarkRunner] {batch.Describe(run)}");
                    lastVariant = run.VariantIndex;
                }
                Status = $"Gotowe. Zapisane uruchomienia: {batch.CompletedMeasuredRuns}, bez mapy: {batch.FailedRuns}, naruszenia zasad: {batch.Violations}." +
                         DescribeDifference(batch) + batch.DescribeCorrelation();
                Debug.Log($"[BenchmarkRunner] {Status} Wyniki: {OutputPath}");
                return batch.Violations == 0 ? 0 : 1;
            }
        }

        /// <summary>Wszystkie eksperymenty od razu, bez okna, oprócz tych, które porównują rysowanie. Zwraca najgorszy kod wyjścia.</summary>
        int RunAllNow(IEnumerable<BenchmarkConfig> configs)
        {
            int exitCode = 0;
            foreach (BenchmarkConfig config in configs.Where(config => !config.RequiresWindow).OrderBy(config => config.TimeLimitSeconds > 0 ? 1 : 0))
                exitCode = Math.Max(exitCode, RunAllNow(config, BenchmarkSession.DefaultOutputPath(config.ExperimentName)));
            return exitCode;
        }

        static string DescribeDifference(BenchmarkSession finished) =>
            finished.AverageDifference is double all
                ? $" Mapy kolejnych ziaren różnią się średnio w {all:0}% komórek, w zabudowie w {finished.AverageBuiltDifference:0}%."
                : string.Empty;

        /// <summary>Przy wyłączonym Burst wariant Burst liczyłby się jak zwykły C# (czyli jak Bitset).</summary>
        static void WarnIfBurstDisabled(BenchmarkConfig config)
        {
            if (config.Solvers.Contains(SolverKind.Burst) && !BurstSolver.IsBurstCompiled)
                Debug.LogWarning("[BenchmarkRunner] Burst jest wyłączony: wariant Burst wykonuje zwykły C#, tak jak Bitset.");
        }

        void OnDestroy()
        {
            session?.Dispose();
            session = null;
        }
    }
}
