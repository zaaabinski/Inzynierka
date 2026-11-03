using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using WFC.Core;
using WFC.Editor;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>Narzędzie w edytorze: okno generatora, generacja w kawałkach, podgląd i zapis jako prefab.</summary>
    public sealed class GeneratorWindowTests
    {
        const string TestFolder = "Assets/TestZapisuMapy";

        GeneratorWindow window;

        [SetUp]
        public void Open()
        {
            window = ScriptableObject.CreateInstance<GeneratorWindow>();
            window.BuildInterface(new VisualElement());
            window.Settings.East = 10;
            window.Settings.North = 10;
            window.Settings.Floors = 8;
            window.BakeFolder = TestFolder;
        }

        [TearDown]
        public void Close()
        {
            if (window != null)
                Object.DestroyImmediate(window);
            AssetDatabase.DeleteAsset(TestFolder);
        }

        void GenerateToEnd()
        {
            window.Generate();
            Assert.IsTrue(window.IsGenerating, window.Report);
            for (int tick = 0; window.IsGenerating; tick++)
            {
                Assert.Less(tick, 100000);
                window.Tick(1);
            }
        }

        [Test]
        public void Okno_MaWszystkieParametryIPrzyciski()
        {
            var root = new VisualElement();
            window.BuildInterface(root);
            foreach (string name in new[] { "zestaw", "wymiar-e", "wymiar-n", "pietra", "rozwiazywacz", "heurystyka", "sprzecznosc", "ziarno", "limit-prob", "atlas", "fragment", "postep", "raport" })
                Assert.IsNotNull(root.Q(name), name);
            foreach (string name in new[] { "generuj", "nastepne-ziarno", "anuluj", "wyczysc", "zapisz", "zapisz-wstaw" })
                Assert.IsNotNull(root.Q<Button>(name), name);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset("Pelny")), window.Settings.Tileset, "domyślny zestaw");
            Assert.IsFalse(root.Q<Button>("anuluj").enabledSelf, "nie ma czego anulować");
            Assert.IsFalse(root.Q<Button>("zapisz").enabledSelf, "nie ma czego zapisać");
        }

        [Test]
        public void Generacja_TaSamaMapaCoBezOkna_PodgladPoza_Scena()
        {
            bool sceneWasDirty = SceneManager.GetActiveScene().isDirty;
            window.Settings.Seed = 5;
            GenerateToEnd();

            var solver = WfcFactory.CreateSolver(window.Settings.Solver);
            CompiledRules rules = RuleCompiler.Compile(window.Settings.Tileset.ToTilesetData());
            solver.Initialize(rules, window.Settings.Size, 5, WfcFactory.CreateSettings(window.Settings.Selector, window.Settings.Contradiction, window.Settings.MaxAttempts));
            GenerationResult expected = solver.Run();

            var builder = new GameObject("Mapa testowa");
            try
            {
                using (var plain = new CombinedMap(builder.transform, window.Settings.ChunkSize, true))
                {
                    plain.Build(window.Settings.Tileset, rules, expected.Grid, expected.States, 5);
                    Assert.AreEqual(plain.InstanceCount, window.Map.InstanceCount);
                    Assert.AreEqual(plain.VertexCount, window.Map.VertexCount);
                }
            }
            finally
            {
                Object.DestroyImmediate(builder);
            }

            Assert.AreEqual(HideFlags.DontSave, window.PreviewRoot.hideFlags);
            foreach (Transform chunk in window.PreviewRoot.transform)
                Assert.AreEqual(HideFlags.DontSave, chunk.gameObject.hideFlags, chunk.name);
            foreach (Mesh mesh in window.Map.Meshes)
                Assert.AreEqual(HideFlags.DontSave, mesh.hideFlags, mesh.name);
            Assert.AreEqual(HideFlags.DontSave, window.Map.Atlas.Material.hideFlags);
            Assert.AreEqual(HideFlags.DontSave, window.Map.Atlas.Texture.hideFlags);
            Assert.AreEqual(sceneWasDirty, SceneManager.GetActiveScene().isDirty, "podgląd nie zmienia sceny");
            StringAssert.Contains("udana", window.Report);
            StringAssert.Contains("Przed scaleniem", window.Report);
            StringAssert.Contains("Po scaleniu", window.Report);
        }

        [Test]
        public void Generacja_WKawalkach_JedenKrokNaWywolanie()
        {
            window.Generate();
            window.Tick(0);
            Assert.IsTrue(window.IsGenerating, "mapa 10 × 10 × 8 nie kończy się po jednym kroku");
            Assert.IsNull(window.Map, "mapa pojawia się po końcu generacji");
        }

        [Test]
        public void Anuluj_NastepnaGeneracjaDziala_PokazanaMapaZostaje()
        {
            GenerateToEnd();
            long vertices = window.Map.VertexCount;
            CombinedMap shown = window.Map;

            window.NextSeed();
            window.Tick(0);
            window.Cancel();
            Assert.IsFalse(window.IsGenerating);
            Assert.AreSame(shown, window.Map);
            Assert.AreEqual(vertices, window.Map.VertexCount);
            StringAssert.Contains("przerwana", window.Report);

            uint seed = window.Settings.Seed;
            Assert.AreEqual(2u, seed, "Następne ziarno");
            GenerateToEnd();
            Assert.IsNotNull(window.Map);
        }

        [Test]
        public void Niezgodne_Ustawienia_NieStartuje_I_MowiDlaczego()
        {
            window.Settings.Solver = SolverKind.SupportCount;
            window.Settings.Contradiction = ContradictionKind.Backtrack;
            window.Generate();
            Assert.IsFalse(window.IsGenerating);
            StringAssert.Contains("nie obsługuje", window.Report);

            window.Settings.Contradiction = ContradictionKind.Restart;
            window.Settings.Tileset = null;
            window.Generate();
            Assert.IsFalse(window.IsGenerating);
            StringAssert.Contains("zestaw", window.Report);
        }

        [Test]
        public void Wyczysc_I_Zamkniecie_ZwalniajaWszystko()
        {
            int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;

            GenerateToEnd();
            Assert.Greater(Resources.FindObjectsOfTypeAll<Mesh>().Length, meshes);
            window.ClearPreview();
            Assert.IsNull(window.Map);
            Assert.IsNull(window.PreviewRoot);
            Assert.AreEqual(meshes, Resources.FindObjectsOfTypeAll<Mesh>().Length, "siatki po Wyczyść");

            GenerateToEnd();
            GameObject preview = window.PreviewRoot;
            Object.DestroyImmediate(window);
            Assert.IsTrue(preview == null, "podgląd po zamknięciu okna");
            Assert.AreEqual(meshes, Resources.FindObjectsOfTypeAll<Mesh>().Length, "siatki po zamknięciu okna");
            Assert.AreEqual(textures, Resources.FindObjectsOfTypeAll<Texture2D>().Length, "paleta");
            Assert.AreEqual(materials, Resources.FindObjectsOfTypeAll<Material>().Length, "materiał atlasu");
        }

        [Test]
        public void ZmianaRysowania_PrzebudowujeTylkoScalanie_BezNowejGeneracji()
        {
            GenerateToEnd();
            int cubes = window.Map.InstanceCount;
            long vertices = window.Map.VertexCount;
            Assert.AreEqual(1, window.Map.MaterialCount);
            Assert.AreEqual(1, window.Map.RendererCount, "fragment 16 × 16 na siatkę 10 × 10");

            window.Settings.UseAtlas = false;
            window.RebuildShownMap();
            Assert.IsFalse(window.IsGenerating);
            Assert.Greater(window.Map.MaterialCount, 1);
            Assert.AreEqual(cubes, window.Map.InstanceCount, "ta sama mapa");
            Assert.AreEqual(vertices, window.Map.VertexCount, "ta sama mapa");

            window.Settings.ChunkSize = 4;
            window.RebuildShownMap();
            Assert.AreEqual(9, window.Map.RendererCount, "3 × 3 fragmenty");
            Assert.AreEqual(9, window.PreviewRoot.transform.childCount, "po poprzednim scalaniu nic nie zostało");
            Assert.AreEqual(cubes, window.Map.InstanceCount, "ta sama mapa");
        }

        [Test]
        public void Zapis_Atlas_PrefabSiatkiMaterialIPaleta()
        {
            GenerateToEnd();
            CombinedMap map = window.Map;
            GameObject prefab = window.SaveAsPrefab();
            Assert.IsNotNull(prefab, window.Report);
            string path = AssetDatabase.GetAssetPath(prefab);
            StringAssert.StartsWith($"{TestFolder}/Zamek_Pelny_10x10x8_ziarno1", path);
            StringAssert.Contains(path, window.Report);
            Assert.Greater(MapBaker.MeshesFileSize(prefab), map.MeshBytes, "plik tekstowy większy niż dane w pamięci");

            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>();
            Assert.AreEqual(map.RendererCount, renderers.Length);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path.Replace(".prefab", "_Material.mat"));
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace(".prefab", "_Paleta.asset"));
            Assert.IsNotNull(material);
            Assert.IsNotNull(palette);
            Assert.AreEqual(palette, material.GetTexture("_BaseMap"));
            Assert.AreEqual(FilterMode.Point, palette.filterMode);
            Assert.AreEqual(map.Atlas.Texture.GetPixels32(), palette.GetPixels32());

            long vertices = 0;
            foreach (MeshRenderer renderer in renderers)
            {
                Assert.AreSame(material, renderer.sharedMaterial);
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsTrue(AssetDatabase.Contains(mesh), $"{mesh.name}: siatka musi być zasobem, inaczej prefab ją zgubi");
                Assert.IsFalse(mesh.isReadable, $"{mesh.name}: bez kopii w pamięci procesora");
                Assert.AreEqual(CombinedMap.BytesPerVertex, mesh.GetVertexBufferStride(0));
                vertices += mesh.vertexCount;
            }
            Assert.AreEqual(map.VertexCount, vertices);
            Assert.AreEqual(map, window.Map, "podgląd zostaje");

            GameObject second = window.SaveAsPrefab();
            Assert.AreNotEqual(path, AssetDatabase.GetAssetPath(second), "drugi zapis nie nadpisuje pierwszego");
        }

        [Test]
        public void Zapis_BezAtlasu_MaterialyZModeli_BezPalety()
        {
            window.Settings.UseAtlas = false;
            GenerateToEnd();
            GameObject prefab = window.SaveAsPrefab();
            string path = AssetDatabase.GetAssetPath(prefab);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace(".prefab", "_Paleta.asset")));
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                Assert.AreEqual(renderer.GetComponent<MeshFilter>().sharedMesh.subMeshCount, renderer.sharedMaterials.Length);
                foreach (Material material in renderer.sharedMaterials)
                    StringAssert.StartsWith(WfcPaths.Materials, AssetDatabase.GetAssetPath(material));
            }
        }

        [Test]
        public void Zapis_I_Wstawienie_ZastepujePodglad_MoznaCofnac()
        {
            GenerateToEnd();
            GameObject prefab = window.SaveAsPrefab(true);
            Assert.IsNotNull(prefab);
            Assert.IsNull(window.Map, "wstawiony prefab zastępuje podgląd");
            GameObject instance = Selection.activeGameObject;
            Assert.IsNotNull(instance);
            Assert.AreSame(prefab, PrefabUtility.GetCorrespondingObjectFromSource(instance));
            StringAssert.Contains("Wstawiono do sceny", window.Report);
            Undo.PerformUndo();
            Assert.IsTrue(instance == null, "Cofnij usuwa wstawiony prefab");
        }

        [Test]
        public void Zapis_BezMapy_NicNieRobi_MapaBezKopiiSiatek_Blad()
        {
            Assert.IsNull(window.SaveAsPrefab());
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestFolder));

            GenerationResult result = Solve(out CompiledRules rules);
            var root = new GameObject("Mapa testowa");
            try
            {
                using (var map = new CombinedMap(root.transform, 16, true))
                {
                    map.Build(window.Settings.Tileset, rules, result.Grid, result.States, 1);
                    Assert.Throws<System.InvalidOperationException>(() => MapBaker.Bake(map, TestFolder, "Zamek"));
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestFolder), "błąd nie zostawia plików");
        }

        GenerationResult Solve(out CompiledRules rules)
        {
            rules = RuleCompiler.Compile(window.Settings.Tileset.ToTilesetData());
            var solver = new BitsetSolver();
            solver.Initialize(rules, new int3(10, 8, 10), 1, new SolverSettings { ContradictionHandler = new BacktrackHandler() });
            return solver.Run();
        }
    }
}
