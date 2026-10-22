using System;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>
    /// Kamień 7: cofanie decyzji (BacktrackHandler) i reset lokalny (LocalResetHandler). Naive (kopie list w dzienniku)
    /// oraz Bitset i Burst (maski w dzienniku, propagacja na wskaźnikach) to niezależne wykonania, więc te same mapy
    /// potwierdzają, że cofanie przywraca dokładnie stan sprzed decyzji.
    /// </summary>
    public sealed class ContradictionTests
    {
        static readonly int3 Small = new int3(10, 8, 10);
        static readonly int3 Base = new int3(20, 13, 20);

        static readonly SolverKind[] UndoSolvers = { SolverKind.Naive, SolverKind.Bitset, SolverKind.Burst };

        static GenerationResult Generate(SolverKind solver, ContradictionKind contradiction, int3 size, uint seed,
            SelectorKind selector = SelectorKind.LevelFirst, string setName = "Pelny")
        {
            IWfcSolver instance = WfcFactory.CreateSolver(solver);
            instance.Initialize(TestData.Compile(setName), size, seed, WfcFactory.CreateSettings(selector, contradiction, 1000));
            return instance.Run();
        }

        static void AssertSame(GenerationResult expected, GenerationResult actual, string context)
        {
            Assert.AreEqual(expected.Status, actual.Status, context);
            Assert.AreEqual(expected.Metrics.Attempts, actual.Metrics.Attempts, $"{context}: próby");
            Assert.AreEqual(expected.Metrics.Decisions, actual.Metrics.Decisions, $"{context}: decyzje");
            Assert.AreEqual(expected.Metrics.Contradictions, actual.Metrics.Contradictions, $"{context}: sprzeczności");
            Assert.AreEqual(expected.Metrics.Backtracks, actual.Metrics.Backtracks, $"{context}: cofnięcia");
            Assert.AreEqual(0, expected.States.Where((state, cell) => state != actual.States[cell]).Count(), $"{context}: komórki z innym stanem");
        }

        [Test]
        public void Fabryka_TrzyReakcjeIPiecHeurystyk()
        {
            CollectionAssert.AreEqual(new[] { "Restart", "Backtrack", "LocalReset" },
                ((ContradictionKind[])Enum.GetValues(typeof(ContradictionKind))).Select(kind => WfcFactory.CreateContradictionHandler(kind).Name).ToArray());
            CollectionAssert.AreEqual(new[] { "LevelFirst", "Scanline", "MinRemaining", "Entropy", "Random" },
                ((SelectorKind[])Enum.GetValues(typeof(SelectorKind))).Select(kind => WfcFactory.CreateSelector(kind).Name).ToArray());
        }

        [Test]
        public void Cofanie_DecyzjaReakcji()
        {
            var handler = new BacktrackHandler(maxBacktracks: 2);
            Assert.AreEqual(ContradictionAction.Backtrack, handler.OnContradiction(new ContradictionContext(1, 10, 5, 0, 0, 7)));
            Assert.AreEqual(ContradictionAction.Backtrack, handler.OnContradiction(new ContradictionContext(1, 10, 5, 1, 0, 7)));
            Assert.AreEqual(ContradictionAction.Restart, handler.OnContradiction(new ContradictionContext(1, 10, 5, 2, 0, 7)), "limit cofnięć w próbie");
            Assert.AreEqual(ContradictionAction.Restart, handler.OnContradiction(new ContradictionContext(1, 10, 0, 0, 0, 7)), "nie ma czego cofać");
            Assert.AreEqual(ContradictionAction.Abort, handler.OnContradiction(new ContradictionContext(10, 10, 0, 0, 0, 7)), "limit prób");
        }

        [Test]
        public void ResetLokalny_DecyzjaReakcji()
        {
            var handler = new LocalResetHandler(radius: 3, maxResets: 2);
            Assert.AreEqual(3, handler.ResetRadius);
            Assert.AreEqual(ContradictionAction.LocalReset, handler.OnContradiction(new ContradictionContext(1, 10, 0, 0, 1, 7)));
            Assert.AreEqual(ContradictionAction.Restart, handler.OnContradiction(new ContradictionContext(1, 10, 0, 0, 2, 7)), "limit resetów w próbie");
            Assert.AreEqual(ContradictionAction.Abort, handler.OnContradiction(new ContradictionContext(10, 10, 0, 0, 2, 7)), "limit prób");
        }

        [TestCase(ContradictionKind.Backtrack)]
        [TestCase(ContradictionKind.LocalReset)]
        public void NaiveBitsetBurst_TeSameMapy_WszystkieHeurystyki(ContradictionKind contradiction)
        {
            foreach (SelectorKind selector in (SelectorKind[])Enum.GetValues(typeof(SelectorKind)))
                for (uint seed = 1; seed <= 3; seed++)
                {
                    GenerationResult naive = Generate(SolverKind.Naive, contradiction, Small, seed, selector);
                    Assert.IsTrue(naive.Success, $"{contradiction}, {selector}, ziarno {seed}: {naive.Status}");
                    foreach (SolverKind solver in new[] { SolverKind.Bitset, SolverKind.Burst })
                        AssertSame(naive, Generate(solver, contradiction, Small, seed, selector), $"{solver}, {contradiction}, {selector}, ziarno {seed}");
                }
        }

        [TestCase(ContradictionKind.Backtrack)]
        [TestCase(ContradictionKind.LocalReset)]
        public void NaiveBitset_TeSameMapy_InneZestawy(ContradictionKind contradiction)
        {
            foreach (string setName in new[] { "Prosty", "Sredni", "PelnyBezDekoracji" })
                for (uint seed = 1; seed <= 3; seed++)
                    AssertSame(Generate(SolverKind.Naive, contradiction, Small, seed, setName: setName),
                        Generate(SolverKind.Bitset, contradiction, Small, seed, setName: setName), $"{contradiction}, {setName}, ziarno {seed}");
        }

        [Test]
        public void Cofanie_Siatka20x20x13_KazdaMapaWPierwszejProbie_0Naruszen()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            int backtracks = 0;
            for (uint seed = 1; seed <= 10; seed++)
            {
                GenerationResult result = Generate(SolverKind.Bitset, ContradictionKind.Backtrack, Base, seed);
                Assert.IsTrue(result.Success, $"ziarno {seed}: {result.Status}");
                Assert.AreEqual(1, result.Metrics.Attempts, $"ziarno {seed}: cofanie zamiast startu od nowa");
                Assert.AreEqual(result.Metrics.Contradictions, result.Metrics.Backtracks, $"ziarno {seed}: każda sprzeczność to jedno cofnięcie");
                Assert.AreEqual(0, RuleChecker.Check(rules, Base, result.States).Total, $"ziarno {seed}");
                backtracks += result.Metrics.Backtracks;
            }
            Assert.Greater(backtracks, 0, "w 10 ziarnach była co najmniej jedna sprzeczność");
        }

        [Test]
        public void ResetLokalny_Siatka20x20x13_0Naruszen()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            int resets = 0;
            for (uint seed = 1; seed <= 10; seed++)
            {
                GenerationResult result = Generate(SolverKind.Bitset, ContradictionKind.LocalReset, Base, seed);
                Assert.IsTrue(result.Success, $"ziarno {seed}: {result.Status}");
                Assert.AreEqual(0, RuleChecker.Check(rules, Base, result.States).Total, $"ziarno {seed}");
                resets += result.Metrics.Backtracks;
            }
            Assert.Greater(resets, 0, "w 10 ziarnach był co najmniej jeden reset");
        }

        [Test]
        public void Cofanie_KrokPoKroku_TakSamoJakRun([ValueSource(nameof(UndoSolvers))] SolverKind solver)
        {
            // Ziarno 8 z MRV na 10 × 10 × 8: 3 cofnięcia w trakcie kroków.
            IWfcSolver stepped = WfcFactory.CreateSolver(solver);
            stepped.Initialize(TestData.Compile("Pelny"), Small, 8, WfcFactory.CreateSettings(SelectorKind.MinRemaining, ContradictionKind.Backtrack, 1000));
            int contradictionSteps = 0;
            StepStatus status;
            while ((status = stepped.Step()) is StepStatus.Decided or StepStatus.Contradiction)
                contradictionSteps += status == StepStatus.Contradiction ? 1 : 0;
            GenerationResult run = Generate(solver, ContradictionKind.Backtrack, Small, 8, SelectorKind.MinRemaining);
            CollectionAssert.AreEqual(run.States, stepped.Result.States);
            Assert.AreEqual(run.Metrics.Backtracks, stepped.Metrics.Backtracks);
            Assert.AreEqual(3, stepped.Metrics.Backtracks);
            Assert.Greater(contradictionSteps, 0, "krok ze sprzecznością kończy się cofnięciem, a generacja trwa dalej");
        }

        [TestCase(ContradictionKind.Backtrack)]
        [TestCase(ContradictionKind.LocalReset)]
        public void SprzecznoscZaKazdymRazem_KonczySiePoLimicieProb(ContradictionKind contradiction)
        {
            // Bez kafelka z dołem „podloze” parter nie ma żadnej możliwości: cofanie nie ma czego cofnąć, reset nic nie zmienia.
            CompiledRules rules = RuleCompiler.Compile(TestData.Rules.CreateTileset("Bez parteru", new[] { "Powietrze", "Mur_Srodek" }));
            foreach (SolverKind solver in UndoSolvers)
            {
                IWfcSolver instance = WfcFactory.CreateSolver(solver);
                instance.Initialize(rules, new int3(3, 3, 3), 1, WfcFactory.CreateSettings(SelectorKind.LevelFirst, contradiction, 3));
                GenerationResult result = instance.Run();
                Assert.AreEqual(GenerationStatus.Failed, result.Status, $"{solver}");
                Assert.AreEqual(3, result.Metrics.Attempts, $"{solver}");
                Assert.AreEqual(2, result.Metrics.Restarts, $"{solver}");
            }
        }

        [TestCase(ContradictionKind.Backtrack)]
        [TestCase(ContradictionKind.LocalReset)]
        public void SupportCount_TylkoStartOdNowa(ContradictionKind contradiction)
        {
            Assert.IsFalse(WfcFactory.IsSupported(SolverKind.SupportCount, contradiction));
            Assert.IsTrue(WfcFactory.IsSupported(SolverKind.SupportCount, ContradictionKind.Restart));
            foreach (SolverKind solver in UndoSolvers)
                Assert.IsTrue(WfcFactory.IsSupported(solver, contradiction), $"{solver}");

            IWfcSolver instance = WfcFactory.CreateSolver(SolverKind.SupportCount);
            Assert.Throws<NotSupportedException>(() =>
                instance.Initialize(TestData.Compile("Pelny"), Small, 1, WfcFactory.CreateSettings(SelectorKind.LevelFirst, contradiction, 10)));

            var config = ScriptableObject.CreateInstance<BenchmarkConfig>();
            try
            {
                config.Tilesets.Add(ScriptableObject.CreateInstance<TilesetDefinition>());
                config.Solvers.Clear();
                config.Solvers.AddRange(new[] { SolverKind.SupportCount, SolverKind.Bitset });
                config.ContradictionHandlers.Clear();
                config.ContradictionHandlers.Add(contradiction);
                Assert.AreEqual(1, config.Variants().Count, "zostaje Bitset");
                Assert.AreEqual(1, config.SkippedVariants);
                StringAssert.Contains("Pominięte warianty", config.Summary());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config.Tilesets[0]);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Cofanie_DziennikZmianZwiekszaPamiec()
        {
            long Memory(ContradictionKind contradiction)
            {
                IWfcSolver solver = WfcFactory.CreateSolver(SolverKind.Bitset);
                solver.Initialize(TestData.Compile("Pelny"), Base, 1, WfcFactory.CreateSettings(SelectorKind.LevelFirst, contradiction, 1000));
                solver.Run();
                return solver.PeakMemoryBytes;
            }
            long restart = Memory(ContradictionKind.Restart);
            // Dziennik: co najmniej numer poziomu (4 bajty) i jeden wpis (4 + 3 × 8 bajtów) na komórkę.
            Assert.GreaterOrEqual(Memory(ContradictionKind.Backtrack) - restart, 5200L * (4 + 28));
            Assert.AreEqual(restart, Memory(ContradictionKind.LocalReset), "reset lokalny nie potrzebuje dziennika");
        }
    }
}
