using System;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>Rdzeń generacji: siatka, generator liczb losowych, entropia, kolejka wyboru i NaiveSolver.</summary>
    public sealed class GenerationTests
    {
        static readonly int3 Small = new int3(10, 8, 10);
        static readonly int3 Base = new int3(20, 13, 20);

        static GenerationResult Generate(string setName, int3 size, uint seed, SolverSettings settings = null)
        {
            var solver = new NaiveSolver();
            solver.Initialize(TestData.Compile(setName), size, seed, settings ?? new SolverSettings());
            return solver.Run();
        }

        [Test]
        public void Siatka_NumeracjaPietramiIWspolrzedne()
        {
            var grid = new GridShape(new int3(3, 2, 4));
            Assert.AreEqual(24, grid.CellCount);
            Assert.AreEqual(12, grid.CellsPerFloor);
            Assert.AreEqual(23, grid.Index(2, 1, 3));
            Assert.AreEqual(new int3(2, 1, 3), grid.Coordinates(23));
            Assert.AreEqual(1, grid.Floor(23));
            Assert.AreEqual(1, grid.Neighbor(0, Direction.E));
            Assert.AreEqual(3, grid.Neighbor(0, Direction.N));
            Assert.AreEqual(12, grid.Neighbor(0, Direction.Up));
            Assert.AreEqual(-1, grid.Neighbor(0, Direction.W));
            Assert.AreEqual(-1, grid.Neighbor(0, Direction.S));
            Assert.AreEqual(-1, grid.Neighbor(0, Direction.Down));
        }

        [Test]
        public void Losowanie_TeSameZiarnoIProbaTenSamCiag()
        {
            DeterministicRandom a = DeterministicRandom.ForAttempt(7, 0);
            DeterministicRandom b = DeterministicRandom.ForAttempt(7, 0);
            DeterministicRandom c = DeterministicRandom.ForAttempt(7, 1);
            double[] first = Enumerable.Range(0, 10).Select(_ => a.NextDouble()).ToArray();
            CollectionAssert.AreEqual(first, Enumerable.Range(0, 10).Select(_ => b.NextDouble()).ToArray());
            CollectionAssert.AreNotEqual(first, Enumerable.Range(0, 10).Select(_ => c.NextDouble()).ToArray());
            Assert.IsTrue(first.All(value => value >= 0 && value < 1));
        }

        [Test]
        public void Entropia_JednaMozliwosc0_DwieRowneLn2()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            int stairs0 = rules.FindState("Schody", 0);
            int stairs1 = rules.FindState("Schody", 1);
            Assert.AreEqual(0.0, WeightedChooser.Entropy(rules, new[] { stairs0 }));
            Assert.AreEqual(Math.Log(2), WeightedChooser.Entropy(rules, new[] { stairs0, stairs1 }), 1e-12);
        }

        [Test]
        public void Losowanie_ProporcjonalneDoWag()
        {
            // Teren_Trawa (waga 10) i Teren_Trawa_V2 (waga 3): oczekiwany udział 10 / 13 = 76,9%.
            CompiledRules rules = TestData.Compile("Pelny");
            int[] states = { rules.FindState("Teren_Trawa", 0), rules.FindState("Teren_Trawa_V2", 0) };
            var random = new DeterministicRandom(1);
            int first = 0;
            const int draws = 20000;
            for (int i = 0; i < draws; i++)
                first += WeightedChooser.Choose(rules, states, ref random) == states[0] ? 1 : 0;
            Assert.AreEqual(10.0 / 13.0, (double)first / draws, 0.01);
        }

        [Test]
        public void KolejnoscPietrami_NajpierwNizszePietro_StareWpisyPomijane()
        {
            var grid = new GridShape(new int3(2, 2, 2));
            var selector = new LevelFirstSelector();
            var random = new DeterministicRandom(3);
            selector.Reset(grid, ref random);
            selector.OnCellChanged(grid.Index(0, 1, 0), 2, 0.1);
            selector.OnCellChanged(grid.Index(1, 0, 1), 5, 2.0);
            selector.OnCellChanged(grid.Index(0, 0, 0), 5, 1.0);
            selector.OnCellChanged(grid.Index(0, 0, 0), 1, 0.0);

            Assert.AreEqual(grid.Index(1, 0, 1), selector.SelectNext(), "parter przed piętrem 1, komórka (0, 0, 0) ma już 1 możliwość");
            Assert.AreEqual(grid.Index(0, 1, 0), selector.SelectNext());
            Assert.AreEqual(-1, selector.SelectNext());
        }

        [Test]
        public void Naive_TenSamoZiarno_TaSamaMapa()
        {
            GenerationResult first = Generate("Pelny", Small, 7);
            GenerationResult second = Generate("Pelny", Small, 7);
            Assert.IsTrue(first.Success);
            CollectionAssert.AreEqual(first.States, second.States);
            Assert.AreEqual(first.Metrics.Attempts, second.Metrics.Attempts);
            Assert.AreEqual(first.Metrics.Decisions, second.Metrics.Decisions);
            Assert.AreEqual(first.Metrics.RemovedPossibilities, second.Metrics.RemovedPossibilities);
        }

        [Test]
        public void Naive_RozneZiarna_RozneMapy()
        {
            int[] first = Generate("Pelny", Small, 1).States;
            int[] second = Generate("Pelny", Small, 2).States;
            Assert.Greater(first.Where((state, cell) => state != second[cell]).Count(), 0);
        }

        [Test]
        public void Naive_KrokPoKroku_TakSamoJakRun()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            var stepped = new NaiveSolver();
            stepped.Initialize(rules, Small, 11, new SolverSettings());
            int steps = 0;
            while (stepped.Step() is StepStatus.Decided or StepStatus.Contradiction)
                steps++;
            GenerationResult run = Generate("Pelny", Small, 11);

            CollectionAssert.AreEqual(run.States, stepped.Result.States);
            Assert.AreEqual(run.Metrics.Decisions, stepped.Metrics.Decisions);
            Assert.AreEqual(stepped.Metrics.Decisions, steps, "każdy krok to jedna decyzja");
        }

        [TestCase("Prosty")]
        [TestCase("Sredni")]
        [TestCase("PelnyBezDekoracji")]
        [TestCase("Pelny")]
        public void Naive_Siatka10x10x8_Ziarna1do3_0Naruszen(string setName)
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                GenerationResult result = Generate(setName, Small, seed);
                Assert.IsTrue(result.Success, $"{setName}, ziarno {seed}: {result.Status}, próby: {result.Metrics.Attempts}");
                RuleCheckReport check = RuleChecker.Check(TestData.Compile(setName), Small, result.States);
                Assert.AreEqual(0, check.Total, $"{setName}, ziarno {seed}: {check}");
            }
        }

        [Test]
        public void Naive_Siatka20x20x13_Pelny_Ziarna1i2_0Naruszen()
        {
            for (uint seed = 1; seed <= 2; seed++)
            {
                GenerationResult result = Generate("Pelny", Base, seed);
                Assert.IsTrue(result.Success, $"ziarno {seed}: {result.Status}");
                RuleCheckReport check = RuleChecker.Check(TestData.Compile("Pelny"), Base, result.States);
                Assert.AreEqual(0, check.Total, $"ziarno {seed}: {check}");
            }
        }

        [Test]
        public void Naive_UdanaPierwszaProba_UsuwaKazdejKomorceWszystkoPozaJednymStanem()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            for (uint seed = 1; seed <= 10; seed++)
            {
                GenerationResult result = Generate("Pelny", Small, seed);
                if (result.Metrics.Attempts != 1)
                    continue;
                Assert.AreEqual((long)result.Grid.CellCount * (rules.StateCount - 1), result.Metrics.RemovedPossibilities);
                Assert.Greater(result.Metrics.Decisions, 0);
                Assert.Greater(result.Metrics.PropagationSteps, 0);
                return;
            }
            Assert.Inconclusive("Żadne z ziaren 1–10 nie dało mapy w pierwszej próbie.");
        }

        [Test]
        public void Naive_SprzecznoscZaKazdymRazem_KonczySiePoLimicieProb()
        {
            // Bez kafelka z dołem „podloze” parter nie ma żadnej możliwości: każda próba to sprzeczność.
            CompiledRules rules = RuleCompiler.Compile(TestData.Rules.CreateTileset("Bez parteru", new[] { "Powietrze", "Mur_Srodek" }));
            var solver = new NaiveSolver();
            solver.Initialize(rules, new int3(3, 3, 3), 1, new SolverSettings { MaxAttempts = 3 });
            GenerationResult result = solver.Run();

            Assert.AreEqual(GenerationStatus.Failed, result.Status);
            Assert.AreEqual(3, result.Metrics.Attempts);
            Assert.AreEqual(3, result.Metrics.Contradictions);
            Assert.AreEqual(2, result.Metrics.Restarts);
        }

        [Test]
        public void Naive_LimitCzasu_PrzerywaGeneracje()
        {
            var solver = new NaiveSolver();
            solver.Initialize(TestData.Compile("Pelny"), Small, 1, new SolverSettings { TimeLimitSeconds = 1e-9 });
            Assert.AreEqual(StepStatus.Failed, solver.Step());
            Assert.AreEqual(GenerationStatus.TimedOut, solver.Result.Status);
        }
    }
}
