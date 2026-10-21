using System;
using UnityEditor;
using UnityEngine;
using WFC.Runtime;
using Object = UnityEngine.Object;

namespace WFC.Editor
{
    /// <summary>
    /// Prefab kafelka, jeden na model: root w środku podstawy kostki bez obrotu (obrót kafelka ustawia MapBuilder)
    /// i dziecko „Model” z modelem FBX obróconym o 180° wokół osi Y (rozdział 6).
    /// </summary>
    static class TilePrefabBuilder
    {
        public const string ModelChildName = "Model";

        public static GameObject BuildOrUpdate(string modelName, string modelPath)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
                throw new InvalidOperationException($"Nie można wczytać modelu {modelPath}.");

            AssetFolders.Ensure(WfcPaths.TilePrefabs);
            string path = WfcPaths.TilePrefab(modelName);

            // Istniejący prefab poprawiamy na miejscu, żeby zachować jego identyfikatory i odwołania do niego.
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(modelName);
            try
            {
                Transform model = root.transform.Find(ModelChildName);
                if (model != null && PrefabUtility.GetCorrespondingObjectFromOriginalSource(model.gameObject) != modelAsset)
                {
                    Object.DestroyImmediate(model.gameObject);
                    model = null;
                }
                if (model == null)
                {
                    model = ((GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform)).transform;
                    model.name = ModelChildName;
                }

                root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                model.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, UnityGrid.ModelBaseYaw, 0f));
                model.localScale = Vector3.one;

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved)
                    throw new InvalidOperationException($"Nie udało się zapisać prefabu {path}.");
                return prefab;
            }
            finally
            {
                if (exists)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }
        }
    }
}
