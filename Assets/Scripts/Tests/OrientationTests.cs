using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>
    /// Test na start z rozdziału 6: Mur_Dol w obrocie 0 ma cokół (stronę zewnętrzną) od −Z, czyli od S,
    /// a pas bruku od +Z, czyli od N. W Blenderze bruk (M_Bruk) leży przy y od 0,75 do 1,5 m (N), a cokół sięga
    /// y = −1,5 m (S). Po eksporcie i imporcie surowy model ma N na −Z, więc prefab obraca go o 180°.
    /// Mur_Dol jest symetryczny względem osi X, dlatego odbicie lustrzane E–W sprawdza Mur_Naroznik_Srodek
    /// (ramiona E i N, w Blenderze x i y od −0,75 do 1,5 m).
    /// </summary>
    public sealed class OrientationTests
    {
        const float Tolerance = 0.01f;
        readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void DestroySpawned()
        {
            foreach (GameObject instance in spawned)
                if (instance != null)
                    Object.DestroyImmediate(instance);
            spawned.Clear();
        }

        [Test]
        public void MurDol_SurowyModel_BrukOdMinusZ_CokolOdPlusZ()
        {
            GameObject model = SpawnModel("02_Mury/Mur_Dol.fbx");
            Bounds bruk = MaterialBounds(model, "M_Bruk");
            Bounds kamien = MaterialBounds(model, "M_Kamien");

            AssertRange(bruk.min.z, bruk.max.z, -1.5f, -0.75f, "Pas bruku surowego modelu Mur_Dol (oś Z)");
            AssertRange(kamien.min.z, kamien.max.z, -0.75f, 1.5f, "Kamień z cokołem surowego modelu Mur_Dol (oś Z)");
        }

        [Test]
        public void MurDol_Prefab_Obrot0_CokolOdS_BrukOdN()
        {
            GameObject tile = SpawnPrefab("Mur_Dol", 0);
            Bounds bruk = MaterialBounds(tile, "M_Bruk");
            Bounds kamien = MaterialBounds(tile, "M_Kamien");

            AssertRange(bruk.min.z, bruk.max.z, 0.75f, 1.5f, "Pas bruku Mur_Dol w obrocie 0 (oś Z, N = +Z)");
            AssertRange(bruk.min.y, bruk.max.y, 0f, 0.25f, "Pas bruku Mur_Dol (wysokość)");
            AssertRange(kamien.min.z, kamien.max.z, -1.5f, 0.75f, "Kamień z cokołem Mur_Dol w obrocie 0 (oś Z, S = −Z)");
        }

        [Test]
        public void MurDol_Prefab_PunktObrotuWSrodkuPodstawy()
        {
            Bounds bounds = MeshBounds(SpawnPrefab("Mur_Dol", 0));

            AssertRange(bounds.min.x, bounds.max.x, -1.5f, 1.5f, "Mur_Dol (oś X)");
            AssertRange(bounds.min.y, bounds.max.y, 0f, 3f, "Mur_Dol (oś Y)");
            AssertRange(bounds.min.z, bounds.max.z, -1.5f, 1.5f, "Mur_Dol (oś Z)");
        }

        [Test]
        public void MurDol_Prefab_Obrot1_BrukOdW_CokolOdE()
        {
            // Obrót 1 przenosi N na W i S na E: Quaternion.Euler(0, −90, 0) na roocie prefabu.
            GameObject tile = SpawnPrefab("Mur_Dol", 1);
            Bounds bruk = MaterialBounds(tile, "M_Bruk");
            Bounds kamien = MaterialBounds(tile, "M_Kamien");

            AssertRange(bruk.min.x, bruk.max.x, -1.5f, -0.75f, "Pas bruku Mur_Dol w obrocie 1 (oś X, W = −X)");
            AssertRange(kamien.min.x, kamien.max.x, -0.75f, 1.5f, "Kamień z cokołem Mur_Dol w obrocie 1 (oś X, E = +X)");
        }

        [Test]
        public void MurNaroznikSrodek_SurowyModel_RamionaOdMinusXiMinusZ()
        {
            Bounds bounds = MeshBounds(SpawnModel("02_Mury/Mur_Naroznik_Srodek.fbx"));

            AssertRange(bounds.min.x, bounds.max.x, -1.5f, 0.75f, "Surowy Mur_Naroznik_Srodek (oś X, E = −X)");
            AssertRange(bounds.min.z, bounds.max.z, -1.5f, 0.75f, "Surowy Mur_Naroznik_Srodek (oś Z, N = −Z)");
        }

        [Test]
        public void MurNaroznikSrodek_Prefab_Obrot0_RamionaOdEiN_BezOdbiciaLustrzanego()
        {
            Bounds bounds = MeshBounds(SpawnPrefab("Mur_Naroznik_Srodek", 0));

            AssertRange(bounds.min.x, bounds.max.x, -0.75f, 1.5f, "Mur_Naroznik_Srodek w obrocie 0 (oś X, ramię E = +X)");
            AssertRange(bounds.min.z, bounds.max.z, -0.75f, 1.5f, "Mur_Naroznik_Srodek w obrocie 0 (oś Z, ramię N = +Z)");
        }

        GameObject SpawnModel(string pathInModels)
        {
            string path = $"{WfcPaths.Models}/{pathInModels}";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(asset, $"Brak modelu {path}.");
            return Spawn(asset, Quaternion.identity);
        }

        GameObject SpawnPrefab(string model, int rotation)
        {
            string path = WfcPaths.TilePrefab(model);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(asset, $"Brak prefabu {path}. Uruchom WFC → Importuj reguły i modele.");
            return Spawn(asset, UnityGrid.TileRotation(rotation));
        }

        GameObject Spawn(GameObject asset, Quaternion rotation)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.transform.SetPositionAndRotation(Vector3.zero, rotation);
            spawned.Add(instance);
            return instance;
        }

        /// <summary>Granice części siatki z danym materiałem w układzie świata (root stoi w punkcie 0, 0, 0).</summary>
        static Bounds MaterialBounds(GameObject root, string materialName)
        {
            bool found = false;
            var result = new Bounds();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Material[] materials = renderer.sharedMaterials;
                for (int subMesh = 0; subMesh < mesh.subMeshCount && subMesh < materials.Length; subMesh++)
                {
                    if (materials[subMesh] == null || materials[subMesh].name != materialName)
                        continue;
                    Bounds bounds = ToWorld(renderer.transform.localToWorldMatrix, mesh.GetSubMesh(subMesh).bounds);
                    if (found)
                        result.Encapsulate(bounds);
                    else
                        result = bounds;
                    found = true;
                }
            }
            Assert.IsTrue(found, $"Model {root.name} nie ma materiału {materialName}.");
            return result;
        }

        static Bounds MeshBounds(GameObject root)
        {
            bool found = false;
            var result = new Bounds();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Bounds bounds = ToWorld(filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
                if (found)
                    result.Encapsulate(bounds);
                else
                    result = bounds;
                found = true;
            }
            Assert.IsTrue(found, $"Model {root.name} nie ma siatki.");
            return result;
        }

        static Bounds ToWorld(Matrix4x4 matrix, Bounds local)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                var sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                result.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, sign)));
            }
            return result;
        }

        static void AssertRange(float min, float max, float expectedMin, float expectedMax, string what)
        {
            bool ok = Mathf.Abs(min - expectedMin) <= Tolerance && Mathf.Abs(max - expectedMax) <= Tolerance;
            Assert.IsTrue(ok, $"{what}: jest od {min:0.###} do {max:0.###} m, powinno być od {expectedMin:0.###} do {expectedMax:0.###} m. " +
                              "Jeśli strony są zamienione, popraw obrót bazowy w prefabach (UnityGrid.ModelBaseYaw) przed czymkolwiek innym.");
        }
    }
}
