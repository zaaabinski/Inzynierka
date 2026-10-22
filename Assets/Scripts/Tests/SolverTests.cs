using System;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>
    /// Kamień 6: SupportCountSolver, BitsetSolver i BurstSolver dają te same mapy co NaiveSolver (ten sam stan każdej
    /// komórki, te same próby i decyzje), tylko szybciej albo z inną pamięcią. Pełne kryterium (100 ziaren, około
    /// 8 minut) to menu WFC → Porównaj rozwiązywacze.
    /// </summary>
    public sealed class SolverTests
    {
        static readonly int3 Small = new int3(10, 8, 10);
        static readonly int3 Base = new int3(20, 13, 20);

        static readonly SolverKind[] FastSolvers = { SolverKind.SupportCount, SolverKind.Bitset, SolverKind.Burst };

        static GenerationResult Generate(SolverKind kind, string setName, int3 size, uint seed, SelectorKind selector = SelectorKind.LevelFirst, int maxAttempts = 1000)
        {
            IWfcSolver solver = WfcFactory.CreateSolver(kind);
            solver.Initialize(TestData.Compile(setName), size, seed, WfcFactory.CreateSettings(selector, ContradictionKind.Restart, maxAttempts));
            return solver.Run();
        }

        static void AssertSameMap(GenerationResult expected, GenerationResult actual, string context)
        {
            Assert.AreEqual(expected.Status, actual.Status, context);
            Assert.AreEqual(expected.Metrics.Attempts, actual.Metrics.Attempts, $"{context}: próby");
            Assert.AreEqual(expected.Metrics.Decisions, actual.Metrics.Decisions, $"{context}: decyzje");
            Assert.AreEqual(expected.Metrics.Contradictions, actual.Metrics.Contradictions, $"{context}: sprzeczności");
            int different = expected.States.Where((state, cell) => state != actual.States[cell]).Count();
            Assert.AreEqual(0, different, $"{context}: komórki z innym stanem");
        }

        [Test]
        public void Fabryka_TworzyCzteryRozwiazywacze()
        {
            CollectionAssert.AreEqual(new[] { "Naive", "SupportCount", "Bitset", "Burst" },
                ((SolverKind[])Enum.GetValues(typeof(SolverKind))).Select(kind => WfcFactory.CreateSolver(kind).Name).ToArray());
        }

        [Test]
        public void Burst_PropagacjaSkompilowanaPrzezBurst()
        {
            Assert.IsTrue(BurstSolver.IsBurstCompiled, "BurstSolver wykonuje zwykły C#: Burst jest wyłączony (Jobs → Burst → Enable Compilation).");
        }

        [Test]
        public void Maska_EntropiaILosowanieJakDlaListy()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            var picker = new DeterministicRandom(5);
            for (int sample = 0; sample < 200; sample++)
            {
                var mask = new ulong[rules.WordCount];
                for (int state = 0; state < rules.StateCount; state++)
                    if (picker.NextDouble() < (sample % 10 + 1) / 10.0)
                        BitMask.Set(mask, state);
                if (BitMask.Count(mask) == 0)
                    continue;
                int[] states = Enumerable.Range(0, rules.StateCount).Where(state => BitMask.Get(mask, state)).ToArray();

                Assert.AreEqual(WeightedChooser.Entropy(rules, states), WeightedChooser.Entropy(rules, mask), 0.0, "entropia co do bitu");
                var fromList = new DeterministicRandom((uint)sample + 1);
                var fromMask = new DeterministicRandom((uint)sample + 1);
                for (int draw = 0; draw < 5; draw++)
                    Assert.AreEqual(WeightedChooser.Choose(rules, states, ref fromList), WeightedChooser.Choose(rules, mask, ref fromMask));
            }
        }

        [Test]
        public void Rozwiazywacze_Siatka10x10x8_WszystkieHeurystyki_MapyJakNaive([ValueSource(nameof(FastSolvers))] SolverKind kind)
        {
            foreach (SelectorKind selector in (SelectorKind[])Enum.GetValues(typeof(SelectorKind)))
                for (uint seed = 1; seed <= 3; seed++)
                {
                    GenerationResult naive = Generate(SolverKind.Naive, "Pelny", Small, seed, selector);
                    AssertSameMap(naive, Generate(kind, "Pelny", Small, seed, selector), $"{kind}, {selector}, ziarno {seed}");
                }
        }

        [Test]
        public void Rozwiazywacze_InneZestawy_MapyJakNaive([ValueSource(nameof(FastSolvers))] SolverKind kind)
        {
            foreach (string setName in new[] { "Prosty", "Sredni", "PelnyBezDekoracji" })
                for (uint seed = 1; seed <= 3; seed++)
                {
                    GenerationResult naive = Generate(SolverKind.Naive, setName, Small, seed);
                    AssertSameMap(naive, Generate(kind, setName, Small, seed), $"{kind}, {setName}, ziarno {seed}");
                }
        }

        [Test]
        public void Rozwiazywacze_Siatka20x20x13_Ziarna1do3_MapyJakNaive_0Naruszen()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            for (uint seed = 1; seed <= 3; seed++)
            {
                GenerationResult naive = Generate(SolverKind.Naive, "Pelny", Base, seed);
                Assert.IsTrue(naive.Success);
                foreach (SolverKind kind in FastSolvers)
                {
                    GenerationResult result = Generate(kind, "Pelny", Base, seed);
                    AssertSameMap(naive, result, $"{kind}, ziarno {seed}");
                    Assert.AreEqual(0, RuleChecker.Check(rules, Base, result.States).Total);
                }
            }
        }

        [Test]
        public void Rozwiazywacze_KrokPoKroku_TakSamoJakRun([ValueSource(nameof(FastSolvers))] SolverKind kind)
        {
            IWfcSolver stepped = WfcFactory.CreateSolver(kind);
            stepped.Initialize(TestData.Compile("Pelny"), Small, 11, new SolverSettings());
            int decisions = 0;
            StepStatus status;
            while ((status = stepped.Step()) is StepStatus.Decided or StepStatus.Contradiction)
            {
                if (status == StepStatus.Decided)
                {
                    decisions++;
                    Assert.AreEqual(1, stepped.GetPossibilityCount(stepped.LastDecisionCell), "komórka decyzji ma 1 możliwość");
                    Assert.GreaterOrEqual(stepped.GetState(stepped.LastDecisionCell), 0);
                }
            }
            GenerationResult run = Generate(kind, "Pelny", Small, 11);
            CollectionAssert.AreEqual(run.States, stepped.Result.States);
            Assert.AreEqual(run.Metrics.Decisions, stepped.Metrics.Decisions);
        }

        [Test]
        public void Rozwiazywacze_UdanaPierwszaProba_UsuwaJakNaive([ValueSource(nameof(FastSolvers))] SolverKind kind)
        {
            CompiledRules rules = TestData.Compile("Pelny");
            for (uint seed = 1; seed <= 10; seed++)
            {
                GenerationResult result = Generate(kind, "Pelny", Small, seed);
                if (result.Metrics.Attempts != 1)
                    continue;
                Assert.AreEqual((long)Small.x * Small.y * Small.z * (rules.StateCount - 1), result.Metrics.RemovedPossibilities);
                Assert.Greater(result.Metrics.PropagationSteps, 0);
                return;
            }
            Assert.Inconclusive("Żadne z ziaren 1–10 nie dało mapy w pierwszej próbie.");
        }

        [Test]
        public void Rozwiazywacze_SprzecznoscZaKazdymRazem_KonczySiePoLimicieProb([ValueSource(nameof(FastSolvers))] SolverKind kind)
        {
            // Bez kafelka z dołem „podloze” parter nie ma żadnej możliwości: każda próba to sprzeczność.
            CompiledRules rules = RuleCompiler.Compile(TestData.Rules.CreateTileset("Bez parteru", new[] { "Powietrze", "Mur_Srodek" }));
            IWfcSolver solver = WfcFactory.CreateSolver(kind);
            solver.Initialize(rules, new int3(3, 3, 3), 1, new SolverSettings { MaxAttempts = 3 });
            GenerationResult result = solver.Run();

            Assert.AreEqual(GenerationStatus.Failed, result.Status);
            Assert.AreEqual(3, result.Metrics.Attempts);
            Assert.AreEqual(3, result.Metrics.Contradictions);
            Assert.AreEqual(2, result.Metrics.Restarts);
            Assert.GreaterOrEqual(result.LastContradictionCell, 0);
        }

        [Test]
        public void Pamiec_BitsetNajmniej_SupportCountNajwiecej()
        {
            long Memory(SolverKind kind)
            {
                IWfcSolver solver = WfcFactory.CreateSolver(kind);
                solver.Initialize(TestData.Compile("Pelny"), Base, 1, new SolverSettings());
                solver.Run();
                return solver.PeakMemoryBytes;
            }
            long naive = Memory(SolverKind.Naive);
            long support = Memory(SolverKind.SupportCount);
            long bitset = Memory(SolverKind.Bitset);
            // Liczniki wsparcia: 5200 komórek × 6 kierunków × 170 stanów × 2 bajty ≈ 10,6 MB.
            Assert.Greater(support, 5200L * 6 * 170 * 2);
            Assert.Greater(support, naive);
            Assert.Greater(naive, bitset);
            Assert.AreEqual(bitset, Memory(SolverKind.Burst), "Burst ma te same tablice co Bitset");
        }
    }
}
