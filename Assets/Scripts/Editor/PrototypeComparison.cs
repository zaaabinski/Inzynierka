using System;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Kryterium kamienia milowego 3: mapy 20 × 20 × 13 z zestawu Pelny dla ziaren 1–100 (NaiveSolver, piętrami
    /// z najmniejszą entropią, start od nowa) muszą mieć 0 naruszeń, a średnie mają być bliskie prototypowi
    /// w Pythonie (rozdział 9 handoffu). Mapy dla tego samego ziarna różnią się od prototypu, bo inny jest
    /// generator liczb losowych, więc porównujemy średnie.
    /// </summary>
    public static class PrototypeComparison
    {
        static readonly int3 Size = new int3(20, 13, 20);

        [MenuItem("WFC/Porównaj z prototypem (100 ziaren)", priority = 40)]
        static void RunFromMenu()
        {
            string report = Run(100, true);
            Debug.Log(report);
            EditorUtility.DisplayDialog("Porównanie z prototypem", "Raport jest w konsoli.", "OK");
        }

        /// <summary>
        /// Tryb wsadowy: Unity -batchmode -executeMethod WFC.Editor.PrototypeComparison.RunBatch -wfcSeeds 100 -wfcOut raport.md.
        /// Kod wyjścia 0, gdy wszystkie mapy są udane i nie mają naruszeń.
        /// </summary>
        public static void RunBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int seeds = int.Parse(Argument(args, "-wfcSeeds") ?? "100");
                string output = Argument(args, "-wfcOut");
                string report = Run(seeds, false, out SeriesSummary summary);
                Debug.Log(report);
                if (output != null)
                    File.WriteAllText(output, report, new UTF8Encoding(false));
                EditorApplication.Exit(summary.SuccessfulMaps == seeds && summary.Violations == 0 ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(2);
            }
        }

        public static string Run(int seeds, bool showProgress) => Run(seeds, showProgress, out _);

        public static string Run(int seeds, bool showProgress, out SeriesSummary summary)
        {
            var tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset("Pelny"));
            TilesetData data = tileset != null ? tileset.ToTilesetData() : RulesImporter.ReadRules().CreateTileset("Pelny");
            CompiledRules rules = RuleCompiler.Compile(data);
            summary = new SeriesSummary();
            try
            {
                for (uint seed = 1; seed <= seeds; seed++)
                {
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar("Porównanie z prototypem", $"Ziarno {seed} z {seeds}", (seed - 1f) / seeds))
                        break;
                    var solver = new NaiveSolver();
                    solver.Initialize(rules, Size, seed, new SolverSettings());
                    GenerationResult result = solver.Run();
                    RuleCheckReport check = result.Success ? RuleChecker.Check(rules, Size, result.States) : new RuleCheckReport();
                    StructureStats stats = result.Success ? StructureAnalyzer.Analyze(rules, Size, result.States) : new StructureStats();
                    summary.Add(rules, result, check, stats);
                }
            }
            finally
            {
                if (showProgress)
                    EditorUtility.ClearProgressBar();
            }
            return Format(summary);
        }

        static string Format(SeriesSummary s)
        {
            string N(double value, int decimals = 1) => SeriesSummary.Number(value, decimals);
            string Percent(double fraction) => N(100 * fraction) + "%";
            var text = new StringBuilder();
            text.AppendLine($"Porównanie z prototypem: siatka 20 × 20 × 13, zestaw Pelny, NaiveSolver + LevelFirst + Restart, ziarna 1–{s.Maps}.");
            text.AppendLine();
            text.AppendLine("| Wskaźnik | Unity | Prototyp (rozdział 9) |");
            text.AppendLine("| --- | --- | --- |");
            text.AppendLine($"| Próby / udane mapy | {s.Attempts} / {s.SuccessfulMaps} (1 mapa = {N(s.Maps == 0 ? 0 : (double)s.Attempts / s.Maps, 2)} próby) | 326 / 224 (1,46 próby) |");
            text.AppendLine($"| Sprzeczności | {s.Contradictions} ({Percent(s.ContradictionRate)}) | 102 (31%) |");
            text.AppendLine($"| Naruszenia zasad | {s.Violations} | 0 |");
            text.AppendLine($"| Wieże na mapę | {N(s.PerMap(s.Towers))} | 11,8 |");
            text.AppendLine($"| Donżony na mapę | {N(s.PerMap(s.Keeps))} | 6,4 |");
            text.AppendLine($"| Mosty na mapę | {N(s.PerMap(s.Bridges))} (na {s.MapsWithBridges} z {s.SuccessfulMaps} map) | 1,1 (na 133 z 224 map) |");
            text.AppendLine($"| Schody na mapę | {N(s.PerMap(s.Stairs))} (na {s.MapsWithStairs} z {s.SuccessfulMaps} map) | 4,4 (na 221 z 224 map) |");
            text.AppendLine($"| Wysokość wież [kostki] | {SeriesSummary.Range(s.TowerHeights)} | 2–13, średnio 5,9 |");
            text.AppendLine($"| Wysokość murów [kostki] | {SeriesSummary.Range(s.WallHeights)} | 2–9, średnio 2,8 |");
            text.AppendLine($"| Wysokość donżonów [kostki] | {SeriesSummary.Range(s.KeepHeights)} | 3–12, średnio 4,3 |");
            text.AppendLine($"| Długość mostów [kostki] | {SeriesSummary.Range(s.BridgeLengths)} | 3–16, średnio 5,6 |");
            text.AppendLine($"| Teren: trawa / z kamieniami / składowisko / wóz / studnia | {Percent(s.TerrainShare("Teren_Trawa"))} / {Percent(s.TerrainShare("Teren_Trawa_V2"))} / " +
                            $"{Percent(s.TerrainShare("Teren_Trawa_Skladowisko"))} / {Percent(s.TerrainShare("Teren_Trawa_Woz"))} / {Percent(s.TerrainShare("Teren_Trawa_Studnia"))} | 66% / 20% / 6,5% / 5% / 2% |");
            text.AppendLine($"| Czas na udaną mapę | {N(s.SuccessfulMaps == 0 ? 0 : s.SolverMs / s.SuccessfulMaps / 1000.0, 2)} s (edytor Unity, Mono) | 0,54 s (Python) |");
            text.AppendLine($"| Teren dostępny z krawędzi mapy | średnio {N(s.SuccessfulMaps == 0 ? 0 : s.ReachableTerrainPercentSum / s.SuccessfulMaps)}% | – |");
            text.AppendLine($"| Zamknięte dziedzińce na mapę | {N(s.PerMap(s.EnclosedCourtyards), 2)} | – |");
            for (int type = 0; type < s.ViolationsByType.Length; type++)
                if (s.ViolationsByType[type] > 0)
                    text.AppendLine($"| Naruszenie: {RuleCheckReport.Describe((ViolationType)type)} | {s.ViolationsByType[type]} | 0 |");
            return text.ToString();
        }

        static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
