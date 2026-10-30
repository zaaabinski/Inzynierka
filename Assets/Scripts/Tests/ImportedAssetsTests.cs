using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>
    /// Zasoby po imporcie (WFC → Importuj reguły i modele): 50 TileDefinition, 4 TilesetDefinition, 43 prefaby,
    /// 12 materiałów. Zestawy z zasobów muszą dać po kompilacji dokładnie to samo co plik JSON.
    /// </summary>
    public sealed class ImportedAssetsTests
    {
        const string ImportHint = "Uruchom WFC → Importuj reguły i modele.";

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"Brak zasobu {path}. {ImportHint}");
            return asset;
        }

        static string[] Find(string filter, string folder) =>
            AssetDatabase.FindAssets(filter, new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).ToArray();

        [Test]
        public void Kafelki_ZasobNaKazdyKafelekZPliku()
        {
            Assert.AreEqual(TestData.Rules.Tiles.Count, Find("t:TileDefinition", WfcPaths.Tiles).Length, ImportHint);
            foreach (TileData tile in TestData.Rules.Tiles)
            {
                TileDefinition asset = Load<TileDefinition>(WfcPaths.Tile(tile.Name));
                TileData data = asset.ToTileData();
                Assert.AreEqual(tile.Name, data.Name);
                Assert.AreEqual(tile.Weight, data.Weight, tile.Name);
                Assert.AreEqual(tile.Rotations, data.Rotations, tile.Name);
                CollectionAssert.AreEqual(tile.Labels, data.Labels, tile.Name);
            }
        }

        [Test]
        public void Kafelki_PrefabyPodpietePoNazwieModelu()
        {
            int withPrefab = 0;
            foreach (TileData tile in TestData.Rules.Tiles)
            {
                TileDefinition asset = Load<TileDefinition>(WfcPaths.Tile(tile.Name));
                if (tile.HasModel && tile.ModelAvailable)
                {
                    Assert.IsNotNull(asset.Prefab, $"Kafelek {tile.Name} nie ma prefabu {tile.Model}.");
                    Assert.AreEqual(tile.Model, asset.Prefab.name, tile.Name);
                    withPrefab++;
                }
                else if (tile.HasModel)
                {
                    // Brak modelu: zastępczy prefab kafelka o tych samych etykietach (RulesImporter.FindPrefab).
                    Assert.IsNotNull(asset.Prefab, $"Kafelek {tile.Name} bez modelu powinien dostać prefab zastępczy.");
                    withPrefab++;
                }
                else
                {
                    Assert.IsNull(asset.Prefab, $"Kafelek niewidoczny {tile.Name} nie powinien mieć prefabu.");
                }
            }
            Assert.AreEqual("Dach_Szczyt_Choragiew", Load<TileDefinition>(WfcPaths.Tile("Dach_Szczyt_Choragiew")).Prefab.name);
            // 3 kafelki bez prefabu: Powietrze, Powietrze_Nad_Schodami i Donzon_Wnetrze (niewidoczne).
            Assert.AreEqual(47, withPrefab);
        }

        [Test]
        public void Prefaby_43_ModelObroconyO180Stopni()
        {
            string[] prefabs = Find("t:Prefab", WfcPaths.TilePrefabs);
            Assert.AreEqual(43, prefabs.Length, ImportHint);
            foreach (string path in prefabs)
            {
                var prefab = Load<GameObject>(path);
                Assert.AreEqual(Vector3.zero, prefab.transform.localPosition, path);
                Assert.AreEqual(Quaternion.identity, prefab.transform.localRotation, $"{path}: root bez obrotu");
                Transform model = prefab.transform.Find("Model");
                Assert.IsNotNull(model, $"{path}: brak dziecka Model");
                Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, UnityGrid.ModelBaseYaw, 0f), model.localRotation), 0.01f, $"{path}: Model obrócony o 180° wokół Y");
                Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), $"{path}: bez colliderów");
            }
        }

        [Test]
        public void DachSzczytChoragiew_WPoziomieWKostce_WGoreNajwyzej055MetraNadKostke()
        {
            // Nad szczytem dachu zawsze stoi Powietrze, ale na nim może stanąć most, którego łuk schodzi 2,4 m w dół,
            // czyli do 0,6 m nad spód tej kostki. Maszt z chorągwią musi się zmieścić poniżej.
            var prefab = Load<GameObject>(WfcPaths.TilePrefab("Dach_Szczyt_Choragiew"));
            foreach (int rotation in new[] { 0, 1, 2, 3 })
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    instance.transform.rotation = UnityGrid.TileRotation(rotation);
                    var bounds = new Bounds();
                    bool first = true;
                    foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                    {
                        if (first)
                            bounds = renderer.bounds;
                        else
                            bounds.Encapsulate(renderer.bounds);
                        first = false;
                    }
                    Assert.LessOrEqual(Mathf.Max(-bounds.min.x, bounds.max.x, -bounds.min.z, bounds.max.z), 1.5f + 1e-3f, $"obrót {rotation}: wystaje w bok");
                    Assert.LessOrEqual(bounds.max.y, 3.55f + 1e-3f, $"obrót {rotation}: za wysoko");
                    Assert.Greater(bounds.max.y, 3.0f, "maszt powinien wystawać ponad kostkę");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void Materialy_12_WspolneDlaWszystkichModeli()
        {
            string[] materials = Find("t:Material", WfcPaths.Materials);
            Assert.AreEqual(12, materials.Length, ImportHint);
            foreach (string path in Find("t:Prefab", WfcPaths.TilePrefabs))
                foreach (Renderer renderer in Load<GameObject>(path).GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        string materialPath = AssetDatabase.GetAssetPath(material);
                        Assert.IsTrue(materialPath.StartsWith(WfcPaths.Materials + "/"), $"{path}: materiał {material.name} jest w {materialPath}");
                    }
        }

        [TestCase("Prosty")]
        [TestCase("Sredni")]
        [TestCase("PelnyBezDekoracji")]
        [TestCase("Pelny")]
        [TestCase("PelnyBezWolnegoPola")]
        [TestCase("PelnyBezDrzwiNadSchodami")]
        [TestCase("PelnyTrawa040")]
        [TestCase("PelnyTrawa025")]
        public void Zestaw_ZZasobuTeSameRegulyCoZPlikuJson(string setName)
        {
            TilesetDefinition asset = Load<TilesetDefinition>(WfcPaths.Tileset(setName));
            RuleVariant variant = RuleVariants.Find(setName);
            Assert.AreEqual(TestData.Rules.Control.Sets[variant?.BaseSet ?? setName].Tiles, asset.Tiles.Count, "kafelki");
            if (variant != null)
            {
                Assert.AreEqual(variant.Description, asset.Description, "opis wariantu");
                CollectionAssert.AreEqual(variant.DisabledChecks, asset.DisabledChecks, "wyłączone sprawdzenia");
            }
            Assert.AreEqual(TestData.Rules.Version, asset.RulesVersion, "wersja pliku reguł");

            CompiledRules fromAsset = RuleCompiler.Compile(asset.ToTilesetData());
            RulesAssert.AreEqual(TestData.Compile(setName), fromAsset);
            Assert.IsTrue(TilesetValidator.Validate(fromAsset, TestData.Floors).IsValid);
        }

        [Test]
        public void Scena_TilesetPreview()
        {
            Assert.IsTrue(File.Exists(TestData.ProjectPath(WfcPaths.TilesetPreviewScene)), $"Brak sceny {WfcPaths.TilesetPreviewScene}. Uruchom WFC → Utwórz scenę TilesetPreview.");
        }

        [TestCase("E1", 4)]
        [TestCase("E2", 5)]
        [TestCase("E3", 12)]
        [TestCase("E4", 16)]
        [TestCase("E5", 4)]
        [TestCase("E6", 5)]
        public void Eksperyment_ZasobZWariantami(string experimentName, int variants)
        {
            BenchmarkConfig config = Load<BenchmarkConfig>(WfcPaths.Experiment(experimentName));
            Assert.AreEqual(experimentName, config.ExperimentName);
            Assert.AreEqual(variants, config.Variants().Count, config.Summary());
            Assert.AreEqual(100, config.SeedCount);
            Assert.AreEqual(5, config.WarmupRuns);
            Assert.IsTrue(config.Variants().All(variant => variant.Tileset != null));
        }

        [Test]
        public void Scena_Generator_OknoMaUstawieniaPanelu()
        {
            string scene = File.ReadAllText(TestData.ProjectPath(WfcPaths.GeneratorScene));
            string guid = AssetDatabase.AssetPathToGUID(WfcPaths.UiSettings + "/PanelSettings.asset");
            Assert.IsNotEmpty(guid, "Brak Assets/Settings/UI/PanelSettings.asset. Uruchom WFC → Utwórz scenę Generator.");
            StringAssert.Contains($"m_PanelSettings: {{fileID: 11400000, guid: {guid}, type: 2}}", scene, "UIDocument w scenie Generator bez ustawień panelu: okno się nie narysuje.");
            StringAssert.Contains("PanelGeneratora", scene);
            StringAssert.Contains("BenchmarkRunner", scene);
        }

        [Test]
        public void Scena_Generator()
        {
            const string path = WfcPaths.Scenes + "/Generator.unity";
            Assert.IsTrue(File.Exists(TestData.ProjectPath(path)), $"Brak sceny {path}. Uruchom WFC → Utwórz scenę Generator.");
        }
    }
}
