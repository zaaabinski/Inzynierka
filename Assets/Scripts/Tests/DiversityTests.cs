using System;
using NUnit.Framework;
using Unity.Mathematics;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>Różnorodność map (DiversityAnalyzer): odsetek komórek z innym kafelkiem między dwiema mapami.</summary>
    public sealed class DiversityTests
    {
        static CompiledRules Rules => TestData.Compile("Pelny");

        [Test]
        public void TaSamaMapa_0Procent()
        {
            TestMap map = TestMap.WithTower(Rules);
            MapDifference difference = DiversityAnalyzer.Compare(Rules, map.Grid.Size, map.States, (int[])map.States.Clone());
            Assert.AreEqual(0, difference.AllPercent);
            Assert.AreEqual(0, difference.GroundPercent);
            Assert.AreEqual(0, difference.BuiltPercent);
            Assert.AreEqual(8, difference.BuiltCells, "4 ćwiartki × 2 kostki");
        }

        [Test]
        public void WiezaNaPustejMapie_ZabudowaW100Procentach()
        {
            // Mapa 4 × 3 × 4 = 48 komórek; wieża zajmuje 4 komórki parteru (z 16) i 4 nad nimi.
            var empty = new TestMap(Rules, new int3(4, 3, 4));
            TestMap tower = TestMap.WithTower(Rules);
            MapDifference difference = DiversityAnalyzer.Compare(Rules, empty.Grid.Size, empty.States, tower.States);
            Assert.AreEqual(100.0 * 8 / 48, difference.AllPercent, 1e-9);
            Assert.AreEqual(100.0 * 4 / 16, difference.GroundPercent, 1e-9);
            Assert.AreEqual(100.0, difference.BuiltPercent, 1e-9);
            Assert.AreEqual(8, difference.BuiltCells);

            MapDifference reverse = DiversityAnalyzer.Compare(Rules, empty.Grid.Size, tower.States, empty.States);
            Assert.AreEqual(difference.AllPercent, reverse.AllPercent, "kolejność map bez znaczenia");
            Assert.AreEqual(difference.BuiltPercent, reverse.BuiltPercent);
        }

        [Test]
        public void ObrotSieNieLiczy()
        {
            TestMap first = new TestMap(Rules, new int3(3, 2, 3)).Set(1, 0, 1, "Schody", 0);
            TestMap second = new TestMap(Rules, new int3(3, 2, 3)).Set(1, 0, 1, "Schody", 1);
            MapDifference difference = DiversityAnalyzer.Compare(Rules, first.Grid.Size, first.States, second.States);
            Assert.AreEqual(0, difference.AllPercent);
            Assert.AreEqual(1, difference.BuiltCells);
        }

        [Test]
        public void InnyRozmiarMapy_Wyjatek()
        {
            TestMap map = TestMap.WithTower(Rules);
            Assert.Throws<ArgumentException>(() => DiversityAnalyzer.Compare(Rules, new int3(5, 3, 4), map.States, map.States));
        }

        [Test]
        public void RozneZiarna_20x20x13_ZabudowaRozniSieBardziejNizCalaSiatka()
        {
            var size = new int3(20, 13, 20);
            int[] Generate(uint seed)
            {
                var solver = new BitsetSolver();
                solver.Initialize(Rules, size, seed, new SolverSettings());
                GenerationResult result = solver.Run();
                Assert.IsTrue(result.Success);
                return result.States;
            }

            int[] first = Generate(1);
            MapDifference same = DiversityAnalyzer.Compare(Rules, size, first, Generate(1));
            Assert.AreEqual(0, same.AllPercent, "to samo ziarno = ta sama mapa");

            MapDifference other = DiversityAnalyzer.Compare(Rules, size, first, Generate(2));
            Assert.Greater(other.AllPercent, 0);
            Assert.Greater(other.GroundPercent, other.AllPercent, "wyżej dominuje puste powietrze, wspólne dla obu map");
            Assert.Greater(other.BuiltPercent, other.GroundPercent, "zabudowa bez wspólnego tła");
            Assert.LessOrEqual(other.BuiltPercent, 100);
        }
    }
}
