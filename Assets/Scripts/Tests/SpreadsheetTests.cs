using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>
    /// Reguły po kompilacji porównane z arkuszem Data/Rules/Zasady_laczenia_WFC_zamek.xlsx:
    /// zakładka Stany (170 stanów z etykietami po obrocie), Zgodność (macierz 36 × 36 etykiet bocznych)
    /// i Sąsiedzi (200 wierszy: 50 kafelków w obrocie 0 × 4 boki).
    /// </summary>
    public sealed class SpreadsheetTests
    {
        static CompiledRules Full => TestData.Compile("Pelny");

        [Test]
        public void Stany_JakWZakladceStany()
        {
            // Kolumny: Nr, Kafelek, Obrót, E, N, W, S, Góra, Dół, Waga.
            List<string[]> rows = TestData.Sheet.Rows("Stany").Where(row => row.Length >= 10 && int.TryParse(row[0], out _)).ToList();
            Assert.AreEqual(170, rows.Count, "wiersze stanów w arkuszu");
            Assert.AreEqual(rows.Count, Full.StateCount);

            var labels = new[] { Direction.E, Direction.N, Direction.W, Direction.S, Direction.Up, Direction.Down };
            foreach (string[] row in rows)
            {
                int state = int.Parse(row[0]) - 1;
                string where = $"wiersz {row[0]} ({row[1]} {row[2]})";
                Assert.AreEqual(row[1], Full.GetTile(state).Name, where);
                Assert.AreEqual(TestData.SheetRotation(row[2]), Full.GetRotation(state), where);
                for (int column = 0; column < labels.Length; column++)
                    Assert.AreEqual(TestData.SheetLabel(row[3 + column]), Full.GetLabel(state, labels[column]), $"{where}, {labels[column].RulesKey()}");
                Assert.AreEqual(double.Parse(row[9], CultureInfo.InvariantCulture), Full.GetWeight(state), 1e-9, $"{where}, waga");
            }
        }

        [Test]
        public void Zgodnosc_JakWZakladceZgodnosc()
        {
            IReadOnlyList<string[]> rows = TestData.Sheet.Rows("Zgodność");
            int headerIndex = rows.ToList().FindIndex(row => row.Length > 0 && row[0] == "Etykieta");
            Assert.GreaterOrEqual(headerIndex, 0, "Brak wiersza nagłówka „Etykieta”.");
            string[] header = rows[headerIndex];
            string[] columns = header.Skip(1).Take(header.Length - 2).ToArray();
            Assert.AreEqual(36, columns.Length, "etykiety boczne w macierzy");
            CollectionAssert.AreEquivalent(TestData.Rules.SideLabels.Select(label => label.Name), columns);

            int cells = 0;
            foreach (string[] row in rows.Skip(headerIndex + 1).Where(row => row.Length > 0 && columns.Contains(row[0])))
            {
                int compatible = 0;
                for (int column = 0; column < columns.Length; column++)
                {
                    bool expected = column + 1 < row.Length && row[column + 1] == "✓";
                    Assert.AreEqual(expected, Full.AreSideLabelsCompatible(row[0], columns[column]), $"{row[0]} – {columns[column]}");
                    compatible += expected ? 1 : 0;
                    cells++;
                }
                Assert.AreEqual(int.Parse(row[header.Length - 1]), compatible, $"kolumna „Liczba pasujących” dla {row[0]}");
            }
            Assert.AreEqual(36 * 36, cells, "komórki macierzy");
        }

        [Test]
        public void Zgodnosc_DzialaWObieStrony()
        {
            foreach (SideLabelInfo a in TestData.Rules.SideLabels)
                foreach (SideLabelInfo b in TestData.Rules.SideLabels)
                    Assert.AreEqual(Full.AreSideLabelsCompatible(a.Name, b.Name), Full.AreSideLabelsCompatible(b.Name, a.Name), $"{a.Name} – {b.Name}");
        }

        [Test]
        public void Sasiedzi_JakWZakladceSasiedzi()
        {
            // Zakładka podaje sąsiadów kafelka w obrocie 0, którzy pasują bokiem i mogą stać na tym samym poziomie:
            // parter (dół „podloze”) albo wyżej. Kolumny: Kafelek, Strona, Etykieta, Może stać obok.
            var sides = new Dictionary<string, Direction> { ["E"] = Direction.E, ["N"] = Direction.N, ["W"] = Direction.W, ["S"] = Direction.S };
            List<string[]> rows = TestData.Sheet.Rows("Sąsiedzi").Where(row => row.Length >= 4 && sides.ContainsKey(row[1])).ToList();
            Assert.AreEqual(200, rows.Count, "wiersze w zakładce Sąsiedzi");

            foreach (string[] row in rows)
            {
                int state = Full.FindState(row[0], 0);
                Direction side = sides[row[1]];
                string where = $"{row[0]} strona {row[1]}";
                Assert.GreaterOrEqual(state, 0, where);
                Assert.AreEqual(row[2], Full.GetLabel(state, side), $"{where}, etykieta");

                bool groundFloor = BitMask.Get(Full.BottomMask, state);
                var actual = new SortedSet<string>(StringComparer.Ordinal);
                foreach (int neighbor in Full.GetNeighbors(state, side))
                    if (BitMask.Get(Full.BottomMask, neighbor) == groundFloor)
                        actual.Add(Full.GetStateName(neighbor));
                CollectionAssert.AreEqual(ParseNeighbors(row[3]), actual, where);
            }
        }

        /// <summary>„Teren_Trawa; Schody (0°, 180°, 270°)” → Teren_Trawa@0, Schody@0, Schody@180, Schody@270.</summary>
        static SortedSet<string> ParseNeighbors(string text)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string item in text.Split(';').Select(part => part.Trim()).Where(part => part.Length > 0))
            {
                Match match = Regex.Match(item, @"^(\w+)(?: \(([^)]*)\))?$");
                Assert.IsTrue(match.Success, $"Nie rozumiem wpisu „{item}”.");
                string[] angles = match.Groups[2].Success ? match.Groups[2].Value.Split(',') : new[] { "0°" };
                foreach (string angle in angles)
                    result.Add($"{match.Groups[1].Value}{RuleCompiler.RotationSeparator}{TestData.SheetRotation(angle) * 90}");
            }
            return result;
        }
    }
}
