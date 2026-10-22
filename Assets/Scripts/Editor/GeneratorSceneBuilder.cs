using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Tworzy scenę Generator z hierarchią z rozdziału 5: [Systemy] (GeneratorController, MapBuilder, BenchmarkRunner),
    /// [Mapa], [Kamera] z FlyCamera i zrzutami ekranu, [Swiatlo] i [UI] z oknem ustawień. Ta sama scena obsługuje
    /// pomiary z oknem i bez okna (argument -benchmark), więc osobna scena Benchmark nie jest potrzebna.
    /// [Debug] dojdzie razem ze StepVisualizer.
    /// </summary>
    public static class GeneratorSceneBuilder
    {
        public const string ScenePath = WfcPaths.GeneratorScene;

        static readonly string[] PanelTilesets = { "Pelny", "PelnyBezDekoracji", "Sredni", "Prosty" };

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
            // Nowa scena zwalnia nieużywane zasoby, więc zestaw, eksperymenty i ustawienia panelu wczytujemy dopiero po niej;
            // wczytane wcześniej byłyby już zwolnionymi obiektami i w scenie zapisałyby się puste odnośniki.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset(TilesetPreviewSceneBuilder.DefaultTileset));
            if (tileset == null)
                throw new InvalidOperationException("Brak zestawu Pelny. Najpierw uruchom WFC → Importuj reguły i modele.");
            List<BenchmarkConfig> experiments = ExperimentPresets.LoadAll();
            if (experiments.Count == 0)
                experiments = ExperimentPresets.CreateOrUpdate();
            PanelSettings panelSettings = UiAssets.EnsurePanelSettings();
            if (panelSettings == null || panelSettings.themeStyleSheet == null)
                throw new InvalidOperationException($"Brak ustawień panelu albo motywu w {WfcPaths.UiSettings}.");

            var systems = new GameObject("[Systemy]");
            var map = new GameObject("[Mapa]");

            var controller = new GameObject("GeneratorController").AddComponent<GeneratorController>();
            controller.transform.SetParent(systems.transform, false);
            controller.Tileset = tileset;
            controller.Solver = SolverKind.Bitset;

            var builder = new GameObject("MapBuilder").AddComponent<MapBuilder>();
            builder.transform.SetParent(systems.transform, false);
            builder.MapRoot = map.transform;
            controller.MapBuilder = builder;

            var runner = new GameObject("BenchmarkRunner").AddComponent<BenchmarkRunner>();
            runner.transform.SetParent(systems.transform, false);
            runner.MapBuilder = builder;
            runner.Generator = controller;
            runner.Experiments.AddRange(experiments);

            var cameraGroup = new GameObject("[Kamera]");
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(cameraGroup.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.farClipPlane = 1000f;
            cameraObject.AddComponent<AudioListener>();
            Vector3 center = new Vector3(controller.GridSize.x, 0f, controller.GridSize.z) * (UnityGrid.CellSize / 2f);
            cameraObject.AddComponent<FlyCamera>().LookAt(center + Vector3.up * 6f, 95f, 30f, 40f);
            cameraObject.AddComponent<ScreenshotTaker>();

            var lightGroup = new GameObject("[Swiatlo]");
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.transform.SetParent(lightGroup.transform, false);
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var uiGroup = new GameObject("[UI]");
            var panelObject = new GameObject("PanelGeneratora");
            panelObject.transform.SetParent(uiGroup.transform, false);
            panelObject.AddComponent<UIDocument>().panelSettings = panelSettings;
            var panel = panelObject.AddComponent<GeneratorPanel>();
            panel.Generator = controller;
            panel.Runner = runner;
            foreach (string setName in PanelTilesets)
            {
                var set = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset(setName));
                if (set != null)
                    panel.Tilesets.Add(set);
            }

            AssetFolders.Ensure(WfcPaths.Scenes);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (panelObject.GetComponent<UIDocument>().panelSettings == null || controller.Tileset == null)
                throw new InvalidOperationException("Scena Generator zapisała się z pustymi odnośnikami do zasobów.");
            return scene;
        }
    }
}
