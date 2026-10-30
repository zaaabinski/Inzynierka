using System;
using System.Collections.Generic;
using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Prowadzi badanie w scenie Generator. W oknie: jedno uruchomienie na klatkę, mapy widoczne (co N-ta),
    /// Pauza i Stop działają po bieżącym uruchomieniu. Z argumentem -benchmark badanie startuje samo:
    /// z oknem pokazuje mapy i zamyka aplikację na końcu, a z -batchmode -nographics liczy wszystko bez okna.
    /// Argumenty: -config E1, -out plik.csv, opcjonalnie -seeds, -firstSeed, -repetitions, -warmup, -showEvery.
    /// </summary>
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        [Tooltip("Eksperymenty do wyboru w oknie i z wiersza poleceń (-config E1).")]
        [SerializeField] List<BenchmarkConfig> experiments = new List<BenchmarkConfig>();
        [SerializeField] MapBuilder mapBuilder;
        [SerializeField] GeneratorController generator;

        BenchmarkSession session;
        bool quitWhenDone;

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

        void Start()
        {
            if (!CommandLine.IsBenchmark)
                return;
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
            if (preset == null)
                return null;
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
            if (generator != null)
                generator.Clear();
            session = new BenchmarkSession(config, outputPath ?? BenchmarkSession.DefaultOutputPath(config.ExperimentName), MachineInfo.Current());
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
            if (session == null)
                return;
            Status = $"Przerwano. Zapisane uruchomienia: {session.CompletedMeasuredRuns} z {session.MeasuredRuns}.";
            session.Dispose();
            session = null;
            IsPaused = false;
        }

        void Update()
        {
            if (!IsRunning || IsPaused)
                return;
            LastRun = session.RunNext(Show);
            CompletedRuns = session.CompletedRuns;
            Status = session.Describe(LastRun);
            if (session.IsFinished)
                Finish();
        }

        double? Show(BenchmarkRunResult run)
        {
            if (mapBuilder == null || Application.isBatchMode || run.Warmup)
                return null;
            if (session.CompletedMeasuredRuns % session.Config.ShowEveryNth != 0)
                return null;
            mapBuilder.Show(run.Variant.Tileset, run.Rules, run.Result.Grid, run.Result.States, run.Seed);
            return mapBuilder.LastBuildMs;
        }

        void Finish()
        {
            Status = $"Gotowe. Zapisane uruchomienia: {session.CompletedMeasuredRuns}, bez mapy: {session.FailedRuns}, naruszenia zasad: {session.Violations}." +
                     DescribeDifference(session);
            Debug.Log($"[BenchmarkRunner] {Status} Wyniki: {OutputPath}");
            session.Dispose();
            session = null;
            if (quitWhenDone)
                Application.Quit(0);
        }

        /// <summary>Cała seria od razu, bez okna. Zwraca kod wyjścia: 0 bez naruszeń zasad, 1 gdy były naruszenia.</summary>
        public int RunAllNow(BenchmarkConfig config, string outputPath)
        {
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
                         DescribeDifference(batch);
                Debug.Log($"[BenchmarkRunner] {Status} Wyniki: {OutputPath}");
                return batch.Violations == 0 ? 0 : 1;
            }
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
