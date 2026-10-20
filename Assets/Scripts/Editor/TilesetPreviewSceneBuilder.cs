using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Tworzy scenę Scenes/TilesetPreview: TilesetPreviewer z zestawem Pelny, Main Camera z FlyCamera
    /// i Directional Light. Podgląd buduje się po wciśnięciu Play albo z menu kontekstowego TilesetPreviewer.
    /// </summary>
    public static class TilesetPreviewSceneBuilder
    {
        public const string DefaultTileset = "Pelny";

        [MenuItem("WFC/Utwórz scenę TilesetPreview", priority = 20)]
        static void CreateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (File.Exists(WfcPaths.TilesetPreviewScene) &&
                !EditorUtility.DisplayDialog("TilesetPreview", $"Scena {WfcPaths.TilesetPreviewScene} już istnieje. Nadpisać ją?", "Nadpisz", "Anuluj"))
                return;
            Create();
        }

        public static Scene Create()
        {
            var tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset(DefaultTileset));
            if (tileset == null)
                throw new InvalidOperationException($"Brak zestawu {WfcPaths.Tileset(DefaultTileset)}. Najpierw uruchom WFC → Importuj reguły i modele.");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var previewer = new GameObject("TilesetPreviewer").AddComponent<TilesetPreviewer>();
            previewer.Tileset = tileset;

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.farClipPlane = 2000f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<FlyCamera>().LookAt(previewer.Center, 150f, 0f, 55f);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            AssetFolders.Ensure(WfcPaths.Scenes);
            EditorSceneManager.SaveScene(scene, WfcPaths.TilesetPreviewScene);
            return scene;
        }
    }
}
