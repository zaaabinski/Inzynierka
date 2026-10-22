using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Kryterium kamienia milowego 6: dla ziaren 1–100 na siatce 20 × 20 × 13 (zestaw Pelny, piętrami, start od nowa)
    /// SupportCount, Bitset i Burst dają mapy identyczne z NaiveSolver: ten sam stan każdej komórki, te same próby,
    /// decyzje i sprzeczności. Raport podaje też średni czas, czas propagacji i pamięć struktur każdego rozwiązywacza
    /// w edytorze (Mono); właściwe pomiary robi eksperyment E1 w zbudowanej aplikacji.
    /// </summary>
    public static class SolverComparison
    {
        static readonly int3 Size = new int3(20, 13, 20);

        static readonly SolverKind[] Solvers = (SolverKind[])Enum.GetValues(typeof(SolverKind));

        [MenuItem("WFC/Porównaj rozwiązywacze (100 ziaren)", priority = 41)]
        static void RunFromMenu()
        {
            string report = Run(100, true, out bool identical);
            Debug.Log(report);
            EditorUtility.DisplayDialog("Porównanie rozwiązywaczy",
                (identical ? "Wszystkie mapy identyczne z NaiveSolver." : "Są mapy różne od NaiveSolver!") + " Raport jest w konsoli.", "OK");
        }

        /// <summary>
        /// Tryb wsadowy: Unity -batchmode -executeMethod WFC.Editor.SolverComparison.RunBatch -wfcSeeds 100 -wfcOut raport.md.
        /// Kod wyjścia 0, gdy wszystkie mapy są identyczne z NaiveSolver.
        /// </summary>
        public static void RunBatch()
        {
            try
            {
                string report = Run(CommandLine.GetInt("-wfcSeeds", 100), false, out bool identical);
                Debug.Log(report);
                string output = CommandLine.Get("-wfcOut");
                if (output != null)
                    File.WriteAllText(output, report, new UTF8Encoding(false));
                EditorApplication.Exit(identical ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(2);
            }
        }

        sealed class Totals
        {
            public int SameMaps;
            public int Successful;
            public double TotalMs;
            public double PropagationMs;
            public long PeakMemoryBytes;
            public readonly List<string> Differences = new List<string>();
        }

        public static string Run(int seeds, bool showProgress, out bool identical)
        {
            var tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset("Pelny"));
            TilesetData data = tileset != null ? tileset.ToTilesetData() : RulesImporter.ReadRules().CreateTileset("Pelny");
            CompiledRules rules = RuleCompiler.Compile(data);
            Dictionary<SolverKind, Totals> totals = Solvers.ToDictionary(kind => kind, _ => new Totals());
            int done = 0;
            try
            {
                for (uint seed = 1; seed <= seeds; seed++)
                {
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar("Porównanie rozwiązywaczy", $"Ziarno {seed} z {seeds}", (seed - 1f) / seeds))
                        break;
                    GenerationResult reference = null;
                    foreach (SolverKind kind in Solvers)
                    {
                        IWfcSolver solver = WfcFactory.CreateSolver(kind);
                        solver.Initialize(rules, Size, seed, new SolverSettings());
                        GenerationResult result = solver.Run();
                        reference ??= result;
                        Totals total = totals[kind];
                        total.TotalMs += result.Metrics.TotalMs;
                        total.PropagationMs += result.Metrics.PropagationMs;
                        total.PeakMemoryBytes = Math.Max(total.PeakMemoryBytes, solver.PeakMemoryBytes);
                        total.Successful += result.Success ? 1 : 0;
                        string difference = Compare(reference, result);
                        if (difference == null)
                            total.SameMaps++;
                        else
                            total.Differences.Add($"ziarno {seed}: {difference}");
                    }
                    done++;
                }
            }
            finally
            {
                if (showProgress)
                    EditorUtility.ClearProgressBar();
            }
            identical = done == seeds && totals.Values.All(total => total.SameMaps == done);
            return Format(totals, done);
        }

        /// <summary>Opis pierwszej różnicy względem NaiveSolver albo null, gdy mapa jest taka sama.</summary>
        static string Compare(GenerationResult expected, GenerationResult actual)
        {
            if (expected.Status != actual.Status)
                return $"status {actual.Status} zamiast {expected.Status}";
            if (expected.Metrics.Attempts != actual.Metrics.Attempts)
                return $"próby {actual.Metrics.Attempts} zamiast {expected.Metrics.Attempts}";
            if (expected.Metrics.Decisions != actual.Metrics.Decisions)
                return $"decyzje {actual.Metrics.Decisions} zamiast {expected.Metrics.Decisions}";
            if (expected.Metrics.Contradictions != actual.Metrics.Contradictions)
                return $"sprzeczności {actual.Metrics.Contradictions} zamiast {expected.Metrics.Contradictions}";
            int cells = expected.States.Where((state, cell) => state != actual.States[cell]).Count();
            return cells == 0 ? null : $"komórki z innym stanem: {cells}";
        }

        static string Format(Dictionary<SolverKind, Totals> totals, int maps)
        {
            string N(double value, int decimals = 1) => SeriesSummary.Number(value, decimals);
            double naiveMs = totals[SolverKind.Naive].TotalMs;
            var text = new StringBuilder();
            text.AppendLine($"Porównanie rozwiązywaczy: siatka 20 × 20 × 13, zestaw Pelny, LevelFirst + Restart, ziarna 1–{maps}, edytor Unity (Mono).");
            text.AppendLine($"Propagacja BurstSolver skompilowana przez Burst: {(BurstSolver.IsBurstCompiled ? "tak" : "nie (zwykły C#)")}.");
            text.AppendLine();
            text.AppendLine("| Rozwiązywacz | Mapy jak Naive | Udane | Czas na mapę [ms] | Propagacja [ms] | Szybciej niż Naive | Pamięć struktur [MB] |");
            text.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (KeyValuePair<SolverKind, Totals> pair in totals)
            {
                Totals t = pair.Value;
                double perMap = maps == 0 ? 0 : t.TotalMs / maps;
                text.AppendLine($"| {pair.Key} | {t.SameMaps} z {maps} | {t.Successful} | {N(perMap)} | {N(maps == 0 ? 0 : t.PropagationMs / maps)} | " +
                                $"{(pair.Key == SolverKind.Naive || t.TotalMs == 0 ? "–" : N(naiveMs / t.TotalMs) + " razy")} | {N(t.PeakMemoryBytes / 1048576.0, 2)} |");
            }
            foreach (KeyValuePair<SolverKind, Totals> pair in totals.Where(pair => pair.Value.Differences.Count > 0))
                text.AppendLine($"Różnice {pair.Key}: {string.Join("; ", pair.Value.Differences.Take(10))}");
            return text.ToString();
        }
    }
}
