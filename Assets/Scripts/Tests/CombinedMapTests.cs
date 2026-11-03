using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>Scalanie mapy w statyczne siatki (CombinedMap) i atlas materiałów (MaterialAtlas).</summary>
    public sealed class CombinedMapTests
    {
        static readonly int3 Small = new int3(10, 8, 10);

        readonly List<Object> created = new List<Object>();
        readonly List<CombinedMap> maps = new List<CombinedMap>();

        [TearDown]
        public void Cleanup()
        {
            foreach (CombinedMap map in maps)
                map.Dispose();
            maps.Clear();
            foreach (Object item in created)
                if (item != null)
                    Object.DestroyImmediate(item);
            created.Clear();
        }

        static TilesetDefinition Tileset => AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset("Pelny"));

        static CompiledRules Rules => TestData.Compile("Pelny");

        static int[] Generate(int3 size, uint seed = 1)
        {
            var solver = new BitsetSolver();
            solver.Initialize(Rules, size, seed, new SolverSettings { ContradictionHandler = new BacktrackHandler() });
            GenerationResult result = solver.Run();
            Assert.IsTrue(result.Success);
            return result.States;
        }

        Transform Root()
        {
            var root = new GameObject("Mapa testowa");
            created.Add(root);
            return root.transform;
        }

        CombinedMap Map(int chunkSize, bool atlas, bool keepReadable = true)
        {
            var map = new CombinedMap(Root(), chunkSize, atlas, keepReadable);
            maps.Add(map);
            return map;
        }

        /// <summary>Oczekiwane wierzchołki i środki trójkątów, liczone po kolei z części kafelków, tak jak stawia je MapBuilder.</summary>
        static void Expected(int3 size, int[] states, uint seed, Dictionary<Material, int> verticesPerMaterial, out List<Vector3> vertices, out List<Vector3> centers)
        {
            var library = new TilePartLibrary();
            var grid = new GridShape(size);
            vertices = new List<Vector3>();
            centers = new List<Vector3>();
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int state = states[cell];
                TileDefinition tile = Tileset.Tiles[Rules.GetTileIndex(state)];
                if (tile.Prefab == null)
                    continue;
                int3 position = grid.Coordinates(cell);
                Matrix4x4 root = Matrix4x4.TRS(UnityGrid.CellPosition(position.x, position.y, position.z),
                    UnityGrid.TileRotation(MapBuilder.ModelRotation(tile, Rules, state, seed, cell)), Vector3.one);
                foreach (TilePart part in library.PartsOf(tile.Prefab))
                {
                    Matrix4x4 matrix = root * part.Local;
                    PartGeometry geometry = library.Geometry[part.Id];
                    for (int v = 0; v < geometry.VertexCount; v++)
                        vertices.Add(matrix.MultiplyPoint3x4(library.Positions[geometry.VertexStart + v]));
                    for (int i = 0; i < geometry.IndexCount; i += 3)
                    {
                        Vector3 a = library.Positions[geometry.VertexStart + library.Indices[geometry.IndexStart + i]];
                        Vector3 b = library.Positions[geometry.VertexStart + library.Indices[geometry.IndexStart + i + 1]];
                        Vector3 c = library.Positions[geometry.VertexStart + library.Indices[geometry.IndexStart + i + 2]];
                        centers.Add(matrix.MultiplyPoint3x4((a + b + c) / 3f));
                    }
                    if (verticesPerMaterial != null)
                    {
                        verticesPerMaterial.TryGetValue(part.Material, out int count);
                        verticesPerMaterial[part.Material] = count + geometry.VertexCount;
                    }
                }
            }
        }

        /// <summary>Te same punkty w obu listach, w dowolnej kolejności, z dokładnością do 1 mm.</summary>
        static void AssertSamePoints(List<Vector3> expected, List<Vector3> actual, string what)
        {
            Assert.AreEqual(expected.Count, actual.Count, what);
            var counts = new Dictionary<Vector3Int, int>();
            foreach (Vector3 point in actual)
            {
                Vector3Int key = Vector3Int.RoundToInt(point * 1000f);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }
            foreach (Vector3 point in expected)
            {
                Vector3Int center = Vector3Int.RoundToInt(point * 1000f);
                bool found = false;
                for (int x = -1; x <= 1 && !found; x++)
                    for (int y = -1; y <= 1 && !found; y++)
                        for (int z = -1; z <= 1 && !found; z++)
                        {
                            Vector3Int key = center + new Vector3Int(x, y, z);
                            if (counts.TryGetValue(key, out int count) && count > 0)
                            {
                                counts[key] = count - 1;
                                found = true;
                            }
                        }
                Assert.IsTrue(found, $"{what}: brak punktu {point}");
            }
        }

        static void Actual(CombinedMap map, out List<Vector3> vertices, out List<Vector3> centers)
        {
            vertices = new List<Vector3>();
            centers = new List<Vector3>();
            foreach (Mesh mesh in map.Meshes)
            {
                Vector3[] meshVertices = mesh.vertices;
                vertices.AddRange(meshVertices);
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int[] indices = mesh.GetIndices(submesh);
                    for (int i = 0; i < indices.Length; i += 3)
                        centers.Add((meshVertices[indices[i]] + meshVertices[indices[i + 1]] + meshVertices[indices[i + 2]]) / 3f);
                }
            }
        }

        [Test]
        public void Modele_OdczytWlaczony_BezOdbicLustrzanych()
        {
            var library = new TilePartLibrary();
            foreach (TileDefinition tile in Tileset.Tiles)
            {
                if (tile.Prefab == null)
                    continue;
                foreach (TilePart part in library.PartsOf(tile.Prefab))
                {
                    Assert.IsTrue(part.Mesh.isReadable, part.Mesh.name);
                    Assert.Greater(part.Local.determinant, 0f, $"{part.Mesh.name}: odbicie odwróciłoby trójkąty po scaleniu");
                    Assert.AreEqual(part.Mesh.vertexCount, part.Mesh.normals.Length, $"{part.Mesh.name}: normalne");
                    Assert.AreEqual(ShadowCastingMode.On, part.ShadowCasting, part.Mesh.name);
                    Assert.IsTrue(part.ReceiveShadows, part.Mesh.name);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Scalona_TeSameWierzcholkiITrojkatyCoKostki(bool atlas)
        {
            int[] states = Generate(Small);
            CombinedMap map = Map(4, atlas);
            map.Build(Tileset, Rules, new GridShape(Small), states, 7);

            Expected(Small, states, 7, null, out List<Vector3> expectedVertices, out List<Vector3> expectedCenters);
            Actual(map, out List<Vector3> vertices, out List<Vector3> centers);
            AssertSamePoints(expectedVertices, vertices, "wierzchołki");
            AssertSamePoints(expectedCenters, centers, "trójkąty");
            Assert.AreEqual(expectedVertices.Count, map.VertexCount);
            Assert.AreEqual(expectedCenters.Count, map.TriangleCount);
        }

        [Test]
        public void Scalona_NormalneZgodneZTrojkatami()
        {
            CombinedMap map = Map(16, true);
            map.Build(Tileset, Rules, new GridShape(Small), Generate(Small), 1);
            int checkedTriangles = 0;
            foreach (Mesh mesh in map.Meshes)
            {
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                int[] indices = mesh.GetIndices(0);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 face = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]);
                    if (face.sqrMagnitude < 1e-10f)
                        continue;
                    Vector3 average = normals[indices[i]] + normals[indices[i + 1]] + normals[indices[i + 2]];
                    Assert.Greater(Vector3.Dot(face, average), 0f, $"trójkąt {i / 3} w {mesh.name}: odwrócony albo zła normalna");
                    Assert.AreEqual(1f, normals[indices[i]].magnitude, 0.001f, "normalna długości 1");
                    checkedTriangles++;
                }
            }
            Assert.Greater(checkedTriangles, 1000);
        }

        [Test]
        public void BezAtlasu_PodsiatkaNaMaterial_Materialy12ZModeli()
        {
            CombinedMap map = Map(16, false);
            map.Build(Tileset, Rules, new GridShape(Small), Generate(Small), 1);
            Assert.IsNull(map.Atlas);
            int submeshes = 0;
            foreach (MeshRenderer renderer in map.Renderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(mesh.subMeshCount, renderer.sharedMaterials.Length);
                Assert.AreEqual(mesh.subMeshCount, renderer.sharedMaterials.Distinct().Count(), "każdy materiał raz");
                Assert.LessOrEqual(mesh.subMeshCount, 12);
                submeshes += mesh.subMeshCount;
            }
            Assert.AreEqual(submeshes, map.DrawCallCount);
            Assert.Greater(map.MaterialCount, 1);
        }

        [Test]
        public void Atlas_JedenMaterial_KolorKazdegoWierzcholkaZPalety()
        {
            int[] states = Generate(Small);
            CombinedMap map = Map(4, true);
            map.Build(Tileset, Rules, new GridShape(Small), states, 1);

            Assert.IsEmpty(map.Atlas.Differences, "materiały zestawu Pelny różnią się tylko kolorem");
            Assert.AreEqual(1, map.MaterialCount);
            Assert.AreEqual(map.RendererCount, map.DrawCallCount, "jedno wywołanie rysowania na fragment");
            var perColor = new Dictionary<Color32, int>();
            foreach (MeshRenderer renderer in map.Renderers)
            {
                Assert.AreEqual(1, renderer.sharedMaterials.Length);
                Assert.AreSame(map.Atlas.Material, renderer.sharedMaterial);
                foreach (Vector2 uv in renderer.GetComponent<MeshFilter>().sharedMesh.uv)
                {
                    Color32 color = map.Atlas.ColorAt(uv);
                    perColor.TryGetValue(color, out int count);
                    perColor[color] = count + 1;
                }
            }

            var perMaterial = new Dictionary<Material, int>();
            Expected(Small, states, 1, perMaterial, out _, out _);
            foreach (KeyValuePair<Material, int> pair in perMaterial)
            {
                Color32 color = pair.Key.GetColor("_BaseColor");
                Assert.IsTrue(perColor.TryGetValue(color, out int count), $"brak koloru {pair.Key.name} w palecie");
                Assert.AreEqual(pair.Value, count, $"wierzchołki w kolorze {pair.Key.name}");
            }
        }

        [Test]
        public void Atlas_MaterialTenSamShaderIWlasciwosci()
        {
            var library = new TilePartLibrary();
            var materials = new HashSet<Material>();
            foreach (TileDefinition tile in Tileset.Tiles)
                if (tile.Prefab != null)
                    foreach (TilePart part in library.PartsOf(tile.Prefab))
                        materials.Add(part.Material);
            using (MaterialAtlas atlas = MaterialAtlas.Build(materials))
            {
                Assert.AreEqual(12, atlas.Materials.Count);
                Assert.AreEqual(4, atlas.Texture.width, "12 kolorów w palecie 4 × 4");
                Assert.AreEqual(FilterMode.Point, atlas.Texture.filterMode);
                Assert.IsTrue(atlas.Material.enableInstancing);
                Assert.AreEqual(atlas.Texture, atlas.Material.GetTexture("_BaseMap"));
                Assert.AreEqual(Color.white, atlas.Material.GetColor("_BaseColor"));
                Material stone = materials.First(material => material.name == "M_Kamien");
                Assert.AreEqual(stone.shader, atlas.Material.shader);
                Assert.AreEqual(stone.GetFloat("_Smoothness"), atlas.Material.GetFloat("_Smoothness"));
            }
        }

        [Test]
        public void Przebudowa_TylkoFragmentyZeZmienionymiKomorkami()
        {
            int[] states = Generate(Small);
            CombinedMap map = Map(4, true);
            var grid = new GridShape(Small);
            map.Build(Tileset, Rules, grid, states, 1);
            Assert.AreEqual(9, map.RebuiltChunks, "pierwsza mapa: 3 × 3 fragmenty");

            map.Build(Tileset, Rules, grid, states, 1);
            Assert.AreEqual(0, map.RebuiltChunks, "ta sama mapa");

            var changed = (int[])states.Clone();
            changed[grid.Index(1, 3, 1)] = changed[grid.Index(1, 3, 1)] == 0 ? 1 : 0;
            map.Build(Tileset, Rules, grid, changed, 1);
            Assert.AreEqual(1, map.RebuiltChunks, "jedna komórka");

            map.Build(Tileset, Rules, grid, changed, 2);
            Assert.AreEqual(9, map.RebuiltChunks, "inne ziarno to inne losowe obroty modeli");
        }

        [Test]
        public void KomorkiNieustalone_NicNieStawia_PustaMapaNieRysuje()
        {
            var grid = new GridShape(Small);
            var empty = new int[grid.CellCount];
            System.Array.Fill(empty, -1);
            CombinedMap map = Map(4, true);
            map.Build(Tileset, Rules, grid, empty, 1);
            Assert.AreEqual(0, map.InstanceCount);
            Assert.AreEqual(0, map.RendererCount);
            Assert.IsEmpty(map.Meshes);

            map.Build(Tileset, Rules, grid, Generate(Small), 1);
            Assert.Greater(map.RendererCount, 0);
        }

        [Test]
        public void Pamiec_PowtorzoneMapyNieZostawiajaSiatek_DisposeSprzataWszystko()
        {
            int meshesBefore = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int texturesBefore = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            int materialsBefore = Resources.FindObjectsOfTypeAll<Material>().Length;
            var map = new CombinedMap(Root(), 4, true, true);
            var grid = new GridShape(Small);
            map.Build(Tileset, Rules, grid, Generate(Small, 1), 1);
            int meshesAfterFirst = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            Assert.AreEqual(meshesBefore + map.RendererCount, meshesAfterFirst);

            for (uint seed = 2; seed <= 6; seed++)
                map.Build(Tileset, Rules, grid, Generate(Small, seed), seed);
            Assert.AreEqual(meshesBefore + map.RendererCount, Resources.FindObjectsOfTypeAll<Mesh>().Length, "liczba siatek po 5 kolejnych mapach");

            map.Dispose();
            Assert.AreEqual(meshesBefore, Resources.FindObjectsOfTypeAll<Mesh>().Length, "siatki");
            Assert.AreEqual(texturesBefore, Resources.FindObjectsOfTypeAll<Texture2D>().Length, "paleta");
            Assert.AreEqual(materialsBefore, Resources.FindObjectsOfTypeAll<Material>().Length, "materiał atlasu");
        }

        [Test]
        public void DuzyFragment_IndeksySzesnastoIndwudziestobitowe()
        {
            var size = new int3(40, 13, 40);
            CombinedMap map = Map(40, true);
            map.Build(Tileset, Rules, new GridShape(size), Generate(size), 1);
            Mesh big = map.Meshes.Single();
            Assert.Greater(big.vertexCount, ushort.MaxValue);
            Assert.AreEqual(IndexFormat.UInt32, big.indexFormat);
            Assert.Less(big.GetIndices(0).Max(), big.vertexCount);

            CombinedMap small = Map(8, true);
            small.Build(Tileset, Rules, new GridShape(Small), Generate(Small), 1);
            Assert.IsTrue(small.Meshes.All(mesh => mesh.indexFormat == IndexFormat.UInt16));
        }

        [Test]
        public void Niepolecany_OdczytWylaczony_ZwalniaKopieWPamieci()
        {
            CombinedMap map = Map(16, true, false);
            map.Build(Tileset, Rules, new GridShape(Small), Generate(Small), 1);
            Assert.IsTrue(map.Meshes.All(mesh => !mesh.isReadable));
        }

        [Test]
        public void MapBuilder_SposobyRysowania_TaSamaLiczbaKostek_ZmianaSprzataPoprzedni()
        {
            int[] states = Generate(Small);
            var grid = new GridShape(Small);
            var root = new GameObject("Mapa testowa");
            created.Add(root);
            var builder = root.AddComponent<MapBuilder>();
            builder.MapRoot = root.transform;

            builder.Display = MapDisplay.GameObjects;
            builder.Show(Tileset, Rules, grid, states, 1);
            int cubes = builder.InstanceCount;
            int objectRenderers = root.GetComponentsInChildren<MeshRenderer>().Length;
            Assert.AreEqual(cubes, objectRenderers);
            Assert.IsNull(builder.Combined);

            builder.Display = MapDisplay.CombinedAtlas;
            builder.Show(Tileset, Rules, grid, states, 1);
            Assert.AreEqual(cubes, builder.InstanceCount);
            Assert.AreEqual(builder.Combined.RendererCount, root.GetComponentsInChildren<MeshRenderer>().Length, "po kostkach nie został żaden obiekt");
            Assert.AreEqual(1, builder.Combined.RendererCount, "jeden fragment 16 × 16 na siatkę 10 × 10");
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
                Assert.AreEqual(HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild, renderer.gameObject.hideFlags, "poza trybem Play tylko podgląd");

            builder.Display = MapDisplay.Combined;
            builder.Show(Tileset, Rules, grid, states, 1);
            Assert.Greater(builder.Combined.DrawCallCount, 1);

            builder.Display = MapDisplay.GameObjects;
            builder.Show(Tileset, Rules, grid, states, 1);
            Assert.IsNull(builder.Combined);
            Assert.AreEqual(cubes, root.GetComponentsInChildren<MeshRenderer>().Length);

            builder.Clear();
            Assert.AreEqual(0, root.transform.childCount);
        }
    }
}
