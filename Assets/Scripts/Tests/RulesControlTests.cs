using NUnit.Framework;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>
    /// Liczby z sekcji kontrola pliku reguł. Testy „Zgodne” porównują wynik programu z sekcją kontrola i nie wymagają
    /// zmian, gdy reguły się zmienią. Testy „ZHandoffu” pilnują liczb opisanych w handoffie (50 kafelków,
    /// 170 stanów, 36 + 13 etykiet, 33 pary); po świadomej zmianie reguł trzeba je poprawić razem z opisem w pracy.
    /// </summary>
    public sealed class RulesControlTests
    {
        static ControlSection Control => TestData.Rules.Control;

        [Test]
        public void Zgodne_LiczbaKafelkow()
        {
            Assert.AreEqual(Control.Tiles, TestData.Rules.Tiles.Count);
        }

        [Test]
        public void Zgodne_LiczbaStanowWszystkichKafelkow()
        {
            Assert.AreEqual(Control.States, RuleCompiler.Compile(TestData.Rules.CreateTilesetWithAllTiles()).StateCount);
        }

        [Test]
        public void Zgodne_LiczbaEtykietIPar()
        {
            Assert.AreEqual(Control.SideLabels, TestData.Rules.SideLabels.Count, "etykiety boczne");
            Assert.AreEqual(Control.VerticalLabels, TestData.Rules.VerticalLabels.Count, "etykiety pionowe");
            Assert.AreEqual(Control.CompatiblePairs, TestData.Rules.CompatiblePairs.Count, "pary zgodnych etykiet");
        }

        [Test]
        public void Zgodne_KazdyZestaw()
        {
            Assert.AreEqual(TestData.Rules.Sets.Count, Control.Sets.Count, "liczba zestawów w sekcji kontrola");
            foreach (TileList set in TestData.Rules.Sets)
            {
                Assert.IsTrue(Control.Sets.TryGetValue(set.Name, out ControlCounts counts), $"Sekcja kontrola nie podaje zestawu {set.Name}.");
                CompiledRules rules = TestData.Compile(set.Name);
                Assert.AreEqual(counts.Tiles, rules.TileCount, $"kafelki zestawu {set.Name}");
                Assert.AreEqual(counts.States, rules.StateCount, $"stany zestawu {set.Name}");
            }
        }

        [Test]
        public void Zgodne_PlikRegulBezBledowIOstrzezen()
        {
            ValidationReport report = TilesetValidator.ValidateRules(TestData.Rules);
            Assert.IsEmpty(report.Errors, report.ToString());
            Assert.IsEmpty(report.Warnings, report.ToString());
        }

        [Test]
        public void ZHandoffu_50Kafelkow170Stanow36EtykietBocznych13Pionowych33Pary()
        {
            Assert.AreEqual(50, Control.Tiles, "kafelki");
            Assert.AreEqual(170, Control.States, "stany");
            Assert.AreEqual(36, Control.SideLabels, "etykiety boczne");
            Assert.AreEqual(13, Control.VerticalLabels, "etykiety pionowe");
            Assert.AreEqual(33, Control.CompatiblePairs, "pary zgodnych etykiet");
        }

        [TestCase("Prosty", 8, 26)]
        [TestCase("Sredni", 22, 79)]
        [TestCase("PelnyBezDekoracji", 41, 146)]
        [TestCase("Pelny", 50, 170)]
        public void ZHandoffu_Zestaw(string setName, int tiles, int states)
        {
            Assert.AreEqual(new ControlCounts(tiles, states), Control.Sets[setName], "sekcja kontrola");
            CompiledRules rules = TestData.Compile(setName);
            Assert.AreEqual(tiles, rules.TileCount, "kafelki po kompilacji");
            Assert.AreEqual(states, rules.StateCount, "stany po kompilacji");
        }

        [Test]
        public void ZHandoffu_Siatka20x20x13_Maska3Slowa()
        {
            GridSettings grid = TestData.Rules.Grid;
            Assert.AreEqual(20, grid.CellsEast);
            Assert.AreEqual(20, grid.CellsNorth);
            Assert.AreEqual(13, grid.Floors);
            Assert.AreEqual(3.0, grid.CellSize);
            Assert.AreEqual(3, TestData.Compile("Pelny").WordCount, "170 stanów mieści się w 3 liczbach ulong (192 bity)");
        }
    }
}
