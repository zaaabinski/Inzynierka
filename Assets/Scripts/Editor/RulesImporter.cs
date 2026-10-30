using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Zamienia plik reguł w zasoby Unity: materiały wyciągnięte z modeli, prefab na każdy model, TileDefinition
    /// na każdy kafelek i TilesetDefinition na każdy zestaw oraz na każdy wariant reguł do E6 (RuleVariants).
    /// Istniejące zasoby aktualizuje, nie usuwa żadnego.
    /// Na końcu kompiluje i sprawdza zestawy już z zasobów, tak jak zobaczy je program.
    /// </summary>
    public static class RulesImporter
    {
        public sealed class Result
        {
            public string RulesVersion;
            public int Models;
            public int Materials;
            public int Prefabs;
            public int Tiles;
            public int Tilesets;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<ValidationReport> Reports = new List<ValidationReport>();

            public string Summary =>
                $"Import reguł {RulesVersion}: modele {Models}, materiały {Materials}, prefaby {Prefabs}, " +
                $"kafelki {Tiles}, zestawy {Tilesets}; błędy: {Errors.Count}, ostrzeżenia: {Warnings.Count}.";

            public void Add(ValidationReport report)
            {
                Reports.Add(report);
                Errors.AddRange(report.Errors.Select(error => $"{report.Subject}: {error}"));
                Warnings.AddRange(report.Warnings.Select(warning => $"{report.Subject}: {warning}"));
            }
        }

        [MenuItem("WFC/Importuj reguły i modele", priority = 0)]
        static void ImportFromMenu()
        {
            Result result = ImportAll();
            Log(result);
            EditorUtility.DisplayDialog("Import reguł WFC", result.Summary + "\n\nSzczegóły są w konsoli.", "OK");
        }

        [MenuItem("WFC/Sprawdź zestawy", priority = 1)]
        static void ValidateFromMenu()
        {
            int floors = ReadRules().Grid.Floors;
            foreach (string guid in AssetDatabase.FindAssets("t:TilesetDefinition"))
            {
                var tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                ValidationReport report = TilesetValidator.Validate(RuleCompiler.Compile(tileset.ToTilesetData()), floors);
                if (report.IsValid)
                    Debug.Log(report, tileset);
                else
                    Debug.LogError(report, tileset);
            }
        }

        /// <summary>Wejście dla trybu wsadowego: Unity -batchmode -executeMethod WFC.Editor.RulesImporter.ImportAllBatch.</summary>
        public static void ImportAllBatch()
        {
            try
            {
                Result result = ImportAll();
                Log(result);
                if (result.Errors.Count == 0 && !File.Exists(WfcPaths.TilesetPreviewScene))
                    TilesetPreviewSceneBuilder.Create();
                if (result.Errors.Count == 0 && !File.Exists(GeneratorSceneBuilder.ScenePath))
                    GeneratorSceneBuilder.Create();
                EditorApplication.Exit(result.Errors.Count == 0 ? 0 : 1);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(2);
            }
        }

        public static RulesFile ReadRules() => RulesJsonReader.Parse(File.ReadAllText(WfcPaths.RulesJson));

        public static Result ImportAll()
        {
            var result = new Result();
            RulesFile rules = ReadRules();
            result.RulesVersion = rules.Version;
            result.Add(TilesetValidator.ValidateRules(rules));

            // 1. Modele i materiały.
            Dictionary<string, string> models = ModelLibrary.FindModels(result.Warnings);
            var neededModels = rules.Tiles.Where(tile => tile.HasModel && tile.ModelAvailable).Select(tile => tile.Model).Distinct().ToList();
            foreach (string model in neededModels.Where(model => !models.ContainsKey(model)))
                result.Errors.Add($"Brak modelu {model}.fbx w {WfcPaths.Models}.");
            foreach (string model in models.Keys.Where(model => !neededModels.Contains(model)))
                result.Warnings.Add($"Model {model} nie jest używany przez żaden kafelek.");
            var modelPaths = neededModels.Where(models.ContainsKey).Select(model => models[model]).ToList();
            result.Models = modelPaths.Count;
            result.Materials = ModelLibrary.ExtractAndRemapMaterials(modelPaths);

            // 2. Prefaby, jeden na model.
            var prefabs = new Dictionary<string, GameObject>();
            foreach (string model in neededModels.Where(models.ContainsKey))
                prefabs[model] = TilePrefabBuilder.BuildOrUpdate(model, models[model]);
            result.Prefabs = prefabs.Count;

            // 3. Kafelki.
            var tileAssets = new Dictionary<string, TileDefinition>();
            foreach (TileData tile in rules.Tiles)
            {
                var asset = AssetFolders.LoadOrCreate<TileDefinition>(WfcPaths.Tile(tile.Name));
                asset.CopyFrom(tile, FindPrefab(rules, tile, prefabs, result.Warnings));
                EditorUtility.SetDirty(asset);
                tileAssets[tile.Name] = asset;
            }
            result.Tiles = tileAssets.Count;
            foreach (string guid in AssetDatabase.FindAssets("t:TileDefinition", new[] { WfcPaths.Tiles }))
            {
                string name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (!tileAssets.ContainsKey(name))
                    result.Warnings.Add($"Zasób kafelka {name} nie ma odpowiednika w pliku reguł; zostawiony bez zmian.");
            }

            // 4. Zestawy.
            var tilesets = new List<TilesetDefinition>();
            foreach (TileList set in rules.Sets)
            {
                TilesetData data = rules.CreateTileset(set.Name);
                var asset = AssetFolders.LoadOrCreate<TilesetDefinition>(WfcPaths.Tileset(set.Name));
                asset.CopyFrom(data, set.TileNames.Select(name => tileAssets[name]), rules.Version);
                EditorUtility.SetDirty(asset);
                tilesets.Add(asset);
            }
            foreach (RuleVariant variant in RuleVariants.All)
            {
                TilesetData data = rules.CreateTileset(variant.Name);
                var asset = AssetFolders.LoadOrCreate<TilesetDefinition>(WfcPaths.Tileset(variant.Name));
                asset.CopyVariantFrom(data, data.Tiles.Select(tile => tileAssets[tile.Name]), rules.Version, variant.Description);
                EditorUtility.SetDirty(asset);
                tilesets.Add(asset);
            }
            result.Tilesets = tilesets.Count;
            AssetDatabase.SaveAssets();

            // 5. Sprawdzenie zestawów po imporcie.
            foreach (TilesetDefinition tileset in tilesets)
                result.Add(TilesetValidator.Validate(RuleCompiler.Compile(tileset.ToTilesetData()), rules.Grid.Floors));

            return result;
        }

        /// <summary>
        /// Prefab kafelka po nazwie modelu. Kafelek bez gotowego modelu (modelDostepny = false, np. Dach_Szczyt_Choragiew)
        /// dostaje prefab kafelka z tej samej grupy i o identycznych etykietach (Dach_Szczyt), jak zapisano w arkuszu reguł.
        /// </summary>
        static GameObject FindPrefab(RulesFile rules, TileData tile, Dictionary<string, GameObject> prefabs, List<string> warnings)
        {
            if (!tile.HasModel)
                return null;
            if (tile.ModelAvailable)
                return prefabs.TryGetValue(tile.Model, out GameObject prefab) ? prefab : null;

            TileData substitute = rules.Tiles.FirstOrDefault(other =>
                other.HasModel && other.ModelAvailable && other.Group == tile.Group && other.Labels.SequenceEqual(tile.Labels));
            if (substitute != null && prefabs.TryGetValue(substitute.Model, out GameObject substitutePrefab))
            {
                warnings.Add($"Kafelek {tile.Name} nie ma modelu {tile.Model}; do czasu jego zrobienia stawiany jest model {substitute.Model} (kafelek o tych samych etykietach).");
                return substitutePrefab;
            }
            warnings.Add($"Kafelek {tile.Name} nie ma modelu {tile.Model} ani kafelka zastępczego; w scenie nic się w nim nie postawi.");
            return null;
        }

        static void Log(Result result)
        {
            foreach (string error in result.Errors)
                Debug.LogError($"[RulesImporter] {error}");
            foreach (string warning in result.Warnings)
                Debug.LogWarning($"[RulesImporter] {warning}");
            foreach (ValidationReport report in result.Reports)
                Debug.Log($"[RulesImporter] {report.Subject}: błędy {report.Errors.Count}, ostrzeżenia {report.Warnings.Count}" +
                          (report.MinFloor.Length > 0 ? $", stany, które nigdy nie wystąpią: {report.NeverOccurringStates.Count}" : ""));
            if (result.Errors.Count == 0)
                Debug.Log($"[RulesImporter] {result.Summary}");
            else
                Debug.LogError($"[RulesImporter] {result.Summary}");
        }
    }
}
