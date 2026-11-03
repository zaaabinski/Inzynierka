using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WFC.Runtime;
using Object = UnityEngine.Object;

namespace WFC.Editor
{
    /// <summary>
    /// Zapisuje scaloną mapę jako zasoby: siatki fragmentów w jednym pliku, przy atlasie także paletę i jej materiał, oraz
    /// prefab z obiektem na fragment. Zapisany prefab nie zależy od generatora, można go wstawić do dowolnej sceny.
    /// </summary>
    public static class MapBaker
    {
        const string BaseMap = "_BaseMap";

        /// <param name="map">Mapa zbudowana z keepReadable, bo kopie siatek powstają z ich danych w pamięci procesora.</param>
        /// <param name="folder">Folder pod Assets; brakujące foldery powstają same.</param>
        /// <param name="name">Nazwa prefabu; jeśli jest zajęta, Unity dopisze numer.</param>
        /// <returns>Zasób prefabu.</returns>
        public static GameObject Bake(CombinedMap map, string folder, string name)
        {
            if (map.RendererCount == 0)
                throw new InvalidOperationException("Mapa jest pusta, nie ma czego zapisać.");
            if (map.Meshes.Any(mesh => !mesh.isReadable))
                throw new InvalidOperationException("Zapis wymaga mapy z kopią siatek w pamięci procesora (keepReadable).");

            AssetFolders.Ensure(folder);
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.prefab");
            string uniqueName = Path.GetFileNameWithoutExtension(prefabPath);
            string basePath = $"{folder}/{uniqueName}";

            Material atlasMaterial = map.Atlas != null ? SaveAtlas(map.Atlas, basePath) : null;
            var root = new GameObject(uniqueName);
            try
            {
                SaveChunks(map, root.transform, $"{basePath}_Siatki.asset", atlasMaterial);
                AssetDatabase.SaveAssets();
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Rozmiar pliku z siatkami prefabu w bajtach; dane siatek w pliku tekstowym zajmują około 2 razy więcej niż w pamięci.</summary>
        public static long MeshesFileSize(GameObject prefab)
        {
            Mesh mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            return new FileInfo(AssetDatabase.GetAssetPath(mesh)).Length;
        }

        static Material SaveAtlas(MaterialAtlas atlas, string basePath)
        {
            Texture2D texture = Object.Instantiate(atlas.Texture);
            texture.name = atlas.Texture.name;
            texture.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(texture, $"{basePath}_Paleta.asset");

            var material = new Material(atlas.Material) { name = atlas.Material.name, hideFlags = HideFlags.None };
            material.SetTexture(BaseMap, texture);
            AssetDatabase.CreateAsset(material, $"{basePath}_Material.mat");
            return material;
        }

        static void SaveChunks(CombinedMap map, Transform root, string meshesPath, Material atlasMaterial)
        {
            bool first = true;
            foreach (MeshRenderer source in map.Renderers)
            {
                Mesh mesh = Object.Instantiate(source.GetComponent<MeshFilter>().sharedMesh);
                mesh.name = source.gameObject.name;
                mesh.hideFlags = HideFlags.None;
                if (first)
                    AssetDatabase.CreateAsset(mesh, meshesPath);
                else
                    AssetDatabase.AddObjectToAsset(mesh, meshesPath);
                first = false;

                // Tak jak w aplikacji: po wczytaniu dane siatki są tylko na karcie graficznej.
                using (var serialized = new SerializedObject(mesh))
                {
                    serialized.FindProperty("m_IsReadable").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                var chunk = new GameObject(source.gameObject.name, typeof(MeshFilter), typeof(MeshRenderer));
                chunk.transform.SetParent(root, false);
                chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
                chunk.GetComponent<MeshRenderer>().sharedMaterials = atlasMaterial != null ? new[] { atlasMaterial } : source.sharedMaterials;
            }
        }
    }
}
