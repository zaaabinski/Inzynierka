using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>Kamień 8: warianty reguł do E6 (RuleVariants) i liczenie budowli, gdy ściany budynków się stykają.</summary>
    public sealed class RuleVariantTests
    {
        static readonly int3 Small = new int3(10, 8, 10);
        static readonly int3 Base = new int3(20, 13, 20);

        static readonly string[] VariantNames = RuleVariants.All.Select(variant => variant.Name).ToArray();

        static GenerationResult Generate(string setName, int3 size, uint seed)
        {
            var solver = new BitsetSolver();
            solver.Initialize(TestData.Compile(setName), size, seed, new SolverSettings());
            return solver.Run();
        }

        /// <summary>Wieża 2 × 2 o wysokości 2 kostek (podstawa i blanki) z rogiem SW w (x, z).</summary>
        static TestMap AddTower(TestMap map, int x, int z)
        {
            int[,] quarters = { { 0, 0, 0 }, { 1, 0, 1 }, { 1, 1, 2 }, { 0, 1, 3 } };
            for (int i = 0; i < 4; i++)
            {
                map.Set(x + quarters[i, 0], 0, z + quarters[i, 1], "Wieza_Podstawa", quarters[i, 2]);
                map.Set(x + quarters[i, 0], 1, z + quarters[i, 1], "Wieza_Szczyt", quarters[i, 2]);
            }
            return map;
        }

        [Test]
        public void Warianty_CzteryZPelnego_TeSameKafelkiIStany()
        {
            CollectionAssert.AreEqual(new[] { "PelnyBezWolnegoPola", "PelnyBezDrzwiNadSchodami", "PelnyTrawa040", "PelnyTrawa025" }, VariantNames);
            foreach (RuleVariant variant in RuleVariants.All)
            {
                TilesetData data = TestData.Rules.CreateTileset(variant.Name);
                Assert.AreEqual(variant.Name, data.Name);
                Assert.AreEqual("Pelny", variant.BaseSet);
                Assert.AreEqual(50, data.Tiles.Count, variant.Name);
                Assert.AreEqual(170, RuleCompiler.Compile(data).StateCount, variant.Name);
                Assert.IsNotEmpty(variant.Description);
            }
        }

        [TestCaseSource(nameof(VariantNames))]
        public void Wariant_PrzechodziTilesetValidator(string setName)
        {
            ValidationReport report = TilesetValidator.Validate(TestData.Compile(setName), TestData.Floors);
            Assert.IsTrue(report.IsValid, report.ToString());
        }

        [Test]
        public void BezWolnegoPola_ScianyBudynkowMogaSieStykac()
        {
            CompiledRules full = TestData.Compile("Pelny");
            CompiledRules variant = TestData.Compile("PelnyBezWolnegoPola");
            string[] sides = { "mur_bok_parter", "mur_bok_wyzej", "wieza_sciana", "donzon_sciana" };
            foreach (string a in sides)
                foreach (string b in sides)
                {
                    Assert.IsFalse(full.AreSideLabelsCompatible(a, b), $"Pelny: {a} – {b}");
                    Assert.IsTrue(variant.AreSideLabelsCompatible(a, b), $"wariant: {a} – {b}");
                }
            Assert.IsFalse(variant.AreSideLabelsCompatible("okap", "wieza_sciana"), "okap dalej tylko przy powietrzu");
            Assert.Less(full.NeighborPairCount, variant.NeighborPairCount);
        }

        [Test]
        public void DwieWiezeObokSiebie_ToDwieWiezePoCzteryCwiartki()
        {
            // Wieże w x 1–2 i 3–4 stykają się zewnętrznymi ścianami: w Pelny to naruszenie pary, w wariancie nie.
            foreach (string setName in new[] { "Pelny", "PelnyBezWolnegoPola" })
            {
                TestMap map = AddTower(AddTower(new TestMap(TestData.Compile(setName), new int3(6, 3, 4)), 1, 1), 3, 1);
                RuleCheckReport check = map.Check();
                Assert.AreEqual(0, check[ViolationType.UnevenTower], $"{setName}: {check}");
                Assert.AreEqual(2, map.Analyze().Towers, setName);
                Assert.AreEqual(setName == "Pelny" ? 4 : 0, check[ViolationType.NeighborPair], $"{setName}: 2 piętra × 2 pary ścian");
            }
        }

        [Test]
        public void BezDrzwiNadSchodami_SchodyPodSlepaSciana_LiczaSieOsobno()
        {
            CompiledRules rules = TestData.Compile("PelnyBezDrzwiNadSchodami");
            CollectionAssert.AreEqual(new[] { ViolationType.StairsWithoutDoorAbove }, rules.DisabledChecks);
            int withoutDoor = 0;
            for (uint seed = 1; seed <= 5; seed++)
            {
                GenerationResult result = Generate("PelnyBezDrzwiNadSchodami", Base, seed);
                Assert.IsTrue(result.Success, $"ziarno {seed}");
                RuleCheckReport check = RuleChecker.Check(rules, Base, result.States);
                Assert.AreEqual(0, check.Total, $"ziarno {seed}: {check}");
                Assert.AreEqual(check[ViolationType.StairsWithoutDoorAbove], check.DisabledTotal);
                withoutDoor += check.DisabledTotal;
            }
            Assert.Greater(withoutDoor, 0, "w 5 mapach są schody pod ślepą ścianą");
            Assert.IsEmpty(TestData.Compile("Pelny").DisabledChecks);
        }

        [TestCase("PelnyTrawa040", 0.4)]
        [TestCase("PelnyTrawa025", 0.25)]
        public void WagiTrawy_MnoznikTylkoDlaPieciuKafelkowTrawy(string setName, double factor)
        {
            CompiledRules full = TestData.Compile("Pelny");
            CompiledRules variant = TestData.Compile(setName);
            int changed = 0;
            for (int state = 0; state < full.StateCount; state++)
            {
                bool grass = full.GetTile(state).Group == CastleVocabulary.GroupTerrain;
                Assert.AreEqual(full.GetWeight(state) * (grass ? factor : 1.0), variant.GetWeight(state), 1e-12, full.GetStateName(state));
                changed += grass ? 1 : 0;
            }
            Assert.AreEqual(5, changed, "Teren_Trawa, _V2, _Skladowisko, _Woz, _Studnia");
        }

        [TestCaseSource(nameof(VariantNames))]
        public void Wariant_Siatka10x10x8_Ziarna1do3_0Naruszen(string setName)
        {
            CompiledRules rules = TestData.Compile(setName);
            for (uint seed = 1; seed <= 3; seed++)
            {
                GenerationResult result = Generate(setName, Small, seed);
                Assert.IsTrue(result.Success, $"{setName}, ziarno {seed}: {result.Status}");
                Assert.AreEqual(0, RuleChecker.Check(rules, Small, result.States).Total, $"{setName}, ziarno {seed}");
            }
        }

        [Test]
        public void BezWolnegoPola_Siatka20x20x13_WiecejWiez_KazdaPoCzteryCwiartki()
        {
            CompiledRules rules = TestData.Compile("PelnyBezWolnegoPola");
            int towers = 0;
            int baseTowers = 0;
            for (uint seed = 1; seed <= 5; seed++)
            {
                GenerationResult result = Generate("PelnyBezWolnegoPola", Base, seed);
                Assert.IsTrue(result.Success, $"ziarno {seed}");
                RuleCheckReport check = RuleChecker.Check(rules, Base, result.States);
                Assert.AreEqual(0, check.Total, $"ziarno {seed}: {check}");
                towers += StructureAnalyzer.Analyze(rules, Base, result.States).Towers;
                GenerationResult full = Generate("Pelny", Base, seed);
                baseTowers += StructureAnalyzer.Analyze(TestData.Compile("Pelny"), Base, full.States).Towers;
            }
            // Ze 100 ziaren: 24,2 wieży na mapę zamiast 11,9.
            Assert.Greater(towers, baseTowers);
        }
    }
}
