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
    /// Tworzy scenę Generator z hierarchią z rozdziału 5: [Systemy] (GeneratorController, MapBuilder), [Mapa],
    /// [Kamera] z FlyCamera i [Swiatlo]. [Debug] i [UI] dojdą razem ze StepVisualizer i panelem ustawień.
    /// </summary>
    public static class GeneratorSceneBuilder
    {
        public const string ScenePath = WfcPaths.Scenes + "/Generator.unity";

        [MenuItem("WFC/Utwórz scenę Generator", priority = 21)]
        static void CreateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Generator", $"Scena {ScenePath} już istnieje. Nadpisać ją?", "Nadpisz", "Anuluj"))
                return;
            Create();
        }

        public static Scene Create()
        {
            var tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset(TilesetPreviewSceneBuilder.DefaultTileset));
            if (tileset == null)
                throw new InvalidOperationException("Brak zestawu Pelny. Najpierw uruchom WFC → Importuj reguły i modele.");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var systems = new GameObject("[Systemy]");
            var map = new GameObject("[Mapa]");

            var builder = new GameObject("MapBuilder").AddComponent<MapBuilder>();
            builder.transform.SetParent(systems.transform, false);
            builder.MapRoot = map.transform;

            var controller = new GameObject("GeneratorController").AddComponent<GeneratorController>();
            controller.transform.SetParent(systems.transform, false);
            controller.transform.SetSiblingIndex(0);
            controller.Tileset = tileset;
            controller.MapBuilder = builder;

            var cameraGroup = new GameObject("[Kamera]");
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(cameraGroup.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.farClipPlane = 1000f;
            cameraObject.AddComponent<AudioListener>();
            Vector3 center = new Vector3(controller.GridSize.x, 0f, controller.GridSize.z) * (UnityGrid.CellSize / 2f);
            cameraObject.AddComponent<FlyCamera>().LookAt(center + Vector3.up * 6f, 95f, 30f, 40f);

            var lightGroup = new GameObject("[Swiatlo]");
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.transform.SetParent(lightGroup.transform, false);
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            AssetFolders.Ensure(WfcPaths.Scenes);
            EditorSceneManager.SaveScene(scene, ScenePath);
            return scene;
        }
    }
}
