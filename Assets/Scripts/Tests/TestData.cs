using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>Wspólne dane testów: plik reguł i arkusz z folderu Assets/Data/Rules.</summary>
    static class TestData
    {
        static RulesFile rules;
        static XlsxWorkbook sheet;
        static readonly Dictionary<string, CompiledRules> compiled = new Dictionary<string, CompiledRules>();

        public static readonly string[] SetNames = { "Prosty", "Sredni", "PelnyBezDekoracji", "Pelny" };

        public static string ProjectPath(string relativePath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, relativePath);

        /// <summary>Plik reguł wczytany raz na wszystkie testy; testy, które go zmieniają, używają ReadRules.</summary>
        public static RulesFile Rules => rules ?? (rules = ReadRules());

        public static RulesFile ReadRules() => RulesJsonReader.Parse(File.ReadAllText(ProjectPath(WfcPaths.RulesJson)));

        public static XlsxWorkbook Sheet => sheet ?? (sheet = XlsxWorkbook.Open(ProjectPath(WfcPaths.RulesSheet)));

        public static int Floors => Rules.Grid.Floors;

        public static CompiledRules Compile(string setName)
        {
            if (!compiled.TryGetValue(setName, out CompiledRules result))
                compiled[setName] = result = RuleCompiler.Compile(Rules.CreateTileset(setName));
            return result;
        }

        /// <summary>Etykieta z arkusza na zapis po kompilacji: „nad_schodami (90°)” → „nad_schodami@90”.</summary>
        public static string SheetLabel(string label)
        {
            Match match = Regex.Match(label, @"^(.+) \((\d+)°\)$");
            return match.Success ? $"{match.Groups[1].Value}{RuleCompiler.RotationSeparator}{match.Groups[2].Value}" : label;
        }

        /// <summary>Kąt z arkusza na numer obrotu: „270°” → 3.</summary>
        public static int SheetRotation(string angle) => int.Parse(angle.Trim().TrimEnd('°')) / 90;
    }
}
