using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>StructureAnalyzer: liczenie budowli, wysokości i dostępności terenu.</summary>
    public sealed class StructureAnalyzerTests
    {
        static CompiledRules Full => TestData.Compile("Pelny");

        [Test]
        public void Wieza2x2_JednaWiezaWysokosci2()
        {
            StructureStats stats = TestMap.WithTower(Full).Analyze();
            Assert.AreEqual(1, stats.Towers);
            CollectionAssert.AreEqual(new[] { 2 }, stats.TowerHeights);
            Assert.AreEqual(0, stats.Keeps);
            Assert.AreEqual(0, stats.Bridges);
            Assert.AreEqual(0, stats.Stairs);
            Assert.AreEqual(12, stats.GroundTerrainCells, "16 komórek parteru minus 4 ćwiartki");
            Assert.AreEqual(100.0, stats.ReachableTerrainPercent);
            Assert.AreEqual(0, stats.EnclosedCourtyards);
        }

        [Test]
        public void PierscienMurow_JedenZamknietyDziedziniec()
        {
            // Mapa 5 × 1 × 5: pierścień 8 komórek muru wokół środka (2, 2); analizator nie wymaga poprawności reguł.
            var map = new TestMap(Full, new int3(5, 1, 5));
            for (int x = 1; x <= 3; x++)
                for (int z = 1; z <= 3; z++)
                    if (x != 2 || z != 2)
                        map.Set(x, 0, z, "Mur_Dol");
            StructureStats stats = map.Analyze();

            Assert.AreEqual(1, stats.EnclosedCourtyards);
            Assert.AreEqual(17, stats.GroundTerrainCells);
            Assert.AreEqual(16, stats.ReachableTerrainCells);
            Assert.AreEqual(8, stats.WallHeights.Count);
            Assert.IsTrue(stats.WallHeights.All(height => height == 1));
        }

        [Test]
        public void WygenerowanaMapa_LicznikiSpojne()
        {
            var size = new int3(20, 13, 20);
            var solver = new NaiveSolver();
            solver.Initialize(Full, size, 1, new SolverSettings());
            GenerationResult result = solver.Run();
            StructureStats stats = StructureAnalyzer.Analyze(Full, size, result.States);

            Assert.AreEqual(size.x * size.y * size.z, stats.TileCounts.Sum());
            Assert.AreEqual(stats.Towers, stats.TowerHeights.Count);
            Assert.AreEqual(stats.Keeps, stats.KeepHeights.Count);
            Assert.AreEqual(stats.Bridges, stats.BridgeLengths.Count);
            Assert.IsTrue(stats.TowerHeights.All(height => height >= 2 && height <= 13), string.Join(", ", stats.TowerHeights));
            Assert.IsTrue(stats.KeepHeights.All(height => height >= 3 && height <= 13), string.Join(", ", stats.KeepHeights));
            Assert.IsTrue(stats.BridgeLengths.All(length => length >= 3), string.Join(", ", stats.BridgeLengths));
        }
    }
}
