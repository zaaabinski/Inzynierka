using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>Heurystyki wyboru komórki: Scanline, MRV, entropia, piętrami i losowo.</summary>
    public sealed class SelectorTests
    {
        static readonly GridShape Grid = new GridShape(new int3(2, 2, 2));
        static readonly int3 Small = new int3(10, 8, 10);

        static ICellSelector Create(string name)
        {
            switch (name)
            {
                case "LevelFirst": return new LevelFirstSelector();
                case "Scanline": return new ScanlineSelector();
                case "MinRemaining": return new MinRemainingSelector();
                case "Entropy": return new EntropySelector();
                case "Random": return new RandomSelector();
                default: throw new System.ArgumentException(name);
            }
        }

        static ICellSelector Prepared(string name)
        {
            ICellSelector selector = Create(name);
            var random = new DeterministicRandom(5);
            selector.Reset(Grid, ref random);
            return selector;
        }

        [Test]
        public void Scanline_KomorkiPoKolei_PomijaUstalone()
        {
            ICellSelector selector = Prepared("Scanline");
            for (int cell = 0; cell < Grid.CellCount; cell++)
                selector.OnCellChanged(cell, cell == 0 ? 1 : 3, 1.0);

            Assert.AreEqual(1, selector.SelectNext(), "komórka 0 jest już ustalona");
            selector.OnCellChanged(1, 1, 0);
            selector.OnCellChanged(2, 1, 0);
            Assert.AreEqual(3, selector.SelectNext());
        }

        [Test]
        public void MRV_NajmniejMozliwosciWCalejSiatce_BezPatrzeniaNaPietra()
        {
            ICellSelector selector = Prepared("MinRemaining");
            selector.OnCellChanged(Grid.Index(0, 0, 0), 5, 0.1);
            selector.OnCellChanged(Grid.Index(1, 1, 1), 2, 0.9);
            selector.OnCellChanged(Grid.Index(1, 0, 0), 3, 0.2);

            Assert.AreEqual(Grid.Index(1, 1, 1), selector.SelectNext(), "2 możliwości na piętrze 1 przed 3 i 5 na parterze");
            Assert.AreEqual(Grid.Index(1, 0, 0), selector.SelectNext());
            Assert.AreEqual(Grid.Index(0, 0, 0), selector.SelectNext());
            Assert.AreEqual(-1, selector.SelectNext());
        }

        [Test]
        public void Entropia_NajmniejszaWCalejSiatce_BezPatrzeniaNaPietra()
        {
            ICellSelector selector = Prepared("Entropy");
            selector.OnCellChanged(Grid.Index(0, 0, 0), 2, 0.8);
            selector.OnCellChanged(Grid.Index(1, 1, 1), 9, 0.3);

            Assert.AreEqual(Grid.Index(1, 1, 1), selector.SelectNext());
            Assert.AreEqual(Grid.Index(0, 0, 0), selector.SelectNext());
        }

        [Test]
        public void Pietrami_NajpierwParter_NawetPrzyWiekszejEntropii()
        {
            ICellSelector selector = Prepared("LevelFirst");
            selector.OnCellChanged(Grid.Index(0, 1, 0), 2, 0.1);
            selector.OnCellChanged(Grid.Index(1, 0, 1), 9, 2.0);

            Assert.AreEqual(Grid.Index(1, 0, 1), selector.SelectNext());
            Assert.AreEqual(Grid.Index(0, 1, 0), selector.SelectNext());
        }

        [Test]
        public void Scanline_KomorkaZNowuNieustalona_WracaDoNiej()
        {
            // Po cofnięciu decyzji albo resecie lokalnym komórka przed kursorem znowu ma kilka możliwości.
            ICellSelector selector = Prepared("Scanline");
            for (int cell = 0; cell < Grid.CellCount; cell++)
                selector.OnCellChanged(cell, 1, 0.0);
            selector.OnCellChanged(6, 2, 0.5);
            Assert.AreEqual(6, selector.SelectNext());
            selector.OnCellChanged(6, 1, 0.0);
            Assert.AreEqual(-1, selector.SelectNext());
            selector.OnCellChanged(2, 3, 1.0);
            Assert.AreEqual(2, selector.SelectNext());
        }

        [Test]
        public void Losowo_KolejnoscZLiczbLosowanychWReset_NieZEntropii()
        {
            ICellSelector selector = Prepared("Random");
            for (int cell = 0; cell < Grid.CellCount; cell++)
                selector.OnCellChanged(cell, 2 + cell, cell);
            int[] order = Enumerable.Range(0, Grid.CellCount).Select(_ => selector.SelectNext()).ToArray();
            CollectionAssert.AreEquivalent(Enumerable.Range(0, Grid.CellCount), order, "każda komórka raz");
            CollectionAssert.AreNotEqual(Enumerable.Range(0, Grid.CellCount), order, "nie po kolei");
            Assert.AreEqual(-1, selector.SelectNext());

            ICellSelector again = Prepared("Random");
            for (int cell = 0; cell < Grid.CellCount; cell++)
                again.OnCellChanged(cell, 9, 3.0);
            CollectionAssert.AreEqual(order, Enumerable.Range(0, Grid.CellCount).Select(_ => again.SelectNext()).ToArray(),
                "ta sama kolejność przy innych liczbach możliwości i entropiach");
        }

        [TestCase("LevelFirst")]
        [TestCase("Scanline")]
        [TestCase("MinRemaining")]
        [TestCase("Entropy")]
        [TestCase("Random")]
        public void Heurystyka_Siatka10x10x8_Ziarna1do3_UdaneBezNaruszen(string name)
        {
            CompiledRules rules = TestData.Compile("Pelny");
            for (uint seed = 1; seed <= 3; seed++)
            {
                var solver = new NaiveSolver();
                solver.Initialize(rules, Small, seed, new SolverSettings { Selector = Create(name) });
                GenerationResult result = solver.Run();
                Assert.IsTrue(result.Success, $"{name}, ziarno {seed}: {result.Status}, próby: {result.Metrics.Attempts}");
                Assert.AreEqual(name, result.SelectorName);
                RuleCheckReport check = RuleChecker.Check(rules, Small, result.States);
                Assert.AreEqual(0, check.Total, $"{name}, ziarno {seed}: {check}");
            }
        }

        [TestCase("Scanline")]
        [TestCase("MinRemaining")]
        [TestCase("Entropy")]
        [TestCase("Random")]
        public void Heurystyka_TenSamoZiarno_TaSamaMapa(string name)
        {
            CompiledRules rules = TestData.Compile("Pelny");
            int[] Run()
            {
                var solver = new NaiveSolver();
                solver.Initialize(rules, Small, 4, new SolverSettings { Selector = Create(name) });
                return solver.Run().States;
            }
            CollectionAssert.AreEqual(Run(), Run());
        }

        [Test]
        public void RozneHeurystyki_RozneMapyZTegoSamegoZiarna()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            int[][] maps = new[] { "LevelFirst", "Scanline", "MinRemaining", "Entropy", "Random" }.Select(name =>
            {
                var solver = new NaiveSolver();
                solver.Initialize(rules, Small, 1, new SolverSettings { Selector = Create(name) });
                return solver.Run().States;
            }).ToArray();
            for (int a = 0; a < maps.Length; a++)
                for (int b = a + 1; b < maps.Length; b++)
                    Assert.IsFalse(maps[a].SequenceEqual(maps[b]), $"heurystyki {a} i {b} dały identyczną mapę");
        }
    }
}
