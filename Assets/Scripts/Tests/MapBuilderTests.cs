using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>Stawianie mapy: zwykłe obiekty dla małych map i instancje na karcie graficznej dla dużych.</summary>
    public sealed class MapBuilderTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object item in created)
                if (item != null)
                    Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void Domyslnie_ObiektyWScenie_InstancjeTylkoWTrybiePlay()
        {
            var root = new GameObject("Mapa testowa");
            created.Add(root);
            var builder = root.AddComponent<MapBuilder>();
            Assert.AreEqual(MapDisplay.GameObjects, builder.Display);
            builder.Display = MapDisplay.Instanced;
            Assert.IsFalse(builder.UsesInstancing, "poza trybem Play nikt nie wywoła rysowania instancji co klatkę");
        }

        [Test]
        public void Instancje_TeSameSiatkiMaterialyIPolozeniaCoObiektyWScenie()
        {
            var tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset("Pelny"));
            CompiledRules rules = RuleCompiler.Compile(tileset.ToTilesetData());
            var size = new int3(10, 8, 10);
            var solver = new BitsetSolver();
            solver.Initialize(rules, size, 1, new SolverSettings());
            GenerationResult result = solver.Run();
            Assert.IsTrue(result.Success);

            var root = new GameObject("Mapa testowa");
            created.Add(root);
            root.transform.position = new Vector3(5f, 0f, -3f);
            var builder = root.AddComponent<MapBuilder>();
            builder.MapRoot = root.transform;
            builder.Show(tileset, rules, result.Grid, result.States, 7);
            Assert.IsFalse(builder.IsInstanced, "poza trybem Play zawsze obiekty");

            var fromObjects = new List<string>();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    fromObjects.Add(Key(mesh, renderer.sharedMaterials[Mathf.Min(submesh, renderer.sharedMaterials.Length - 1)], renderer.transform.localToWorldMatrix));
            }

            var map = new InstancedMap();
            map.Build(tileset, rules, result.Grid, result.States, 7, root.transform.localToWorldMatrix);
            List<string> fromInstances = map.Instances().Select(part => Key(part.Mesh, part.Material, part.Matrix)).ToList();

            Assert.AreEqual(builder.InstanceCount, map.InstanceCount, "kostki");
            Assert.Greater(fromObjects.Count, builder.InstanceCount, "co najmniej jedna część na kostkę");
            CollectionAssert.AreEquivalent(fromObjects, fromInstances);
            Assert.Less(map.DrawCalls, 200, "wywołania rysowania: po jednym na siatkę i materiał, nie na kostkę");
        }

        [Test]
        public void Materialy_ZWlaczonymInstancjonowaniem()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { WfcPaths.Materials });
            Assert.AreEqual(12, guids.Length);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Material>(path).enableInstancing, $"{path}: włącz „Enable GPU Instancing” albo uruchom WFC → Importuj reguły i modele.");
            }
        }

        [Test]
        public void Kamera_FrameZostawiaKatyIPrzenosiPunktObrotu()
        {
            var cameraObject = new GameObject("Kamera testowa");
            created.Add(cameraObject);
            var flyCamera = cameraObject.AddComponent<FlyCamera>();
            flyCamera.LookAt(new Vector3(30f, 6f, 30f), 95f, 30f, 40f);
            Quaternion rotation = cameraObject.transform.rotation;

            var target = new Vector3(120f, 24f, 120f);
            flyCamera.Frame(target, 380f);
            Assert.Less(Quaternion.Angle(rotation, cameraObject.transform.rotation), 0.01f, "te same kąty");
            Assert.AreEqual(380f, Vector3.Distance(cameraObject.transform.position, target), 1e-2f);
            Assert.Less(Vector3.Distance(target, flyCamera.Pivot), 1e-2f);
        }

        /// <summary>Siatka, materiał i położenie z dokładnością do 1 mm, do porównania zbiorów części.</summary>
        static string Key(Mesh mesh, Material material, Matrix4x4 matrix)
        {
            Vector3 position = matrix.GetColumn(3);
            Vector3 forward = matrix.GetColumn(2);
            Vector3 up = matrix.GetColumn(1);
            return $"{mesh.name}|{material.name}|{Round(position)}|{Round(forward)}|{Round(up)}";
        }

        // + 0f zamienia −0 na 0, żeby drobne różnice zaokrągleń nie dawały innego napisu.
        static string Round(Vector3 value) => $"{Mathf.Round(value.x * 1000f) + 0f},{Mathf.Round(value.y * 1000f) + 0f},{Mathf.Round(value.z * 1000f) + 0f}";
    }
}
