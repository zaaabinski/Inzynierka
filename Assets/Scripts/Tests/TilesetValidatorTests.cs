using System.Linq;
using NUnit.Framework;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>
    /// TilesetValidator: 4 zestawy z pliku przechodzą bez błędów, a zepsute reguły dają konkretne błędy.
    /// </summary>
    public sealed class TilesetValidatorTests
    {
        [TestCase("Prosty")]
        [TestCase("Sredni")]
        [TestCase("PelnyBezDekoracji")]
        [TestCase("Pelny")]
        public void Zestaw_BezBledow_KazdyStanMozeWystapic(string setName)
        {
            CompiledRules rules = TestData.Compile(setName);
            ValidationReport report = TilesetValidator.Validate(rules, TestData.Floors);
            Assert.IsTrue(report.IsValid, report.ToString());
            Assert.IsEmpty(report.NeverOccurringStates, report.ToString());
        }

        [Test]
        public void Pelny_JedyneOstrzezenie_BrakModeluDachSzczytChoragiew()
        {
            ValidationReport report = TilesetValidator.Validate(TestData.Compile("Pelny"), TestData.Floors);
            Assert.AreEqual(1, report.Warnings.Count, report.ToString());
            StringAssert.Contains("Dach_Szczyt_Choragiew", report.Warnings[0]);
        }

        [TestCase("Teren_Trawa", 0, 0)]
        [TestCase("Schody", 0, 0)]
        [TestCase("Powietrze", 1, 12)]
        [TestCase("Powietrze_Nad_Schodami", 1, 1)]
        [TestCase("Mur_Srodek", 1, 11)]
        [TestCase("Mur_Gora", 1, 12)]
        [TestCase("Wieza_Dach_Gora", 2, 12)]
        [TestCase("Donzon_Sciana", 1, 10)]
        [TestCase("Dach_Skos_Gora", 2, 12)]
        [TestCase("Most_Srodek", 2, 12)]
        public void Pelny_PietraNaKtorychStanMozeStanac(string tileName, int minFloor, int maxFloor)
        {
            CompiledRules rules = TestData.Compile("Pelny");
            ValidationReport report = TilesetValidator.Validate(rules, TestData.Floors);
            int state = rules.FindState(tileName, 0);
            Assert.AreEqual(minFloor, report.MinFloor[state], "najniższe piętro");
            Assert.AreEqual(maxFloor, report.MaxFloor[state], "najwyższe piętro");
        }

        [Test]
        public void Blad_DrzwiBezMostuIBezSchodow()
        {
            // Kafelki z drzwiami wchodzą do zestawu tylko razem z mostami albo schodami (rozdział 10).
            string[] tiles = TestData.Rules.FindSet("Prosty").TileNames.Concat(new[]
            {
                "Donzon_Sciana_Parter", "Donzon_Naroznik_Parter", "Donzon_Posadzka", "Donzon_Sciana", "Donzon_Drzwi",
                "Donzon_Naroznik", "Donzon_Wnetrze", "Dach_Skos", "Dach_Naroznik", "Dach_Skos_Gora", "Dach_Naroznik_Gora", "Dach_Szczyt"
            }).ToArray();
            ValidationReport report = TilesetValidator.Validate(RuleCompiler.Compile(TestData.Rules.CreateTileset("Bez mostów", tiles)), TestData.Floors);

            Assert.AreEqual(4, report.Errors.Count, report.ToString());
            Assert.IsTrue(report.Errors.All(error => error.Contains("Donzon_Drzwi@") && error.Contains("„drzwi”")), report.ToString());
        }

        [Test]
        public void Blad_EtykietaBezZadnegoSasiada()
        {
            TilesetData tileset = TestData.Rules.CreateTileset("Prosty");
            tileset.Tiles.Find(tile => tile.Name == "Mur_Dol").SetLabel(Direction.E, "nieznana");
            ValidationReport report = TilesetValidator.Validate(RuleCompiler.Compile(tileset), TestData.Floors);

            Assert.AreEqual(4, report.Errors.Count, report.ToString());
            Assert.IsTrue(report.Errors.All(error => error.Contains("Mur_Dol@") && error.Contains("„nieznana”")), report.ToString());
        }

        [Test]
        public void Blad_LiczbaStanowInnaNizWSekcjiKontrola()
        {
            TilesetData tileset = TestData.Rules.CreateTileset("Pelny");
            tileset.ExpectedStateCount = 171;
            ValidationReport report = TilesetValidator.Validate(RuleCompiler.Compile(tileset), TestData.Floors);

            Assert.AreEqual(1, report.Errors.Count, report.ToString());
            StringAssert.Contains("170 stanów, a sekcja kontrola podaje 171", report.Errors[0]);
        }

        [Test]
        public void Plik_EtykietaNiezadeklarowana()
        {
            RulesFile rules = TestData.ReadRules();
            rules.FindTile("Mur_Dol").SetLabel(Direction.E, "nieznana");
            ValidationReport report = TilesetValidator.ValidateRules(rules);

            Assert.IsTrue(report.Errors.Any(error => error.Contains("Mur_Dol") && error.Contains("nieznana") && error.Contains("etykietyBoczne")), report.ToString());
        }

        [Test]
        public void Plik_EtykietaDoSiebieBezParyZeSoba()
        {
            RulesFile rules = TestData.ReadRules();
            rules.CompatiblePairs.RemoveAll(pair => pair.A == "grunt" && pair.B == "grunt");
            ValidationReport report = TilesetValidator.ValidateRules(rules);

            Assert.IsTrue(report.Errors.Any(error => error.Contains("par zgodnych etykiet: 32, a sekcja kontrola podaje 33")), report.ToString());
            Assert.IsTrue(report.Errors.Any(error => error.Contains("grunt jest do_siebie")), report.ToString());
        }

        [Test]
        public void Plik_ParaOdbitaPasujacaDoSiebie()
        {
            RulesFile rules = TestData.ReadRules();
            rules.CompatiblePairs.Add(new LabelPair("mur_parter", "mur_parter"));
            ValidationReport report = TilesetValidator.ValidateRules(rules);

            Assert.IsTrue(report.Errors.Any(error => error.Contains("mur_parter jest para_odbita, a pasuje do samej siebie")), report.ToString());
        }

        [Test]
        public void Plik_ParaDodatkowaSpozaParZgodnych()
        {
            RulesFile rules = TestData.ReadRules();
            Assert.AreEqual(19, rules.ExtraPairs.Count, "paryDodatkowe w pliku");
            rules.ExtraPairs.Add(new LabelPair("grunt", "powietrze"));
            ValidationReport report = TilesetValidator.ValidateRules(rules);

            Assert.AreEqual(1, report.Errors.Count, report.ToString());
            StringAssert.Contains("grunt – powietrze", report.Errors[0]);
        }

        [Test]
        public void Plik_ZestawZNieznanymKafelkiem()
        {
            RulesFile rules = TestData.ReadRules();
            rules.FindSet("Prosty").TileNames.Add("Teren_Bruk");
            ValidationReport report = TilesetValidator.ValidateRules(rules);

            Assert.IsTrue(report.Errors.Any(error => error.Contains("Prosty") && error.Contains("Teren_Bruk")), report.ToString());
        }
    }
}
