using System;
using System.Collections.Generic;
using System.Text;

namespace WFC.Core
{
    /// <summary>Wynik sprawdzenia: błąd wyklucza zestaw z pomiarów, ostrzeżenie nie.</summary>
    public sealed class ValidationReport
    {
        public ValidationReport(string subject)
        {
            Subject = subject;
        }

        /// <summary>Co sprawdzano: nazwa zestawu albo pliku reguł.</summary>
        public string Subject { get; }

        public List<string> Errors { get; } = new List<string>();

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Stany, które nie mogą stanąć na żadnym piętrze siatki.</summary>
        public List<int> NeverOccurringStates { get; } = new List<int>();

        /// <summary>Najniższe piętro, na którym stan może stanąć, albo −1 (tylko po sprawdzeniu zestawu).</summary>
        public int[] MinFloor { get; internal set; } = Array.Empty<int>();

        /// <summary>Najwyższe piętro, na którym stan może stanąć, albo −1 (tylko po sprawdzeniu zestawu).</summary>
        public int[] MaxFloor { get; internal set; } = Array.Empty<int>();

        public bool IsValid => Errors.Count == 0;

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append($"{Subject}: {Errors.Count} błędów, {Warnings.Count} ostrzeżeń");
            if (MinFloor.Length > 0)
                text.Append($", stanów, które nigdy nie wystąpią: {NeverOccurringStates.Count}");
            foreach (string error in Errors)
                text.Append("\n  błąd: ").Append(error);
            foreach (string warning in Warnings)
                text.Append("\n  ostrzeżenie: ").Append(warning);
            return text.ToString();
        }
    }

    /// <summary>
    /// Sprawdza reguły przed generacją: każda ściana ma możliwego sąsiada (albo może stać przy krawędzi siatki),
    /// zgodność działa w obie strony, liczby zgadzają się z sekcją kontrola, a stany, które nigdy nie wystąpią,
    /// są wypisane.
    /// </summary>
    public static class TilesetValidator
    {
        /// <summary>Sprawdza skompilowany zestaw dla siatki o podanej liczbie pięter.</summary>
        public static ValidationReport Validate(CompiledRules rules, int floors)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            if (floors < 1)
                throw new ArgumentOutOfRangeException(nameof(floors), floors, "Siatka musi mieć co najmniej 1 piętro.");

            var report = new ValidationReport($"Zestaw {rules.TilesetName} ({rules.StateCount} stanów)");

            if (rules.ExpectedTileCount > 0 && rules.TileCount != rules.ExpectedTileCount)
                report.Errors.Add($"{rules.TileCount} kafelków, a sekcja kontrola podaje {rules.ExpectedTileCount}.");
            if (rules.ExpectedStateCount > 0 && rules.StateCount != rules.ExpectedStateCount)
                report.Errors.Add($"{rules.StateCount} stanów, a sekcja kontrola podaje {rules.ExpectedStateCount}.");

            foreach (TileData tile in rules.Tiles)
            {
                if (!(tile.Weight > 0))
                    report.Errors.Add($"Kafelek {tile.Name} ma wagę {tile.Weight}; waga musi być większa od 0.");
                if (tile.HasModel && !tile.ModelAvailable)
                    report.Warnings.Add($"Kafelek {tile.Name} nie ma jeszcze modelu {tile.Model} (modelDostepny = false).");
            }

            for (int state = 0; state < rules.StateCount; state++)
                foreach (Direction direction in DirectionExtensions.All)
                {
                    if (rules.GetNeighbors(state, direction).Length > 0 || CanTouchGridEdge(rules, state, direction))
                        continue;
                    report.Errors.Add($"Stan {rules.GetStateName(state)}: ściana {direction.RulesKey()} " +
                                      $"(„{rules.GetLabel(state, direction)}”) nie ma żadnego sąsiada i nie może stać przy krawędzi siatki.");
                }

            int asymmetric = 0;
            for (int state = 0; state < rules.StateCount; state++)
                foreach (Direction direction in DirectionExtensions.All)
                    foreach (int neighbor in rules.GetNeighbors(state, direction))
                        if (!rules.CanBeNeighbors(neighbor, direction.Opposite(), state) && asymmetric++ < 10)
                            report.Errors.Add($"Zgodność nie działa w obie strony: {rules.GetStateName(neighbor)} może stać " +
                                              $"w kierunku {direction.RulesKey()} od {rules.GetStateName(state)}, ale nie odwrotnie.");
            if (asymmetric > 10)
                report.Errors.Add($"Razem {asymmetric} niesymetrycznych par sąsiadów.");

            FindFloors(rules, floors, report);
            foreach (int state in report.NeverOccurringStates)
                report.Warnings.Add($"Stan {rules.GetStateName(state)} nie może stanąć na żadnym z {floors} pięter siatki.");

            return report;
        }

        /// <summary>
        /// Sprawdza cały plik reguł: deklaracje etykiet, rodzaje etykiet, pary, zestawy i liczby z sekcji kontrola.
        /// Zestawy po kompilacji sprawdza Validate.
        /// </summary>
        public static ValidationReport ValidateRules(RulesFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            var report = new ValidationReport($"Plik reguł {file.Version}");
            ControlSection control = file.Control;

            CheckCount(report, "kafelków", file.Tiles.Count, control.Tiles);
            CheckCount(report, "etykiet bocznych", file.SideLabels.Count, control.SideLabels);
            CheckCount(report, "etykiet pionowych", file.VerticalLabels.Count, control.VerticalLabels);
            CheckCount(report, "par zgodnych etykiet", file.CompatiblePairs.Count, control.CompatiblePairs);

            var sideKinds = new Dictionary<string, SideLabelKind>();
            foreach (SideLabelInfo label in file.SideLabels)
                if (!sideKinds.ContainsKey(label.Name))
                    sideKinds.Add(label.Name, label.Kind);
                else
                    report.Errors.Add($"Etykieta boczna {label.Name} jest zadeklarowana dwa razy.");
            var verticalLabels = new Dictionary<string, VerticalLabelInfo>();
            foreach (VerticalLabelInfo label in file.VerticalLabels)
                if (!verticalLabels.ContainsKey(label.Name))
                    verticalLabels.Add(label.Name, label);
                else
                    report.Errors.Add($"Etykieta pionowa {label.Name} jest zadeklarowana dwa razy.");

            foreach (VerticalLabelInfo label in file.VerticalLabels)
            {
                bool starred = label.Name.EndsWith(RuleCompiler.RotationDependentMark.ToString(), StringComparison.Ordinal);
                if (starred != label.SameRotation)
                    report.Errors.Add($"Etykieta pionowa {label.Name}: gwiazdka i tenSamObrot = {label.SameRotation} się nie zgadzają.");
            }

            var usedSide = new HashSet<string> { file.MapEdge.SideAtGround, file.MapEdge.SideAbove };
            var usedVertical = new HashSet<string> { file.MapEdge.GridBottom };
            usedVertical.UnionWith(file.MapEdge.GridTopAllowed);
            foreach (VerticalException exception in file.VerticalExceptions)
            {
                usedVertical.Add(exception.Below);
                usedVertical.Add(exception.Above);
            }
            foreach (string label in usedSide)
                if (!sideKinds.ContainsKey(label))
                    report.Errors.Add($"Krawędź mapy używa etykiety bocznej {label}, której nie ma w etykietyBoczne.");
            foreach (string label in usedVertical)
                if (!verticalLabels.ContainsKey(label))
                    report.Errors.Add($"Etykieta pionowa {label} z krawędzi mapy albo wyjątków nie jest zadeklarowana w etykietyPionowe.");

            var tileNames = new HashSet<string>();
            foreach (TileData tile in file.Tiles)
            {
                if (!tileNames.Add(tile.Name))
                    report.Errors.Add($"Kafelek {tile.Name} występuje dwa razy.");
                if (tile.Rotations != 1 && tile.Rotations != DirectionExtensions.SideCount)
                    report.Errors.Add($"Kafelek {tile.Name} ma {tile.Rotations} obrotów; dozwolone 1 albo 4.");
                if (tile.Symmetric != (tile.Rotations == 1))
                    report.Warnings.Add($"Kafelek {tile.Name}: symetryczny = {tile.Symmetric}, a obrotów {tile.Rotations}.");
                if (!(tile.Weight > 0))
                    report.Errors.Add($"Kafelek {tile.Name} ma wagę {tile.Weight}; waga musi być większa od 0.");
                foreach (Direction side in DirectionExtensions.Sides)
                {
                    string label = tile.GetLabel(side);
                    usedSide.Add(label);
                    if (!sideKinds.ContainsKey(label))
                        report.Errors.Add($"Kafelek {tile.Name}, strona {side}: etykiety bocznej {label} nie ma w etykietyBoczne.");
                }
                foreach (Direction vertical in new[] { Direction.Up, Direction.Down })
                {
                    string label = tile.GetLabel(vertical);
                    usedVertical.Add(label);
                    if (!verticalLabels.ContainsKey(label))
                        report.Errors.Add($"Kafelek {tile.Name}, {vertical.RulesKey()}: etykiety pionowej {label} nie ma w etykietyPionowe.");
                    else if (verticalLabels[label].SameRotation && tile.Rotations == 1)
                        report.Warnings.Add($"Kafelek {tile.Name} ma 1 obrót, a etykietę {label} zależną od obrotu.");
                }
            }
            foreach (string label in sideKinds.Keys)
                if (!usedSide.Contains(label))
                    report.Warnings.Add($"Etykieta boczna {label} nie jest używana przez żaden kafelek.");
            foreach (string label in verticalLabels.Keys)
                if (!usedVertical.Contains(label))
                    report.Warnings.Add($"Etykieta pionowa {label} nie jest używana przez żaden kafelek.");

            var pairs = new HashSet<(string, string)>();
            foreach (LabelPair pair in file.CompatiblePairs)
            {
                foreach (string label in new[] { pair.A, pair.B })
                    if (!sideKinds.ContainsKey(label))
                        report.Errors.Add($"Para {pair} zawiera etykietę {label}, której nie ma w etykietyBoczne.");
                if (pairs.Contains((pair.A, pair.B)))
                    report.Warnings.Add($"Para {pair} występuje w paryZgodne więcej niż raz.");
                pairs.Add((pair.A, pair.B));
                pairs.Add((pair.B, pair.A));
            }
            foreach (LabelPair pair in file.ExtraPairs)
                if (!pairs.Contains((pair.A, pair.B)))
                    report.Errors.Add($"Para {pair} z paryDodatkowe nie występuje w paryZgodne.");

            foreach (SideLabelInfo label in file.SideLabels)
            {
                bool self = pairs.Contains((label.Name, label.Name));
                bool hasPartner = false;
                foreach (SideLabelInfo other in file.SideLabels)
                    hasPartner |= pairs.Contains((label.Name, other.Name));
                if (!hasPartner)
                    report.Errors.Add($"Etykieta boczna {label.Name} nie pasuje do żadnej etykiety.");
                switch (label.Kind)
                {
                    case SideLabelKind.Self when !self:
                        report.Errors.Add($"Etykieta {label.Name} jest do_siebie, a nie ma pary ({label.Name}, {label.Name}).");
                        break;
                    case SideLabelKind.MirroredPair:
                        if (self)
                            report.Errors.Add($"Etykieta {label.Name} jest para_odbita, a pasuje do samej siebie.");
                        string partner = MirroredPartner(label.Name);
                        if (!sideKinds.TryGetValue(partner, out SideLabelKind partnerKind) || partnerKind != SideLabelKind.MirroredPair)
                            report.Errors.Add($"Etykieta {label.Name} jest para_odbita, a nie ma etykiety {partner} tego samego rodzaju.");
                        else if (!pairs.Contains((label.Name, partner)))
                            report.Errors.Add($"Etykieta {label.Name} jest para_odbita, a nie ma pary ({label.Name}, {partner}).");
                        break;
                }
            }

            foreach (TileList set in file.Sets)
            {
                var inSet = new HashSet<string>();
                foreach (string name in set.TileNames)
                {
                    if (!tileNames.Contains(name))
                        report.Errors.Add($"Zestaw {set.Name} zawiera kafelek {name}, którego nie ma na liście kafelki.");
                    if (!inSet.Add(name))
                        report.Errors.Add($"Zestaw {set.Name} zawiera kafelek {name} dwa razy.");
                }
                if (!control.Sets.ContainsKey(set.Name))
                    report.Warnings.Add($"Sekcja kontrola nie podaje liczb dla zestawu {set.Name}.");
            }
            foreach (string name in control.Sets.Keys)
                if (file.FindSet(name) == null)
                    report.Errors.Add($"Sekcja kontrola podaje zestaw {name}, którego nie ma w zestawy.");

            // Liczba stanów wymaga kompilacji; przy błędach w kafelkach kompilacja może się nie udać.
            if (report.IsValid)
            {
                CheckCount(report, "stanów", RuleCompiler.Compile(file.CreateTilesetWithAllTiles()).StateCount, control.States);
                foreach (TileList set in file.Sets)
                    if (control.Sets.TryGetValue(set.Name, out ControlCounts counts))
                    {
                        CheckCount(report, $"kafelków w zestawie {set.Name}", set.TileNames.Count, counts.Tiles);
                        CheckCount(report, $"stanów w zestawie {set.Name}", RuleCompiler.Compile(file.CreateTileset(set.Name)).StateCount, counts.States);
                    }
            }

            return report;
        }

        /// <summary>Druga etykieta pary odbitej: mur_parter ↔ mur_parter_odbity.</summary>
        public static string MirroredPartner(string label)
        {
            const string suffix = "_odbity";
            return label.EndsWith(suffix, StringComparison.Ordinal) ? label.Substring(0, label.Length - suffix.Length) : label + suffix;
        }

        /// <summary>
        /// Na których piętrach stan może stanąć. Uproszczenie siatki do jednej kolumny pięter: stan zostaje na piętrze,
        /// jeśli ma pasującego sąsiada pod sobą i nad sobą (albo dno i górę siatki), a na każdym boku pasującego
        /// sąsiada na tym samym piętrze albo może stać przy krawędzi. Usuwanie powtarzamy, aż nic się nie zmienia.
        /// Uproszczenie niczego nie usuwa niesłusznie, więc stan usunięty ze wszystkich pięter naprawdę nie wystąpi.
        /// </summary>
        static void FindFloors(CompiledRules rules, int floors, ValidationReport report)
        {
            int states = rules.StateCount;
            int words = rules.WordCount;
            var possible = new ulong[floors * words];
            for (int floor = 0; floor < floors; floor++)
            {
                Span<ulong> mask = possible.AsSpan(floor * words, words);
                BitMask.SetFirst(mask, states);
                if (floor == 0)
                    And(mask, rules.BottomMask);
                if (floor == floors - 1)
                    And(mask, rules.TopMask);
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int floor = 0; floor < floors; floor++)
                {
                    Span<ulong> mask = possible.AsSpan(floor * words, words);
                    for (int state = 0; state < states; state++)
                    {
                        if (!BitMask.Get(mask, state))
                            continue;
                        bool supported =
                            (floor == 0 || BitMask.Intersects(rules.GetNeighborMask(state, Direction.Down), possible.AsSpan((floor - 1) * words, words))) &&
                            (floor == floors - 1 || BitMask.Intersects(rules.GetNeighborMask(state, Direction.Up), possible.AsSpan((floor + 1) * words, words)));
                        foreach (Direction side in DirectionExtensions.Sides)
                            supported = supported &&
                                        (BitMask.Get(rules.GetSideEdgeMask(side, floor == 0), state) ||
                                         BitMask.Intersects(rules.GetNeighborMask(state, side), mask));
                        if (!supported)
                        {
                            BitMask.Clear(mask, state);
                            changed = true;
                        }
                    }
                }
            }

            report.MinFloor = new int[states];
            report.MaxFloor = new int[states];
            for (int state = 0; state < states; state++)
            {
                report.MinFloor[state] = -1;
                report.MaxFloor[state] = -1;
                for (int floor = 0; floor < floors; floor++)
                    if (BitMask.Get(possible.AsSpan(floor * words, words), state))
                    {
                        if (report.MinFloor[state] < 0)
                            report.MinFloor[state] = floor;
                        report.MaxFloor[state] = floor;
                    }
                if (report.MinFloor[state] < 0)
                    report.NeverOccurringStates.Add(state);
            }
        }

        static bool CanTouchGridEdge(CompiledRules rules, int state, Direction direction)
        {
            switch (direction)
            {
                case Direction.Down: return BitMask.Get(rules.BottomMask, state);
                case Direction.Up: return BitMask.Get(rules.TopMask, state);
                default:
                    return BitMask.Get(rules.GetSideEdgeMask(direction, true), state) ||
                           BitMask.Get(rules.GetSideEdgeMask(direction, false), state);
            }
        }

        static void And(Span<ulong> target, ReadOnlySpan<ulong> mask)
        {
            for (int i = 0; i < target.Length; i++)
                target[i] &= mask[i];
        }

        static void CheckCount(ValidationReport report, string what, int actual, int expected)
        {
            if (actual != expected)
                report.Errors.Add($"Liczba {what}: {actual}, a sekcja kontrola podaje {expected}.");
        }
    }
}
