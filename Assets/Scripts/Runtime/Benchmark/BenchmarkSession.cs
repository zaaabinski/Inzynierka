using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
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

        /// <summary>Największy rozmiar struktur danych rozwiązywacza i selektora, szacowany z rozmiarów tablic (IWfcSolver.PeakMemoryBytes).</summary>
        public long PeakMemoryBytes;

        /// <summary>Rzeczywista pamięć rozwiązywacza: przyrost sterty po generacji i odśmieceniu, gdy rozwiązywacz jeszcze żyje.</summary>
        public long RetainedBytes;

        /// <summary>Czas stawiania kostek w oknie albo null, gdy mapy nie pokazano.</summary>
        public double? BuildMs;

        /// <summary>
        /// Różnica od mapy poprzedniego ziarna w tym samym wariancie albo null (pierwsze ziarno wariantu, rozgrzewka,
        /// brak mapy). Powtórzenia tego samego ziarna też porównują się z poprzednim ziarnem, nie ze sobą.
        /// </summary>
        public MapDifference? Difference;

        /// <summary>Sposób pokazania mapy (MapDisplay) albo null, gdy mapy nie pokazano.</summary>
        public string Display;

        /// <summary>Łączenie rysowania w URP (RenderBatching) albo null, gdy mapy nie pokazano.</summary>
        public string Batching;

        /// <summary>Bok fragmentu scalonej mapy; null w innych sposobach rysowania.</summary>
        public int? ChunkSize;

        /// <summary>Rozmiar scalonych siatek w bajtach; null w innych sposobach rysowania.</summary>
        public long? MeshBytes;

        /// <summary>Od postawienia mapy do początku następnej klatki: pierwsze rysowanie nowej mapy.</summary>
        public double? FirstFrameMs;

        /// <summary>Koszt rysowania mapy w oknie albo null, gdy mapy nie pokazano.</summary>
        public RenderStats? RenderStats;
    }

    /// <summary>
    /// Seria uruchomień z ustawień eksperymentu. Wspólna dla okna (jedno uruchomienie na klatkę) i trybu bez okna
    /// (wszystkie od razu). Przed każdym uruchomieniem odśmiecanie pamięci, poza mierzonym czasem; czas generacji
    /// liczy sam rozwiązywacz, a sprawdzanie mapy i stawianie kostek są poza nim.
    /// </summary>
    public sealed class BenchmarkSession : IDisposable
    {
        /// <summary>Zabudowa parteru w procentach, poniżej której mapa jest pusta: reguły usunęły budynki.</summary>
        const double EmptyMapBuiltPercent = 1.0;

        /// <summary>Zabudowa parteru w procentach, powyżej której mapa jest w całości zabudowana: reguły usunęły teren.</summary>
        const double FullMapBuiltPercent = 90.0;

        static readonly string[] VariantColumns =
        {
            "eksperyment", "rozwiazywacz", "kolejnosc", "sprzecznosc", "zestaw", "x", "y", "z", "rysowanie", "laczenie_rysowania",
            "zaostrzenie_proc", "losowanie_zaostrzenia", "mapy", "udane_mapy", "proby", "sprzecznosci", "udane_proby_proc", "zabudowa_proc",
            "gestosc_regul_proc", "gestosc_wazona_proc", "kappa", "czas_ms_mediana", "czas_ms_q1", "czas_ms_q3", "czas_stawiania_ms_mediana",
            "czas_klatki_ms_mediana", "najdluzsza_klatka_ms_mediana", "wywolania_rysowania_mediana", "narysowane_obiekty_mediana",
            "pamiec_rzeczywista_mb_mediana", "pamiec_unity_mb_mediana"
        };

        /// <summary>Wyniki jednego wariantu do pliku podsumowania i korelacji.</summary>
        sealed class VariantTally
        {
            public BenchmarkRunResult Last;
            public RuleRestrictiveness Restrictiveness;
            public int Maps;
            public int SuccessfulMaps;
            public int Attempts;
            public int Contradictions;
            public double BuiltPercentSum;
            public readonly List<double> GenerationMs = new List<double>();
            public readonly List<double> BuildMs = new List<double>();
            public readonly List<double> FrameMs = new List<double>();
            public readonly List<double> LongestFrameMs = new List<double>();
            public readonly List<double> DrawCalls = new List<double>();
            public readonly List<double> DrawnObjects = new List<double>();
            public readonly List<double> RetainedMb = new List<double>();
            public readonly List<double> UnityMemoryMb = new List<double>();

            public double SuccessPercent => Attempts == 0 ? 0 : 100.0 * SuccessfulMaps / Attempts;

            public double BuiltPercent => SuccessfulMaps == 0 ? 0 : BuiltPercentSum / SuccessfulMaps;

            /// <summary>Mapy powstały, ale bez zamku: reguły usunęły to, co trudne, więc sukces nic nie mówi.</summary>
            public bool HasDegenerateMaps => SuccessfulMaps > 0 && IsDegenerateMap(BuiltPercent);
        }

        readonly List<BenchmarkVariant> variants;
        readonly CsvWriter writer;
        readonly MachineInfo machine;
        readonly Dictionary<(TilesetDefinition, double, uint), CompiledRules> rulesCache = new Dictionary<(TilesetDefinition, double, uint), CompiledRules>();
        readonly Dictionary<CompiledRules, RuleRestrictiveness> restrictivenessCache = new Dictionary<CompiledRules, RuleRestrictiveness>();
        readonly Dictionary<int, VariantTally> tallies = new Dictionary<int, VariantTally>();
        bool disposed;
        int stepInVariant;

        // Do różnicy między ziarnami: pierwsza udana mapa bieżącego ziarna i ostatnia udana mapa wcześniejszego ziarna.
        uint? currentSeed;
        int[] currentSeedMap;
        int[] previousSeedMap;
        double differenceSum;
        double builtDifferenceSum;

        public BenchmarkSession(BenchmarkConfig config, string outputPath, MachineInfo machine)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            variants = config.Variants();
            if (variants.Count == 0)
                throw new InvalidOperationException(config.SkippedVariants > 0
                    ? $"Eksperyment {config.ExperimentName} nie ma wariantów: wybrany rozwiązywacz nie umie tej reakcji na sprzeczność (cofanie i reset lokalny działają z Naive, Bitset i Burst)."
                    : $"Eksperyment {config.ExperimentName} nie ma wariantów: dodaj co najmniej jeden zestaw kafelków i rozmiar siatki.");
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

        /// <summary>Uruchomienia z policzoną różnicą od poprzedniego ziarna.</summary>
        public int ComparedRuns { get; private set; }

        /// <summary>Średni odsetek komórek z innym kafelkiem niż w mapie poprzedniego ziarna; null, gdy nic nie porównano.</summary>
        public double? AverageDifference => ComparedRuns == 0 ? (double?)null : differenceSum / ComparedRuns;

        /// <summary>To samo dla komórek zabudowy.</summary>
        public double? AverageBuiltDifference => ComparedRuns == 0 ? (double?)null : builtDifferenceSum / ComparedRuns;

        /// <summary>Czy mapa o takiej zabudowie parteru nie jest już zamkiem: jest pusta albo nie ma na niej terenu.</summary>
        public static bool IsDegenerateMap(double builtPercent) => builtPercent < EmptyMapBuiltPercent || builtPercent > FullMapBuiltPercent;

        /// <summary>Folder Wyniki obok aplikacji (w edytorze obok folderu Assets).</summary>
        public static string ResultsFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Wyniki");

        public static string DefaultOutputPath(string experimentName) =>
            Path.Combine(ResultsFolder, $"{experimentName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        /// <summary>Wykonuje następne uruchomienie (rozgrzewkowe albo mierzone) i zapisuje mierzone do pliku.</summary>
        /// <param name="show">Wywoływane po generacji, przed zapisem; zwraca czas stawiania kostek albo null.</param>
        public BenchmarkRunResult RunNext(Func<BenchmarkRunResult, double?> show = null)
        {
            BenchmarkRunResult run = StartNext(show);
            CompleteRun(run);
            return run;
        }

        /// <summary>
        /// Generacja i pokazanie mapy, bez zapisu. W oknie między StartNext a CompleteRun mija kilka klatek na pomiar
        /// rysowania tej mapy; bez okna od razu RunNext.
        /// </summary>
        /// <param name="show">Wywoływane po generacji; zwraca czas stawiania kostek albo null.</param>
        public BenchmarkRunResult StartNext(Func<BenchmarkRunResult, double?> show = null)
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
                run.BuildMs = buildMs;
            return run;
        }

        /// <summary>Kończy uruchomienie ze StartNext: mierzone trafia do pliku, seria przechodzi dalej.</summary>
        /// <param name="renderStats">Koszt rysowania tej mapy albo null, gdy nie mierzono.</param>
        public void CompleteRun(BenchmarkRunResult run, RenderStats? renderStats = null)
        {
            if (!run.Warmup)
            {
                run.RenderStats = renderStats;
                CompareWithPreviousSeed(run);
                Write(run);
                Tally(run);
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
                currentSeed = null;
                currentSeedMap = null;
                previousSeedMap = null;
            }
        }

        /// <summary>Różnorodność: każda udana mapa z ostatnią udaną mapą wcześniejszego ziarna w tym samym wariancie.</summary>
        void CompareWithPreviousSeed(BenchmarkRunResult run)
        {
            if (currentSeed != run.Seed)
            {
                if (currentSeedMap != null)
                    previousSeedMap = currentSeedMap;
                currentSeed = run.Seed;
                currentSeedMap = null;
            }
            if (!run.Result.Success)
                return;
            currentSeedMap ??= run.Result.States;
            if (previousSeedMap == null)
                return;
            Vector3Int size = run.Variant.Size;
            MapDifference difference = DiversityAnalyzer.Compare(run.Rules, new int3(size.x, size.y, size.z), previousSeedMap, run.Result.States);
            run.Difference = difference;
            ComparedRuns++;
            differenceSum += difference.AllPercent;
            builtDifferenceSum += difference.BuiltPercent;
        }

        /// <summary>Opis postępu po polsku, do okna i konsoli.</summary>
        public string Describe(BenchmarkRunResult run)
        {
            string what = run.Warmup ? "rozgrzewka" : $"ziarno {run.Seed}, powtórzenie {run.Repetition}";
            SolverMetrics metrics = run.Result.Metrics;
            string outcome = run.Result.Success
                ? $"{metrics.TotalMs:0} ms, próby: {metrics.Attempts}, naruszenia: {run.Check?.Total ?? 0}"
                : $"bez mapy ({run.Result.Status}), próby: {metrics.Attempts}";
            if (run.Difference is MapDifference difference)
                outcome += $", inna niż poprzednie ziarno: {difference.AllPercent:0}% komórek, zabudowa {difference.BuiltPercent:0}%";
            return $"Wariant {run.VariantIndex + 1} z {variants.Count}: {run.Variant}\n" +
                   $"Uruchomienie {CompletedRuns} z {TotalRuns} ({what}): {outcome}.";
        }

        /// <summary>
        /// Korelacja restrykcyjności reguł z odsetkiem udanych prób, po polsku; pusty tekst, gdy reguły wariantów się nie
        /// różnią. W regułach tak zaostrzonych, że zniknęły budynki albo teren, sukces nic nie mówi, więc jest też wynik bez nich.
        /// </summary>
        public string DescribeCorrelation()
        {
            List<VariantTally> all = TalliesWithDifferentRules();
            if (all.Count == 0)
                return string.Empty;
            List<VariantTally> castles = all.Where(tally => !tally.HasDegenerateMaps).ToList();
            string Row(List<VariantTally> list) =>
                $"gęstość reguł {Correlation(list, t => t.Restrictiveness.PairDensityPercent):0.00}, gęstość ważona {Correlation(list, t => t.Restrictiveness.WeightedDensityPercent):0.00}, κ {Correlation(list, t => t.Restrictiveness.Kappa):0.00}";
            string text = $" Korelacja Spearmana z odsetkiem udanych prób ({all.Count} wariantów): {Row(all)}.";
            if (castles.Count != all.Count)
                text += $" Bez wariantów z mapami pustymi albo w całości zabudowanymi ({castles.Count}): {Row(castles)}.";
            return text;
        }

        /// <summary>Zamyka plik wyników i zapisuje obok niego podsumowanie wariantów oraz korelacje, także po przerwanej serii.</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            writer.Dispose();
            if (tallies.Count == 0)
                return;
            string basePath = Path.Combine(Path.GetDirectoryName(OutputPath) ?? ".", Path.GetFileNameWithoutExtension(OutputPath));
            WriteVariantSummary(basePath + "_warianty.csv");
            WriteCorrelations(basePath + "_korelacje.csv");
        }

        BenchmarkRunResult Execute(BenchmarkVariant variant, uint seed)
        {
            CompiledRules rules = RulesFor(variant);
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
                // Licznik alokacji wątku działa w edytorze, a w aplikacji (Mono i IL2CPP) zwraca 0; wtedy przyrost sterty.
                allocated = threadAllocated > 0 ? threadAllocated : Math.Max(0, heapGrowth);
            }
            finally
            {
                GarbageCollector.GCMode = previousMode;
            }
            // Poza mierzonym czasem: po odśmieceniu zostaje to, co naprawdę trzyma rozwiązywacz.
            long retained = Math.Max(0, GC.GetTotalMemory(true) - heapBefore);

            var run = new BenchmarkRunResult
            {
                Variant = variant,
                Seed = seed,
                Rules = rules,
                Result = result,
                AllocatedBytes = allocated,
                PeakMemoryBytes = solver.PeakMemoryBytes,
                RetainedBytes = retained
            };
            if (result.Success)
            {
                using (WfcMarkers.MapCheck.Auto())
                {
                    run.Check = RuleChecker.Check(rules, size, result.States);
                    run.Stats = StructureAnalyzer.Analyze(rules, size, result.States);
                }
            }
            return run;
        }

        CompiledRules RulesFor(BenchmarkVariant variant)
        {
            var key = (variant.Tileset, variant.TighteningPercent, variant.TighteningDraw);
            if (!rulesCache.TryGetValue(key, out CompiledRules rules))
            {
                TilesetData data = variant.Tileset.ToTilesetData();
                data.TighteningFraction = variant.TighteningPercent / 100.0;
                data.TighteningDraw = variant.TighteningDraw;
                rulesCache[key] = rules = RuleCompiler.Compile(data);
            }
            return rules;
        }

        RuleRestrictiveness RestrictivenessOf(CompiledRules rules)
        {
            if (!restrictivenessCache.TryGetValue(rules, out RuleRestrictiveness restrictiveness))
                restrictivenessCache[rules] = restrictiveness = RuleRestrictiveness.Of(rules);
            return restrictiveness;
        }

        void Write(BenchmarkRunResult run)
        {
            SolverMetrics m = run.Result.Metrics;
            StructureStats s = run.Stats;
            RenderStats? r = run.RenderStats;
            Vector3Int size = run.Variant.Size;
            RuleRestrictiveness restrictiveness = RestrictivenessOf(run.Rules);
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
                run.BuildMs, machine.Environment,
                run.Difference?.AllPercent, run.Difference?.GroundPercent, run.Difference?.BuiltPercent,
                run.Display, r?.DrawCalls, r?.DrawnInstances, r?.SetPassCalls, r?.Triangles, r?.Vertices, r?.FrameMs, r?.CpuMs, r?.GpuMs,
                m.RemovedByPropagation, m.RemovedByDecisions, run.RetainedBytes / BytesPerMb, s?.BuiltPercent,
                restrictiveness.PairDensityPercent, restrictiveness.WeightedDensityPercent, restrictiveness.Kappa,
                run.Variant.TighteningPercent, run.Variant.TighteningDraw,
                run.Batching, run.ChunkSize, run.MeshBytes / BytesPerMb,
                Profiler.GetTotalAllocatedMemoryLong() / BytesPerMb, GC.GetTotalMemory(false) / BytesPerMb, r?.ProcessMemoryBytes / BytesPerMb,
                r?.LongestFrameMs, run.FirstFrameMs
            });
        }

        void Tally(BenchmarkRunResult run)
        {
            if (!tallies.TryGetValue(run.VariantIndex, out VariantTally tally))
                tallies[run.VariantIndex] = tally = new VariantTally { Restrictiveness = RestrictivenessOf(run.Rules) };
            SolverMetrics metrics = run.Result.Metrics;
            tally.Last = run;
            tally.Maps++;
            tally.SuccessfulMaps += run.Result.Success ? 1 : 0;
            tally.Attempts += metrics.Attempts;
            tally.Contradictions += metrics.Contradictions;
            tally.BuiltPercentSum += run.Stats?.BuiltPercent ?? 0;
            tally.GenerationMs.Add(metrics.TotalMs);
            tally.RetainedMb.Add(run.RetainedBytes / BytesPerMb);
            tally.UnityMemoryMb.Add(Profiler.GetTotalAllocatedMemoryLong() / BytesPerMb);
            if (run.BuildMs is double buildMs)
                tally.BuildMs.Add(buildMs);
            if (run.RenderStats is RenderStats stats)
            {
                tally.FrameMs.Add(stats.FrameMs);
                if (stats.LongestFrameMs is double longest)
                    tally.LongestFrameMs.Add(longest);
                if (stats.DrawCalls is long drawCalls)
                    tally.DrawCalls.Add(drawCalls);
                if (stats.DrawnInstances is long drawn)
                    tally.DrawnObjects.Add(drawn);
            }
        }

        void WriteVariantSummary(string path)
        {
            using (var summary = new CsvWriter(path, VariantColumns))
                foreach (KeyValuePair<int, VariantTally> entry in tallies.OrderBy(entry => entry.Key))
                {
                    VariantTally t = entry.Value;
                    BenchmarkVariant variant = t.Last.Variant;
                    summary.Write(new object[]
                    {
                        Config.ExperimentName, t.Last.Result.SolverName, t.Last.Result.SelectorName, t.Last.Result.ContradictionHandlerName, variant.Tileset.name,
                        variant.Size.x, variant.Size.z, variant.Size.y, t.Last.Display, t.Last.Batching, variant.TighteningPercent, variant.TighteningDraw,
                        t.Maps, t.SuccessfulMaps, t.Attempts, t.Contradictions, t.SuccessPercent, t.BuiltPercent,
                        t.Restrictiveness.PairDensityPercent, t.Restrictiveness.WeightedDensityPercent, t.Restrictiveness.Kappa,
                        Statistics.Median(t.GenerationMs), Statistics.Quantile(t.GenerationMs, 0.25), Statistics.Quantile(t.GenerationMs, 0.75), Statistics.Median(t.BuildMs),
                        Statistics.Median(t.FrameMs), Statistics.Median(t.LongestFrameMs), Statistics.Median(t.DrawCalls), Statistics.Median(t.DrawnObjects),
                        Statistics.Median(t.RetainedMb), Statistics.Median(t.UnityMemoryMb)
                    });
                }
        }

        void WriteCorrelations(string path)
        {
            List<VariantTally> all = TalliesWithDifferentRules();
            if (all.Count == 0)
                return;
            List<VariantTally> castles = all.Where(tally => !tally.HasDegenerateMaps).ToList();
            using (var correlations = new CsvWriter(path, new[] { "miara", "warianty", "liczba_wariantow", "spearman_z_udanymi_probami" }))
                foreach ((string name, Func<VariantTally, double> measure) in new (string, Func<VariantTally, double>)[]
                {
                    ("gestosc_regul_proc", t => t.Restrictiveness.PairDensityPercent),
                    ("gestosc_wazona_proc", t => t.Restrictiveness.WeightedDensityPercent),
                    ("kappa", t => t.Restrictiveness.Kappa)
                })
                {
                    correlations.Write(new object[] { name, "wszystkie", all.Count, Correlation(all, measure) });
                    correlations.Write(new object[] { name, "bez_map_zdegenerowanych", castles.Count, Correlation(castles, measure) });
                }
        }

        List<VariantTally> TalliesWithDifferentRules()
        {
            List<VariantTally> all = tallies.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
            return all.Select(tally => tally.Restrictiveness.PairDensityPercent).Distinct().Count() >= 3 ? all : new List<VariantTally>();
        }

        static double Correlation(List<VariantTally> list, Func<VariantTally, double> measure) =>
            Statistics.Spearman(list.Select(measure).ToList(), list.Select(tally => tally.SuccessPercent).ToList());

        const double BytesPerMb = 1024.0 * 1024.0;

        static object Average(List<int> values) => values == null || values.Count == 0 ? null : (object)values.Average();
    }
}
