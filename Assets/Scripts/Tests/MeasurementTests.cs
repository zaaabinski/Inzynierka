using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using WFC.Core;
using WFC.Runtime;
using Object = UnityEngine.Object;

namespace WFC.Tests
{
    /// <summary>Pomiary do pracy: skuteczność propagacji, pamięć, restrykcyjność i zaostrzanie reguł, warianty rysowania, seria eksperymentów.</summary>
    public sealed class MeasurementTests
    {
        string folder;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void CreateFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "wfc_pomiary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            foreach (Object item in created)
                if (item != null)
                    Object.DestroyImmediate(item);
            created.Clear();
            RenderBatchingSwitch.RestoreProjectSetting();
        }

        static TilesetDefinition Tileset(string setName) => AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset(setName));

        static CompiledRules Tightened(double fraction, uint draw = 1)
        {
            TilesetData data = Tileset("Pelny").ToTilesetData();
            data.TighteningFraction = fraction;
            data.TighteningDraw = draw;
            return RuleCompiler.Compile(data);
        }

        BenchmarkConfig SmallConfig(string experimentName = "Test")
        {
            var config = ScriptableObject.CreateInstance<BenchmarkConfig>();
            created.Add(config);
            config.ExperimentName = experimentName;
            config.Tilesets.Add(Tileset("Prosty"));
            config.GridSizes.Clear();
            config.GridSizes.Add(new Vector3Int(6, 4, 6));
            config.Solvers.Clear();
            config.Solvers.Add(SolverKind.Bitset);
            config.FirstSeed = 1;
            config.SeedCount = 2;
            config.WarmupRuns = 0;
            return config;
        }

        static List<Dictionary<string, string>> ReadCsv(string path)
        {
            string[] lines = File.ReadAllLines(path);
            string[] header = lines[0].Split(',');
            return lines.Skip(1).Select(line => header.Zip(Split(line), (name, value) => (name, value)).ToDictionary(pair => pair.name, pair => pair.value)).ToList();
        }

        static IEnumerable<string> Split(string line)
        {
            bool quoted = false;
            var field = new System.Text.StringBuilder();
            foreach (char c in line)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    yield return field.ToString();
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }
            yield return field.ToString();
        }

        static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

        [Test]
        public void Propagacja_UsunieteMozliwosci_TeSameWeWszystkichRozwiazywaczach()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            var size = new int3(10, 8, 10);
            var metrics = new List<SolverMetrics>();
            foreach (SolverKind kind in new[] { SolverKind.Naive, SolverKind.SupportCount, SolverKind.Bitset, SolverKind.Burst })
            {
                IWfcSolver solver = WfcFactory.CreateSolver(kind);
                solver.Initialize(rules, size, 3, new SolverSettings());
                solver.Run();
                metrics.Add(solver.Metrics);
            }
            Assert.Greater(metrics[0].RemovedByPropagation, 0);
            Assert.Greater(metrics[0].RemovedByDecisions, 0);
            Assert.Greater(metrics[0].RemovedByPropagation, metrics[0].RemovedByDecisions, "propagacja usuwa więcej niż same decyzje");
            Assert.LessOrEqual(metrics[0].RemovedByPropagation + metrics[0].RemovedByDecisions, metrics[0].RemovedPossibilities);
            foreach (SolverMetrics other in metrics.Skip(1))
            {
                Assert.AreEqual(metrics[0].RemovedByPropagation, other.RemovedByPropagation);
                Assert.AreEqual(metrics[0].RemovedByDecisions, other.RemovedByDecisions);
                Assert.AreEqual(metrics[0].RemovedPossibilities, other.RemovedPossibilities);
            }
        }

        [Test]
        public void Znaczniki_WidoczneDlaProfilera()
        {
            CompiledRules rules = TestData.Compile("Prosty");
            foreach (ProfilerMarker marker in new[] { WfcMarkers.Generation, WfcMarkers.CellSelection, WfcMarkers.Propagation, WfcMarkers.Backtracking, WfcMarkers.MapCheck, WfcMarkers.MapShow, WfcMarkers.MeshCombine })
                Assert.AreNotEqual(IntPtr.Zero, marker.Handle);
            using (ProfilerRecorder recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "WFC.Propagacja"))
            {
                Assert.IsTrue(recorder.Valid, "Unity Profiler zna znacznik po nazwie");
                var solver = new BitsetSolver();
                solver.Initialize(rules, new int3(10, 6, 10), 1, new SolverSettings());
                solver.Run();
                Assert.Greater(recorder.CurrentValue, 0, "czas propagacji w tej klatce");
            }
        }

        [Test]
        public void Zaostrzenie_Zero_ToTeSameReguly()
        {
            CompiledRules plain = RuleCompiler.Compile(Tileset("Pelny").ToTilesetData());
            CompiledRules zero = Tightened(0, 7);
            Assert.AreEqual(plain.NeighborPairCount, zero.NeighborPairCount);
        }

        [Test]
        public void Zaostrzenie_UsuwaPodobnaCzesc_Symetrycznie_WyzszePoziomyToMniejPar()
        {
            CompiledRules plain = RuleCompiler.Compile(Tileset("Pelny").ToTilesetData());
            CompiledRules low = Tightened(0.01);
            CompiledRules high = Tightened(0.05);
            Assert.AreEqual(1 - 0.01, (double)low.NeighborPairCount / plain.NeighborPairCount, 0.005);
            Assert.AreEqual(1 - 0.05, (double)high.NeighborPairCount / plain.NeighborPairCount, 0.01);

            for (int state = 0; state < plain.StateCount; state++)
                foreach (Direction direction in DirectionExtensions.All)
                    for (int neighbor = 0; neighbor < plain.StateCount; neighbor++)
                    {
                        Assert.AreEqual(high.CanBeNeighbors(state, direction, neighbor), high.CanBeNeighbors(neighbor, direction.Opposite(), state), "zgodność działa w obie strony");
                        if (high.CanBeNeighbors(state, direction, neighbor))
                            Assert.IsTrue(low.CanBeNeighbors(state, direction, neighbor), "para z wyższego poziomu jest też na niższym");
                        if (low.CanBeNeighbors(state, direction, neighbor))
                            Assert.IsTrue(plain.CanBeNeighbors(state, direction, neighbor), "zaostrzenie tylko usuwa");
                    }
        }

        [Test]
        public void Zaostrzenie_TenSamNumerTeSamePary_InnyNumerInnePary()
        {
            CompiledRules first = Tightened(0.05, 1);
            CompiledRules again = Tightened(0.05, 1);
            CompiledRules other = Tightened(0.05, 2);
            bool differs = false;
            for (int state = 0; state < first.StateCount; state++)
                foreach (Direction direction in DirectionExtensions.All)
                {
                    CollectionAssert.AreEqual(first.GetNeighbors(state, direction).ToArray(), again.GetNeighbors(state, direction).ToArray());
                    differs |= !first.GetNeighbors(state, direction).SequenceEqual(other.GetNeighbors(state, direction));
                }
            Assert.IsTrue(differs);
        }

        [Test]
        public void Zaostrzenie_Mapa_BezNaruszen_TakzeWRegulachBazowych()
        {
            CompiledRules plain = RuleCompiler.Compile(Tileset("Pelny").ToTilesetData());
            CompiledRules tightened = Tightened(0.002, 1);
            var solver = new BitsetSolver();
            var size = new int3(12, 8, 12);
            solver.Initialize(tightened, size, 1, new SolverSettings { ContradictionHandler = new BacktrackHandler() });
            GenerationResult result = solver.Run();
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, RuleChecker.Check(tightened, size, result.States).Total);
            Assert.AreEqual(0, RuleChecker.Check(plain, size, result.States).Total, "zaostrzone reguły to podzbiór bazowych");
        }

        [Test]
        public void Restrykcyjnosc_Liczby_DlaZestawuPelnego()
        {
            RuleRestrictiveness full = RuleRestrictiveness.Of(RuleCompiler.Compile(Tileset("Pelny").ToTilesetData()));
            Assert.AreEqual(8.05, full.PairDensityPercent, 0.01);
            Assert.AreEqual(11.67, full.WeightedDensityPercent, 0.01);
            Assert.AreEqual(-3 * Math.Log(full.PairDensityPercent / 100) / Math.Log(170), full.Kappa, 1e-9);

            RuleRestrictiveness grass = RuleRestrictiveness.Of(RuleCompiler.Compile(Tileset("PelnyTrawa025").ToTilesetData()));
            Assert.AreEqual(full.PairDensityPercent, grass.PairDensityPercent, 1e-9, "wagi nie zmieniają par");
            Assert.Less(grass.WeightedDensityPercent, full.WeightedDensityPercent, "mniej trawy to ciaśniej w ujęciu ważonym");

            RuleRestrictiveness tightened = RuleRestrictiveness.Of(Tightened(0.1));
            Assert.Less(tightened.PairDensityPercent, full.PairDensityPercent);
            Assert.Greater(tightened.Kappa, full.Kappa);
        }

        [Test]
        public void Statystyka_Spearman_Mediana_Kwantyle()
        {
            Assert.AreEqual(1.0, Statistics.Spearman(new double[] { 1, 2, 3, 4 }, new double[] { 10, 20, 30, 400 }), 1e-9);
            Assert.AreEqual(-1.0, Statistics.Spearman(new double[] { 1, 2, 3, 4 }, new double[] { 9, 7, 5, 1 }), 1e-9);
            Assert.AreEqual(0.8, Statistics.Spearman(new double[] { 1, 2, 3, 4, 5 }, new double[] { 2, 1, 4, 3, 5 }), 1e-9);
            CollectionAssert.AreEqual(new[] { 1.0, 2.5, 2.5, 4.0 }, Statistics.Ranks(new double[] { 1, 5, 5, 9 }), "remis dostaje średnią rangę");
            Assert.IsNaN(Statistics.Spearman(new double[] { 1, 2 }, new double[] { 1, 2 }), "za mało par");
            Assert.IsNaN(Statistics.Spearman(new double[] { 3, 3, 3 }, new double[] { 1, 2, 3 }), "stała lista");
            Assert.AreEqual(2.5, Statistics.Median(new double[] { 4, 1, 2, 3 }), 1e-9);
            Assert.AreEqual(1.75, Statistics.Quantile(new double[] { 1, 2, 3, 4 }, 0.25), 1e-9);
            Assert.IsNaN(Statistics.Median(new double[0]));
        }

        [Test]
        public void Zabudowa_ToOdsetekParteruZBudynkami()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            var size = new int3(12, 8, 12);
            var solver = new BitsetSolver();
            solver.Initialize(rules, size, 1, new SolverSettings { ContradictionHandler = new BacktrackHandler() });
            GenerationResult result = solver.Run();
            StructureStats stats = StructureAnalyzer.Analyze(rules, size, result.States);
            int built = 0;
            for (int cell = 0; cell < 12 * 12; cell++)
                built += rules.GetTile(result.States[cell]).Group != CastleVocabulary.GroupTerrain ? 1 : 0;
            Assert.Greater(built, 0);
            Assert.AreEqual(100.0 * built / (12 * 12), stats.BuiltPercent, 1e-9);
        }

        [Test]
        public void Eksperyment_RysowanieIZaostrzenie_DajaKombinacje()
        {
            BenchmarkConfig config = SmallConfig();
            Assert.IsFalse(config.RequiresWindow);
            Assert.IsNull(config.Variants().Single().Display, "pusta lista = ustawienie z okna");
            Assert.IsNull(config.Variants().Single().Batching);

            config.Displays.AddRange(new[] { MapDisplay.GameObjects, MapDisplay.CombinedAtlas });
            config.Batchings.AddRange(new[] { RenderBatching.SrpBatcher, RenderBatching.InstancingOnly });
            config.TighteningPercents.AddRange(new[] { 0f, 5f });
            config.TighteningDraws = 3;
            List<BenchmarkVariant> variants = config.Variants();
            Assert.AreEqual(2 * 2 * (1 + 3), variants.Count);
            Assert.AreEqual(0, config.SkippedVariants);
            Assert.IsTrue(config.RequiresWindow);
            Assert.AreEqual(4, variants.Count(v => v.TighteningPercent == 0 && v.TighteningDraw == 0), "bez zaostrzenia losowanie nic nie zmienia");
            CollectionAssert.AreEquivalent(new uint[] { 1, 2, 3 }, variants.Where(v => v.TighteningPercent > 0 && v.Display == MapDisplay.GameObjects && v.Batching == RenderBatching.SrpBatcher).Select(v => v.TighteningDraw));
            StringAssert.Contains("zaostrzenie 5%", variants.Last().ToString());
            StringAssert.Contains("CombinedAtlas", variants.Last().ToString());
        }

        [TestCase("E7", 48, 30, 3, 120, true)]
        [TestCase("E8", 8, 100, 5, 10, false)]
        [TestCase("E9", 81, 20, 1, 10, false)]
        public void Eksperymenty_E7_E8_E9_Zasoby(string name, int variants, int seeds, int warmup, int frames, bool window)
        {
            var config = AssetDatabase.LoadAssetAtPath<BenchmarkConfig>(WfcPaths.Experiment(name));
            Assert.IsNotNull(config, "Brak zasobu. Uruchom WFC → Utwórz eksperymenty E1–E9.");
            Assert.AreEqual(variants, config.Variants().Count, config.Summary());
            Assert.AreEqual(seeds, config.SeedCount);
            Assert.AreEqual(warmup, config.WarmupRuns);
            Assert.AreEqual(frames, config.MeasuredFrames);
            Assert.AreEqual(window, config.RequiresWindow);
            Assert.AreEqual(0, config.TimeLimitSeconds, "krótkie eksperymenty bez limitu czasu");
        }

        [Test]
        public void Scena_Generator_MaWszystkieEksperymenty()
        {
            string scene = File.ReadAllText(TestData.ProjectPath(WfcPaths.GeneratorScene));
            foreach (string name in new[] { "E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8", "E9" })
                StringAssert.Contains(AssetDatabase.AssetPathToGUID(WfcPaths.Experiment(name)), scene, $"{name} nie ma w BenchmarkRunner w scenie Generator");
        }

        [Test]
        public void Seria_NoweKolumny_PlikWariantowIKorelacje()
        {
            string path = Path.Combine(folder, "E9.csv");
            BenchmarkConfig config = SmallConfig();
            config.TighteningPercents.AddRange(new[] { 0f, 2f, 10f });
            config.TighteningDraws = 2;
            config.MaxAttempts = 5;
            string correlation;
            using (var session = new BenchmarkSession(config, path, MachineInfo.Current()))
            {
                while (!session.IsFinished)
                    session.RunNext();
                correlation = session.DescribeCorrelation();
            }
            StringAssert.Contains("Korelacja Spearmana", correlation);

            List<Dictionary<string, string>> rows = ReadCsv(path);
            Assert.AreEqual(5 * 2, rows.Count, "5 wariantów × 2 ziarna");
            foreach (Dictionary<string, string> row in rows)
            {
                Assert.AreEqual(CsvWriter.Columns.Length, row.Count);
                Assert.Greater(Number(row["gestosc_regul_proc"]), 0);
                Assert.Greater(Number(row["pamiec_rzeczywista_mb"]), 0);
                Assert.Greater(Number(row["pamiec_unity_mb"]), 0);
                Assert.Greater(Number(row["pamiec_gc_mb"]), 0);
                Assert.AreEqual("", row["rysowanie"], "bez okna mapy nie ma");
                Assert.AreEqual("", row["najdluzsza_klatka_ms"]);
                Assert.AreEqual("", row["pamiec_siatek_mb"]);
            }
            CollectionAssert.AreEquivalent(new[] { "0", "2", "10" }, rows.Select(row => row["zaostrzenie_proc"]).Distinct());
            Assert.Greater(Number(rows[0]["usuniete_propagacja"]), Number(rows[0]["usuniete_decyzje"]));
            Assert.Greater(Number(rows[0]["gestosc_regul_proc"]), Number(rows.Last()["gestosc_regul_proc"]), "ostrzejsze reguły to mniejsza gęstość");

            List<Dictionary<string, string>> variants = ReadCsv(Path.Combine(folder, "E9_warianty.csv"));
            Assert.AreEqual(5, variants.Count);
            Assert.AreEqual("2", variants[0]["mapy"]);
            Assert.Greater(Number(variants[0]["czas_ms_mediana"]), 0);
            int attempts = rows.Take(2).Sum(row => int.Parse(row["proby"]));
            int successes = rows.Take(2).Sum(row => int.Parse(row["udane"]));
            Assert.AreEqual(100.0 * successes / attempts, Number(variants[0]["udane_proby_proc"]), 0.001);

            List<Dictionary<string, string>> correlations = ReadCsv(Path.Combine(folder, "E9_korelacje.csv"));
            CollectionAssert.AreEquivalent(new[] { "gestosc_regul_proc", "gestosc_wazona_proc", "kappa" }, correlations.Select(row => row["miara"]).Distinct());
            Assert.AreEqual(6, correlations.Count, "3 miary × wszystkie warianty i bez map zdegenerowanych");
            CollectionAssert.AreEquivalent(new[] { "wszystkie", "bez_map_zdegenerowanych" }, correlations.Select(row => row["warianty"]).Distinct());
        }

        [TestCase(0.0, true)]
        [TestCase(0.9, true)]
        [TestCase(1.0, false)]
        [TestCase(41.0, false)]
        [TestCase(90.0, false)]
        [TestCase(96.8, true)]
        public void MapaBezZamku_PustaAlboWCalosciZabudowana(double builtPercent, bool degenerate)
        {
            Assert.AreEqual(degenerate, BenchmarkSession.IsDegenerateMap(builtPercent));
        }

        [Test]
        public void Seria_JedenWariant_BezKorelacji()
        {
            string path = Path.Combine(folder, "jeden.csv");
            string correlation;
            using (var session = new BenchmarkSession(SmallConfig(), path, MachineInfo.Current()))
            {
                session.RunNext();
                correlation = session.DescribeCorrelation();
            }
            Assert.AreEqual(string.Empty, correlation);
            Assert.IsTrue(File.Exists(Path.Combine(folder, "jeden_warianty.csv")), "podsumowanie także po przerwanej serii");
            Assert.IsFalse(File.Exists(Path.Combine(folder, "jeden_korelacje.csv")));
        }

        [Test]
        public void PamiecRzeczywista_TegoSamegoRzeduCoSzacunek()
        {
            string path = Path.Combine(folder, "pamiec.csv");
            BenchmarkConfig config = SmallConfig();
            config.Tilesets.Clear();
            config.Tilesets.Add(Tileset("Pelny"));
            config.GridSizes.Clear();
            config.GridSizes.Add(new Vector3Int(20, 13, 20));
            config.SeedCount = 1;
            using (var session = new BenchmarkSession(config, path, MachineInfo.Current()))
                session.RunNext();
            Dictionary<string, string> row = ReadCsv(path).Single();
            double estimate = Number(row["pamiec_szczytowa_mb"]);
            double measured = Number(row["pamiec_rzeczywista_mb"]);
            Assert.Greater(measured, estimate * 0.5, $"zmierzone {measured} MB, szacunek {estimate} MB");
            Assert.Less(measured, estimate * 5, $"zmierzone {measured} MB, szacunek {estimate} MB");
        }

        [Test]
        public void Przelacznik_LaczenieRysowania_UstawiaIPrzywraca()
        {
            RenderBatching before = RenderBatchingSwitch.Current;
            foreach (RenderBatching batching in (RenderBatching[])Enum.GetValues(typeof(RenderBatching)))
            {
                RenderBatchingSwitch.Apply(batching);
                Assert.AreEqual(batching, RenderBatchingSwitch.Current);
            }
            RenderBatchingSwitch.RestoreProjectSetting();
            Assert.AreEqual(before, RenderBatchingSwitch.Current, "zasób URP wraca do ustawień projektu");
        }

        [Test]
        public void WszystkieEksperymenty_PoKolei_NajdluzszeNaKoncu_BladNieZatrzymuje()
        {
            var runner = new GameObject("Runner testowy").AddComponent<BenchmarkRunner>();
            created.Add(runner.gameObject);
            BenchmarkConfig withLimit = SmallConfig("ZLimitem");
            withLimit.TimeLimitSeconds = 60f;
            BenchmarkConfig broken = SmallConfig("Bledny");
            broken.Tilesets.Clear();
            BenchmarkConfig plain = SmallConfig("Zwykly");

            runner.StartAllSeries(new[] { withLimit, broken, plain }, folder);
            LogAssert.Expect(LogType.Exception, new Regex("nie ma wariantów"));
            Assert.AreEqual("Zwykly", runner.CurrentConfig.ExperimentName, "bez limitu czasu pierwszy; błędny pominięty");
            StringAssert.StartsWith("Eksperyment 2 z 3", runner.QueueLabel);
            runner.TogglePause();
            runner.TogglePause();

            for (int step = 0; step < 100 && runner.IsRunning; step++)
                runner.Advance();
            Assert.IsFalse(runner.IsRunning);
            Assert.IsTrue(File.Exists(Path.Combine(folder, "Zwykly.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "ZLimitem.csv")));
            Assert.AreEqual(2, ReadCsv(Path.Combine(folder, "ZLimitem.csv")).Count);
            StringAssert.Contains("Wszystkie eksperymenty zakończone", runner.Status);
            StringAssert.Contains("Bledny: błąd", runner.Status);
            StringAssert.Contains("Zwykly: Gotowe", runner.Status);
            Assert.AreEqual(string.Empty, runner.QueueLabel);
        }

        [Test]
        public void Stop_CzyscikolejkeEksperymentow()
        {
            var runner = new GameObject("Runner testowy").AddComponent<BenchmarkRunner>();
            created.Add(runner.gameObject);
            runner.StartAllSeries(new[] { SmallConfig("A"), SmallConfig("B") }, folder);
            runner.Stop();
            Assert.IsFalse(runner.IsRunning);
            Assert.AreEqual(string.Empty, runner.QueueLabel);
            Assert.IsFalse(File.Exists(Path.Combine(folder, "B.csv")));
        }

        [Test]
        public void EksperymentRysowania_BezOkna_OdmawiaZKodem2()
        {
            var runner = new GameObject("Runner testowy").AddComponent<BenchmarkRunner>();
            created.Add(runner.gameObject);
            BenchmarkConfig config = SmallConfig();
            config.Displays.Add(MapDisplay.CombinedAtlas);
            LogAssert.Expect(LogType.Error, new Regex("tylko w oknie"));
            Assert.AreEqual(2, runner.RunAllNow(config, Path.Combine(folder, "E7.csv")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "E7.csv")));
        }

        [Test]
        public void JednaMapa_ZmianaRysowania_TaSamaMapaBezGeneracji()
        {
            var root = new GameObject("Mapa testowa");
            created.Add(root);
            var builder = root.AddComponent<MapBuilder>();
            builder.MapRoot = root.transform;
            var controller = root.AddComponent<GeneratorController>();
            controller.Tileset = Tileset("Pelny");
            controller.MapBuilder = builder;
            controller.GridSize = new int3(8, 6, 8);
            controller.Contradiction = ContradictionKind.Backtrack;
            builder.Display = MapDisplay.GameObjects;
            controller.RedrawLastMap();
            Assert.AreEqual(0, builder.InstanceCount, "przed pierwszą mapą nic się nie dzieje");

            controller.Generate();
            int cubes = builder.InstanceCount;
            int[] states = controller.LastResult.States;
            Assert.IsNull(builder.Combined);

            builder.Display = MapDisplay.CombinedAtlas;
            controller.RedrawLastMap();
            Assert.AreSame(states, controller.LastResult.States, "bez nowej generacji");
            Assert.AreEqual(cubes, builder.InstanceCount);
            Assert.IsNotNull(builder.Combined);
            StringAssert.Contains("Po scaleniu", controller.LastReport);
            StringAssert.Contains("zabudowa parteru", controller.LastReport);

            builder.Display = MapDisplay.GameObjects;
            controller.RedrawLastMap();
            StringAssert.DoesNotContain("Po scaleniu", controller.LastReport);
            Assert.AreEqual(cubes, root.GetComponentsInChildren<MeshRenderer>().Length);
        }

        [Test]
        public void Okno_MaLaczenieIWszystkieEksperymenty()
        {
            var panelObject = new GameObject("Panel testowy");
            created.Add(panelObject);
            var runner = panelObject.AddComponent<BenchmarkRunner>();
            runner.Experiments.Add(AssetDatabase.LoadAssetAtPath<BenchmarkConfig>(WfcPaths.Experiment("E7")));
            runner.Experiments.Add(AssetDatabase.LoadAssetAtPath<BenchmarkConfig>(WfcPaths.Experiment("E9")));
            var panel = panelObject.AddComponent<GeneratorPanel>();
            panel.Runner = runner;
            var root = new VisualElement();
            panel.Build(root);

            Assert.AreEqual(3, root.Q<DropdownField>("laczenie").choices.Count, "3 ustawienia łączenia rysowania");
            List<string> experiments = root.Q<DropdownField>("eksperyment").choices;
            Assert.AreEqual(4, experiments.Count, "Własne, E7, E9, Wszystkie");
            Assert.AreEqual("Wszystkie po kolei", experiments.Last());

            root.Q<DropdownField>("eksperyment").value = experiments[1];
            root.Q<DropdownField>("eksperyment").index = 3;
        }
    }
}
