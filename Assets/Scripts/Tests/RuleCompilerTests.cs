using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>Obroty, sąsiedztwo, maski i wagi po kompilacji reguł.</summary>
    public sealed class RuleCompilerTests
    {
        static CompiledRules Full => TestData.Compile("Pelny");

        static string[] Names(CompiledRules rules, ReadOnlySpan<int> states)
        {
            var names = new string[states.Length];
            for (int i = 0; i < states.Length; i++)
                names[i] = rules.GetStateName(states[i]);
            return names;
        }

        [Test]
        public void Stany_KolejnoscKafelkowIObrotow()
        {
            Assert.AreEqual("Teren_Trawa@0", Full.GetStateName(0));
            Assert.AreEqual(new[] { "Schody@0", "Schody@90", "Schody@180", "Schody@270" },
                Enumerable.Range(5, 4).Select(Full.GetStateName).ToArray());
            Assert.AreEqual("Powietrze@0", Full.GetStateName(9));
            Assert.AreEqual("Most_Koniec@270", Full.GetStateName(169));
        }

        [Test]
        public void MurDol_Obrot90_Etykiety()
        {
            int state = Full.FindState("Mur_Dol", 1);
            Assert.AreEqual("mur_bok_parter", Full.GetLabel(state, Direction.E), "E = dawne S");
            Assert.AreEqual("mur_parter", Full.GetLabel(state, Direction.N), "N = dawne E");
            Assert.AreEqual("mur_bok_parter", Full.GetLabel(state, Direction.W), "W = dawne N");
            Assert.AreEqual("mur_parter_odbity", Full.GetLabel(state, Direction.S), "S = dawne W");
            Assert.AreEqual("kolumna_muru@90", Full.GetLabel(state, Direction.Up));
            Assert.AreEqual("podloze", Full.GetLabel(state, Direction.Down));
        }

        [Test]
        public void PowietrzeNadSchodami_Obrot180_EtykietaBezGwiazdkiNieZaleznaOdObrotu()
        {
            int state = Full.FindState("Powietrze_Nad_Schodami", 2);
            Assert.AreEqual("przed_drzwiami", Full.GetLabel(state, Direction.S));
            Assert.AreEqual("powietrze", Full.GetLabel(state, Direction.Up));
            Assert.AreEqual("nad_schodami@180", Full.GetLabel(state, Direction.Down));
        }

        [Test]
        public void MurSrodek_StoiTylkoNaMurzeWTymSamymObrocie()
        {
            int state = Full.FindState("Mur_Srodek", 1);
            CollectionAssert.AreEqual(
                new[] { "Mur_Dol@90", "Mur_Dol_Beczki@90", "Mur_Dol_Skrzynie@90", "Brama_Dol@90", "Brama_Dol_Pochodnie@90",
                        "Mur_Srodek@90", "Mur_Srodek_V2@90", "Mur_Srodek_V3@90" },
                Names(Full, Full.GetNeighbors(state, Direction.Down)));
        }

        [Test]
        public void WyjatekPionowy_NaPowietrzuStoiPowietrzeAlboMost()
        {
            int air = Full.FindState("Powietrze", 0);
            string[] above = Names(Full, Full.GetNeighbors(air, Direction.Up));
            Assert.AreEqual(13, above.Length, string.Join(", ", above));
            Assert.AreEqual("Powietrze@0", above[0]);
            Assert.IsTrue(above.Skip(1).All(name => name.StartsWith("Most_", StringComparison.Ordinal)), string.Join(", ", above));
            Assert.AreEqual(64, Full.GetNeighbors(air, Direction.Down).Length, "pod Powietrzem: każdy stan z górą powietrze albo puste");
        }

        [Test]
        public void DrzwiDonzonu_PrzedNimiPrzyczolekMostuAlboPowietrzeNadSchodami()
        {
            int door = Full.FindState("Donzon_Drzwi", 0);
            CollectionAssert.AreEqual(new[] { "Powietrze_Nad_Schodami@0", "Most_Poczatek@270", "Most_Koniec@90" },
                Names(Full, Full.GetNeighbors(door, Direction.S)));
        }

        [TestCase("Prosty", 316)]
        [TestCase("Sredni", 3834)]
        [TestCase("PelnyBezDekoracji", 10808)]
        [TestCase("Pelny", 13968)]
        public void LiczbaParSasiadow(string setName, int pairs)
        {
            Assert.AreEqual(pairs, TestData.Compile(setName).NeighborPairCount);
        }

        [TestCase("Prosty", 9, 10, 5, 9)]
        [TestCase("Sredni", 26, 27, 20, 29)]
        [TestCase("PelnyBezDekoracji", 35, 56, 28, 54)]
        [TestCase("Pelny", 50, 64, 39, 56)]
        public void MaskiKrawedziSiatki(string setName, int bottom, int top, int sideGround, int sideAbove)
        {
            CompiledRules rules = TestData.Compile(setName);
            Assert.AreEqual(bottom, BitMask.Count(rules.BottomMask), "dno siatki (dół podloze)");
            Assert.AreEqual(top, BitMask.Count(rules.TopMask), "góra siatki (góra powietrze albo puste)");
            foreach (Direction side in DirectionExtensions.Sides)
            {
                Assert.AreEqual(sideGround, BitMask.Count(rules.GetSideEdgeMask(side, true)), $"bok {side} na parterze (grunt)");
                Assert.AreEqual(sideAbove, BitMask.Count(rules.GetSideEdgeMask(side, false)), $"bok {side} wyżej (powietrze)");
            }
        }

        [Test]
        public void MaskiZgodneZListamiSasiadow()
        {
            foreach (string setName in TestData.SetNames)
            {
                CompiledRules rules = TestData.Compile(setName);
                for (int state = 0; state < rules.StateCount; state++)
                    foreach (Direction direction in DirectionExtensions.All)
                    {
                        ReadOnlySpan<int> list = rules.GetNeighbors(state, direction);
                        ReadOnlySpan<ulong> mask = rules.GetNeighborMask(state, direction);
                        Assert.AreEqual(list.Length, BitMask.Count(mask), $"{setName}: {rules.GetStateName(state)} {direction}");
                        foreach (int neighbor in list)
                            Assert.IsTrue(BitMask.Get(mask, neighbor));
                    }
            }
        }

        [Test]
        public void Wagi_KazdyObrotMaPelnaWageKafelka()
        {
            int grass = Full.FindState("Teren_Trawa", 0);
            Assert.AreEqual(10.0, Full.GetWeight(grass));
            Assert.AreEqual(10.0 * Math.Log(10.0), Full.GetWeightLogWeight(grass), 1e-12);
            for (int rotation = 0; rotation < 4; rotation++)
                Assert.AreEqual(0.3, Full.GetWeight(Full.FindState("Schody", rotation)));
        }

        [Test]
        public void MnoznikWag_DlaGrupyTeren()
        {
            TilesetData tileset = TestData.Rules.CreateTileset("Pelny");
            tileset.WeightMultipliers.Add(new WeightMultiplier("Teren", 0.4));
            CompiledRules rules = RuleCompiler.Compile(tileset);

            Assert.AreEqual(4.0, rules.GetWeight(rules.FindState("Teren_Trawa", 0)), 1e-12);
            Assert.AreEqual(1.2, rules.GetWeight(rules.FindState("Teren_Trawa_V2", 0)), 1e-12);
            Assert.AreEqual(0.12, rules.GetWeight(rules.FindState("Teren_Trawa_Studnia", 0)), 1e-12);
            Assert.AreEqual(1.0, rules.GetWeight(rules.FindState("Mur_Dol", 0)), "mur nie należy do grupy Teren");
        }

        [Test]
        public void Kompilacja_DwaRazyTenSamWynik()
        {
            CompiledRules first = RuleCompiler.Compile(TestData.Rules.CreateTileset("Pelny"));
            CompiledRules second = RuleCompiler.Compile(TestData.Rules.CreateTileset("Pelny"));
            RulesAssert.AreEqual(first, second);
        }
    }

    /// <summary>Porównanie dwóch skompilowanych zestawów stan po stanie.</summary>
    static class RulesAssert
    {
        public static void AreEqual(CompiledRules expected, CompiledRules actual)
        {
            Assert.AreEqual(expected.StateCount, actual.StateCount, "liczba stanów");
            for (int state = 0; state < expected.StateCount; state++)
            {
                string name = expected.GetStateName(state);
                Assert.AreEqual(name, actual.GetStateName(state), $"stan {state}");
                Assert.AreEqual(expected.GetWeight(state), actual.GetWeight(state), $"waga {name}");
                foreach (Direction direction in DirectionExtensions.All)
                {
                    Assert.AreEqual(expected.GetLabel(state, direction), actual.GetLabel(state, direction), $"etykieta {name} {direction}");
                    CollectionAssert.AreEqual(expected.GetNeighborMask(state, direction).ToArray(), actual.GetNeighborMask(state, direction).ToArray(), $"sąsiedzi {name} {direction}");
                }
            }
            CollectionAssert.AreEqual(expected.BottomMask.ToArray(), actual.BottomMask.ToArray(), "dno siatki");
            CollectionAssert.AreEqual(expected.TopMask.ToArray(), actual.TopMask.ToArray(), "góra siatki");
            foreach (Direction side in DirectionExtensions.Sides)
            {
                CollectionAssert.AreEqual(expected.GetSideEdgeMask(side, true).ToArray(), actual.GetSideEdgeMask(side, true).ToArray(), $"bok {side} na parterze");
                CollectionAssert.AreEqual(expected.GetSideEdgeMask(side, false).ToArray(), actual.GetSideEdgeMask(side, false).ToArray(), $"bok {side} wyżej");
            }
        }
    }
}
