using NUnit.Framework;
using Unity.Mathematics;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>RuleChecker na ręcznie ułożonych mapach: poprawna mapa ma 0 naruszeń, błędy są liczone według typu.</summary>
    public sealed class RuleCheckerTests
    {
        static CompiledRules Full => TestData.Compile("Pelny");

        [Test]
        public void Wieza2x2_PoprawnaMapa_0Naruszen()
        {
            RuleCheckReport report = TestMap.WithTower(Full).Check();
            Assert.AreEqual(0, report.Total, report.ToString());
        }

        [Test]
        public void SamaTrawaIPowietrze_0Naruszen()
        {
            RuleCheckReport report = new TestMap(Full, new int3(5, 4, 5)).Check();
            Assert.AreEqual(0, report.Total, report.ToString());
        }

        [Test]
        public void SchodyPodWiezaBezDrzwi_TrzyNaruszenia()
        {
            // Schody w obrocie 0 mają wyższy koniec od N, przy ćwiartce SW wieży; nad nimi zwykłe Powietrze.
            RuleCheckReport report = TestMap.WithTower(Full).Set(1, 0, 0, "Schody").Check();

            Assert.AreEqual(1, report[ViolationType.NeighborPair], report.ToString());
            Assert.AreEqual(1, report[ViolationType.StairsWithoutAirAbove], report.ToString());
            Assert.AreEqual(1, report[ViolationType.StairsWithoutDoorAbove], report.ToString());
            Assert.AreEqual(0, report[ViolationType.StairsNotAtTowerOrKeep], "wyższy koniec stoi przy wieży");
            Assert.AreEqual(3, report.Total, report.ToString());
        }

        [Test]
        public void SchodyDoNiczego_NieDoWiezyAniDonzonu()
        {
            RuleCheckReport report = new TestMap(Full, new int3(4, 3, 4))
                .Set(1, 0, 1, "Schody").Set(1, 1, 1, "Powietrze_Nad_Schodami").Check();

            Assert.AreEqual(1, report[ViolationType.StairsNotAtTowerOrKeep], report.ToString());
            Assert.AreEqual(1, report[ViolationType.StairsWithoutDoorAbove], report.ToString());
            Assert.AreEqual(0, report[ViolationType.StairsWithoutAirAbove], report.ToString());
        }

        [Test]
        public void WiezaZBrakujacaCwiartkaNaGorze_NierownaWieza()
        {
            RuleCheckReport report = TestMap.WithTower(Full).Set(2, 1, 2, "Powietrze").Check();
            Assert.AreEqual(1, report[ViolationType.UnevenTower], report.ToString());
            Assert.Greater(report[ViolationType.NeighborPair], 0, "podstawa NE ma nad sobą powietrze zamiast kolumny wieży");
        }

        [Test]
        public void PowietrzeNaDnie_ZlyKafelekNaDnieSiatki()
        {
            RuleCheckReport report = new TestMap(Full, new int3(3, 2, 3)).Set(1, 0, 1, "Powietrze").Check();
            Assert.AreEqual(1, report[ViolationType.GridBottom], report.ToString());
        }

        [Test]
        public void DrzwiDonzonuWPolu_BezMostuISchodow()
        {
            RuleCheckReport report = new TestMap(Full, new int3(3, 3, 3)).Set(1, 1, 1, "Donzon_Drzwi").Check();
            Assert.AreEqual(1, report[ViolationType.DoorWithoutBridgeOrStairs], report.ToString());
        }

        [Test]
        public void MostBezDrzwi_NiepelnyIbezPustegoPodSoba()
        {
            RuleCheckReport report = new TestMap(Full, new int3(3, 3, 3)).Set(1, 1, 1, "Most_Srodek").Check();
            Assert.AreEqual(1, report[ViolationType.IncompleteBridge], report.ToString());
            Assert.AreEqual(1, report[ViolationType.BridgeNotAboveAir], "pod mostem na piętrze 1 jest trawa");
        }

        [Test]
        public void KomorkaBezStanu_Liczona()
        {
            var map = new TestMap(Full, new int3(3, 2, 3));
            map.States[4] = -1;
            Assert.AreEqual(1, map.Check()[ViolationType.EmptyCell]);
        }

        [Test]
        public void WygenerowanaMapaZPodmienionaKomorka_Naruszenie()
        {
            var solver = new NaiveSolver();
            solver.Initialize(Full, new int3(8, 6, 8), 3, new SolverSettings());
            GenerationResult result = solver.Run();
            Assert.AreEqual(0, RuleChecker.Check(Full, result.Size, result.States).Total);

            var grid = new GridShape(result.Size);
            result.States[grid.Index(4, 2, 4)] = Full.FindState("Mur_Dol", 0);
            RuleCheckReport report = RuleChecker.Check(Full, result.Size, result.States);
            Assert.Greater(report[ViolationType.NeighborPair], 0, report.ToString());
        }
    }
}
