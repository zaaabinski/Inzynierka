using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>Jedno uruchomienie w badaniu: wariant, ziarno, wynik generacji i pomiary.</summary>
    public sealed class BenchmarkRunResult
    {
        public BenchmarkVariant Variant;
        public int VariantIndex;
        public uint Seed;

        /// <summary>Numer powtórzenia ziarna (od 1); 0 dla rozgrzewki.</summary>
        public int Repetition;

        public bool Warmup;
        public CompiledRules Rules;
        public GenerationResult Result;
        public RuleCheckReport Check;
        public StructureStats Stats;

        /// <summary>Bajty zaalokowane na stercie przez rozwiązywacz (licznik alokacji wątku).</summary>
        public long AllocatedBytes;

        /// <summary>Największy rozmiar struktur danych rozwiązywacza i selektora (IWfcSolver.PeakMemoryBytes).</summary>
        public long PeakMemoryBytes;

        /// <summary>Czas stawiania kostek w oknie albo null, gdy mapy nie pokazano.</summary>
        public double? BuildMs;
    }

    /// <summary>
    /// Seria uruchomień z ustawień eksperymentu. Wspólna dla okna (jedno uruchomienie na klatkę) i trybu bez okna
    /// (wszystkie od razu). Przed każdym uruchomieniem odśmiecanie pamięci, poza mierzonym czasem; czas generacji
    /// liczy sam rozwiązywacz, a sprawdzanie mapy i stawianie kostek są poza nim.
    /// </summary>
    public sealed class BenchmarkSession : IDisposable
    {
        readonly List<BenchmarkVariant> variants;
        readonly CsvWriter writer;
        readonly MachineInfo machine;
        readonly Dictionary<TilesetDefinition, CompiledRules> rulesCache = new Dictionary<TilesetDefinition, CompiledRules>();
        int stepInVariant;

        public BenchmarkSession(BenchmarkConfig config, string outputPath, MachineInfo machine)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            variants = config.Variants();
            if (variants.Count == 0)
                throw new InvalidOperationException($"Eksperyment {config.ExperimentName} nie ma wariantów: dodaj co najmniej jeden zestaw kafelków i rozmiar siatki.");
            if (variants.Any(variant => variant.Tileset == null))
                throw new InvalidOperationException($"Eksperyment {config.ExperimentName} ma pusty wpis na liście zestawów.");
            this.machine = machine ?? MachineInfo.Current();
            writer = new CsvWriter(outputPath);
        }

        public BenchmarkConfig Config { get; }

        public IReadOnlyList<BenchmarkVariant> Variants => variants;

        public string OutputPath => writer.Path;

        public int VariantIndex { get; private set; }

        public bool IsFinished => VariantIndex >= variants.Count;

        /// <summary>Wszystkie wykonane uruchomienia, z rozgrzewką.</summary>
        public int CompletedRuns { get; private set; }

        /// <summary>Uruchomienia zapisane do pliku.</summary>
        public int CompletedMeasuredRuns { get; private set; }

        public int TotalRuns => Config.TotalRuns;

        public int MeasuredRuns => Config.MeasuredRuns;

        public int FailedRuns { get; private set; }

        public int Violations { get; private set; }

        /// <summary>Folder Wyniki obok aplikacji (w edytorze obok folderu Assets).</summary>
        public static string ResultsFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Wyniki");

        public static string DefaultOutputPath(string experimentName) =>
            Path.Combine(ResultsFolder, $"{experimentName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        /// <summary>Wykonuje następne uruchomienie (rozgrzewkowe albo mierzone) i zapisuje mierzone do pliku.</summary>
        /// <param name="show">Wywoływane po generacji, przed zapisem; zwraca czas stawiania kostek albo null.</param>
        public BenchmarkRunResult RunNext(Func<BenchmarkRunResult, double?> show = null)
        {
            if (IsFinished)
                throw new InvalidOperationException("Seria jest już zakończona.");

            BenchmarkVariant variant = variants[VariantIndex];
            bool warmup = stepInVariant < Config.WarmupRuns;
            uint seed;
            int repetition;
            if (warmup)
            {
                seed = Config.FirstSeed + (uint)(stepInVariant % Config.SeedCount);
                repetition = 0;
            }
            else
            {
                int measured = stepInVariant - Config.WarmupRuns;
                seed = Config.FirstSeed + (uint)(measured / Config.Repetitions);
                repetition = measured % Config.Repetitions + 1;
            }

            BenchmarkRunResult run = Execute(variant, seed);
            run.VariantIndex = VariantIndex;
            run.Repetition = repetition;
            run.Warmup = warmup;
            double? buildMs = show?.Invoke(run);
            if (!warmup)
            {
                run.BuildMs = buildMs;
                Write(run);
                CompletedMeasuredRuns++;
                FailedRuns += run.Result.Success ? 0 : 1;
                Violations += run.Check?.Total ?? 0;
            }

            CompletedRuns++;
            stepInVariant++;
            if (stepInVariant >= Config.WarmupRuns + Config.SeedCount * Config.Repetitions)
            {
                VariantIndex++;
                stepInVariant = 0;
            }
            return run;
        }

        /// <summary>Opis postępu po polsku, do okna i konsoli.</summary>
        public string Describe(BenchmarkRunResult run)
        {
            string what = run.Warmup ? "rozgrzewka" : $"ziarno {run.Seed}, powtórzenie {run.Repetition}";
            SolverMetrics metrics = run.Result.Metrics;
            string outcome = run.Result.Success
                ? $"{metrics.TotalMs:0} ms, próby: {metrics.Attempts}, naruszenia: {run.Check?.Total ?? 0}"
                : $"bez mapy ({run.Result.Status}), próby: {metrics.Attempts}";
            return $"Wariant {run.VariantIndex + 1} z {variants.Count}: {run.Variant}\n" +
                   $"Uruchomienie {CompletedRuns} z {TotalRuns} ({what}): {outcome}.";
        }

        public void Dispose() => writer.Dispose();

        BenchmarkRunResult Execute(BenchmarkVariant variant, uint seed)
        {
            CompiledRules rules = RulesFor(variant.Tileset);
            var size = new int3(variant.Size.x, variant.Size.y, variant.Size.z);
            IWfcSolver solver = WfcFactory.CreateSolver(variant.Solver);
            SolverSettings settings = WfcFactory.CreateSettings(variant.Selector, variant.Contradiction, Config.MaxAttempts, Config.TimeLimitSeconds);

            // Odśmiecanie przed pomiarem i wyłączone na czas generacji (w zbudowanej aplikacji; edytor tego nie pozwala),
            // żeby nie wpadało w mierzony czas i żeby przyrost sterty był równy alokacjom.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GarbageCollector.Mode previousMode = GarbageCollector.GCMode;
            if (!Application.isEditor)
                GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
            long heapBefore = GC.GetTotalMemory(false);
            long threadBefore = GC.GetAllocatedBytesForCurrentThread();
            GenerationResult result;
            long allocated;
            try
            {
                solver.Initialize(rules, size, seed, settings);
                result = solver.Run();
                long threadAllocated = GC.GetAllocatedBytesForCurrentThread() - threadBefore;
                long heapGrowth = GC.GetTotalMemory(false) - heapBefore;
                // Licznik alokacji wątku działa w edytorze, a w odtwarzaczu Mono zwraca 0; wtedy przyrost sterty.
                allocated = threadAllocated > 0 ? threadAllocated : Math.Max(0, heapGrowth);
            }
            finally
            {
                GarbageCollector.GCMode = previousMode;
            }

            var run = new BenchmarkRunResult
            {
                Variant = variant,
                Seed = seed,
                Rules = rules,
                Result = result,
                AllocatedBytes = allocated,
                PeakMemoryBytes = solver.PeakMemoryBytes
            };
            if (result.Success)
            {
                run.Check = RuleChecker.Check(rules, size, result.States);
                run.Stats = StructureAnalyzer.Analyze(rules, size, result.States);
            }
            return run;
        }

        CompiledRules RulesFor(TilesetDefinition tileset)
        {
            if (!rulesCache.TryGetValue(tileset, out CompiledRules rules))
                rulesCache[tileset] = rules = RuleCompiler.Compile(tileset.ToTilesetData());
            return rules;
        }

        void Write(BenchmarkRunResult run)
        {
            SolverMetrics m = run.Result.Metrics;
            StructureStats s = run.Stats;
            Vector3Int size = run.Variant.Size;
            writer.Write(new object[]
            {
                DateTime.Now, machine.Computer, machine.Processor, machine.RamGb, machine.UnityVersion, machine.AppVersion,
                Config.ExperimentName, run.Result.SolverName, run.Result.SelectorName, run.Result.ContradictionHandlerName,
                run.Variant.Tileset.name, size.x, size.z, size.y, run.Seed, run.Repetition,
                run.Result.Success, m.Attempts, m.TotalMs, m.SelectionMs, m.PropagationMs, m.BacktrackMs,
                m.Decisions, m.PropagationSteps, m.RemovedPossibilities, m.Contradictions, m.Backtracks, m.Restarts,
                run.PeakMemoryBytes / (1024.0 * 1024.0), run.AllocatedBytes / 1024.0,
                run.Check?.Total, s?.Towers, s?.Keeps, s?.Bridges, s?.Stairs,
                Average(s?.TowerHeights), Average(s?.WallHeights), Average(s?.KeepHeights),
                s?.ReachableTerrainPercent, s?.EnclosedCourtyards,
                run.BuildMs, machine.Environment
            });
        }

        static object Average(List<int> values) => values == null || values.Count == 0 ? null : (object)values.Average();
    }
}
