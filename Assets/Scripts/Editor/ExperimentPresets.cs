using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Eksperymenty E1–E6 z rozdziału 7 handoffu jako zasoby BenchmarkConfig w Assets/Data/Experiments. Bazą jest
    /// siatka 20 × 20 × 13, zestaw Pelny, kolejność piętrami, start od nowa i ziarna 1–100. Warianty, których
    /// jeszcze nie ma (szybsze rozwiązywacze, cofanie, warianty reguł), dojdą do tych samych zasobów w kolejnych etapach.
    /// </summary>
    public static class ExperimentPresets
    {
        static readonly Vector3Int BaseSize = new Vector3Int(20, 13, 20);

        static readonly Vector3Int[] ScalingSizes =
        {
            new Vector3Int(10, 8, 10), new Vector3Int(20, 13, 20), new Vector3Int(40, 13, 40), new Vector3Int(80, 13, 80)
        };

        [MenuItem("WFC/Utwórz eksperymenty E1–E6", priority = 30)]
        static void CreateFromMenu()
        {
            List<BenchmarkConfig> experiments = CreateOrUpdate();
            Debug.Log("[ExperimentPresets] " + string.Join("\n", experiments.Select(e => e.Summary())));
        }

        public static List<BenchmarkConfig> LoadAll() =>
            AssetDatabase.FindAssets("t:BenchmarkConfig", new[] { WfcPaths.Experiments })
                .Select(guid => AssetDatabase.LoadAssetAtPath<BenchmarkConfig>(AssetDatabase.GUIDToAssetPath(guid)))
                .OrderBy(config => config.ExperimentName, StringComparer.Ordinal)
                .ToList();

        /// <summary>Tworzy albo nadpisuje zasoby E1–E6 (zmiany ręczne w tych zasobach zostaną zastąpione).</summary>
        public static List<BenchmarkConfig> CreateOrUpdate()
        {
            TilesetDefinition full = Tileset("Pelny");
            var experiments = new List<BenchmarkConfig>
            {
                Create("E1", "Rozwiązywacze: ten sam zestaw i kolejność, różny sposób przechowywania możliwości (pytania 1 i 7). " +
                             "Na razie NaiveSolver; SupportCount, Bitset i Burst dojdą w kamieniu 6.",
                    config => config.Tilesets.Add(full)),
                Create("E2", "Kolejność wyboru komórek (pytanie 2): piętrami, Scanline, MRV (najmniej możliwości) i entropia. " +
                             "MRV i entropia kończą się sprzecznością w około 97% prób, więc ta seria trwa najdłużej.",
                    config =>
                    {
                        config.Tilesets.Add(full);
                        config.Selectors.Clear();
                        config.Selectors.AddRange((SelectorKind[])Enum.GetValues(typeof(SelectorKind)));
                    }),
                Create("E3", "Reakcja na sprzeczność przy rosnącej siatce (pytanie 3). Na razie start od nowa; " +
                             "cofanie decyzji i reset lokalny dojdą w kamieniu 7.",
                    config =>
                    {
                        config.Tilesets.Add(full);
                        config.GridSizes.Clear();
                        config.GridSizes.AddRange(ScalingSizes);
                    }),
                Create("E4", "Skalowanie (pytanie 4): czas i pamięć dla siatek 10 × 10 × 8, 20 × 20 × 13, 40 × 40 × 13 i 80 × 80 × 13. " +
                             "Limit 600 s na mapę; przekroczenie zapisuje się jako wynik.",
                    config =>
                    {
                        config.Tilesets.Add(full);
                        config.GridSizes.Clear();
                        config.GridSizes.AddRange(ScalingSizes);
                        config.TimeLimitSeconds = 600f;
                    }),
                Create("E5", "Liczba kafelków (pytanie 5): zestawy Prosty (26 stanów), Średni (79), PelnyBezDekoracji (146) i Pełny (170).",
                    config => config.Tilesets.AddRange(new[] { Tileset("Prosty"), Tileset("Sredni"), Tileset("PelnyBezDekoracji"), full })),
                Create("E6", "Reguły i wagi (pytanie 6). Na razie obecne reguły; warianty bez wolnego pola przy ścianach, " +
                             "bez drzwi nad schodami i z wagami trawy × 0,4 i × 0,25 dojdą w etapie 5.",
                    config => config.Tilesets.Add(full))
            };
            AssetDatabase.SaveAssets();
            return experiments;
        }

        static BenchmarkConfig Create(string experimentName, string description, Action<BenchmarkConfig> setup)
        {
            var config = AssetFolders.LoadOrCreate<BenchmarkConfig>(WfcPaths.Experiment(experimentName));
            config.ExperimentName = experimentName;
            config.Description = description;
            config.Solvers.Clear();
            config.Solvers.Add(SolverKind.Naive);
            config.Selectors.Clear();
            config.Selectors.Add(SelectorKind.LevelFirst);
            config.ContradictionHandlers.Clear();
            config.ContradictionHandlers.Add(ContradictionKind.Restart);
            config.Tilesets.Clear();
            config.GridSizes.Clear();
            config.GridSizes.Add(BaseSize);
            config.FirstSeed = 1;
            config.SeedCount = 100;
            config.Repetitions = 1;
            config.WarmupRuns = 5;
            config.MaxAttempts = 1000;
            config.TimeLimitSeconds = 0f;
            config.ShowEveryNth = 1;
            setup(config);
            EditorUtility.SetDirty(config);
            return config;
        }

        static TilesetDefinition Tileset(string setName) =>
            AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset(setName)) ??
            throw new InvalidOperationException($"Brak zestawu {WfcPaths.Tileset(setName)}. Najpierw uruchom WFC → Importuj reguły i modele.");
    }
}
