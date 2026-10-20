using UnityEditor;
using UnityEngine;

namespace WFC.Editor
{
    static class AssetFolders
    {
        /// <summary>Tworzy brakujące foldery ścieżki, np. Assets/Data/Tiles.</summary>
        public static void Ensure(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                Ensure(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        /// <summary>Wczytuje zasób albo tworzy nowy pod podaną ścieżką.</summary>
        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            Ensure(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
