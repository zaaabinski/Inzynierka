using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>Modele FBX z folderu Resources/Models/CastlePieces i ich wspólne materiały w Materials/CastlePieces.</summary>
    static class ModelLibrary
    {
        /// <summary>Nazwa modelu (nazwa pliku bez rozszerzenia) → ścieżka pliku FBX.</summary>
        public static Dictionary<string, string> FindModels(List<string> warnings)
        {
            var models = new Dictionary<string, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { WfcPaths.Models }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    continue;
                string name = Path.GetFileNameWithoutExtension(path);
                if (models.TryGetValue(name, out string other))
                    warnings.Add($"Model {name} jest dwa razy: {other} i {path}; używam pierwszego.");
                else
                    models.Add(name, path);
            }
            return models;
        }

        /// <summary>
        /// Wyciąga materiały osadzone w modelach do Materials/CastlePieces, po jednym pliku na nazwę (M_Kamien, M_Bruk, …),
        /// i podpina je we wszystkich modelach, żeby 42 modele używały tych samych 12 materiałów. Włącza w nich
        /// instancjonowanie. Zwraca liczbę materiałów w folderze.
        /// </summary>
        public static int ExtractAndRemapMaterials(IReadOnlyCollection<string> modelPaths)
        {
            AssetFolders.Ensure(WfcPaths.Materials);

            // Krok 1: materiał, którego jeszcze nie ma w folderze, wyciągamy z pierwszego modelu, który go zawiera.
            foreach (string path in modelPaths)
            {
                bool extracted = false;
                foreach (Material embedded in EmbeddedMaterials(path))
                {
                    string target = MaterialPath(embedded.name);
                    if (AssetDatabase.LoadAssetAtPath<Material>(target) != null)
                        continue;
                    string error = AssetDatabase.ExtractAsset(embedded, target);
                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException($"Nie udało się wyciągnąć materiału {embedded.name} z {path}: {error}");
                    extracted = true;
                }
                if (extracted)
                {
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }

            // Krok 2: pozostałe osadzone materiały zastępujemy plikami z folderu o tej samej nazwie.
            foreach (string path in modelPaths)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                bool changed = false;
                foreach (Material embedded in EmbeddedMaterials(path))
                {
                    var external = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(embedded.name));
                    if (external == null)
                        continue;
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(embedded), external);
                    changed = true;
                }
                if (changed)
                    importer.SaveAndReimport();
            }

            // Krok 3: instancjonowanie włączone w zasobie, żeby build zachował warianty shadera do rysowania dużych map
            // instancjami (MapBuilder); przy zwykłych obiektach i tak rysuje SRP Batcher, więc wygląd się nie zmienia.
            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { WfcPaths.Materials });
            foreach (string guid in materialGuids)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material.enableInstancing)
                    continue;
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
            }

            return materialGuids.Length;
        }

        static IEnumerable<Material> EmbeddedMaterials(string modelPath) =>
            AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().ToArray();

        static string MaterialPath(string materialName) => $"{WfcPaths.Materials}/{materialName}.mat";
    }
}
