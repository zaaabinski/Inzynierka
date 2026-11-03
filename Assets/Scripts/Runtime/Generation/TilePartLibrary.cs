using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace WFC.Runtime
{
    /// <summary>Jedna część prefabu kafelka: podsiatka modelu z materiałem i położeniem względem roota prefabu.</summary>
    public readonly struct TilePart
    {
        public TilePart(int id, Mesh mesh, int submesh, Material material, Matrix4x4 local, MeshRenderer renderer)
        {
            Id = id;
            Mesh = mesh;
            Submesh = submesh;
            Material = material;
            Local = local;
            ShadowCasting = renderer.shadowCastingMode;
            ReceiveShadows = renderer.receiveShadows;
            Layer = renderer.gameObject.layer;
        }

        /// <summary>Numer części w bibliotece, indeks jej danych w PartGeometry.</summary>
        public int Id { get; }

        public Mesh Mesh { get; }

        public int Submesh { get; }

        public Material Material { get; }

        public Matrix4x4 Local { get; }

        public ShadowCastingMode ShadowCasting { get; }

        public bool ReceiveShadows { get; }

        public int Layer { get; }
    }

    /// <summary>Gdzie w płaskich tablicach biblioteki leżą wierzchołki i indeksy jednej części.</summary>
    public struct PartGeometry
    {
        public int VertexStart;
        public int VertexCount;
        public int IndexStart;
        public int IndexCount;
    }

    /// <summary>
    /// Rozkłada prefaby kafelków na części (TilePart), wspólne dla rysowania instancjami i scalania siatek, i przechowuje
    /// ich geometrię w płaskich tablicach. Każda podsiatka trafia tu raz, także gdy używa jej kilka prefabów.
    /// </summary>
    public sealed class TilePartLibrary
    {
        readonly Dictionary<GameObject, TilePart[]> parts = new Dictionary<GameObject, TilePart[]>();
        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<int> indices = new List<int>();
        readonly List<PartGeometry> geometry = new List<PartGeometry>();
        readonly List<TilePart> allParts = new List<TilePart>();
        readonly List<Vector3> meshVertices = new List<Vector3>();
        readonly List<Vector3> meshNormals = new List<Vector3>();
        readonly List<int> meshIndices = new List<int>();

        /// <summary>Wszystkie części w kolejności numerów.</summary>
        public IReadOnlyList<TilePart> AllParts => allParts;

        public IReadOnlyList<PartGeometry> Geometry => geometry;

        /// <summary>Pozycje wierzchołków części w układzie modelu.</summary>
        public IReadOnlyList<Vector3> Positions => positions;

        public IReadOnlyList<Vector3> Normals => normals;

        /// <summary>Indeksy liczone od pierwszego wierzchołka części.</summary>
        public IReadOnlyList<int> Indices => indices;

        /// <summary>Części prefabu; przy pierwszym użyciu czyta geometrię jego podsiatek.</summary>
        public TilePart[] PartsOf(GameObject prefab)
        {
            if (parts.TryGetValue(prefab, out TilePart[] cached))
                return cached;
            var list = new List<TilePart>();
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!renderer.enabled || filter == null || filter.sharedMesh == null)
                    continue;
                Mesh mesh = filter.sharedMesh;
                Material[] materials = renderer.sharedMaterials;
                Matrix4x4 local = toRoot * renderer.transform.localToWorldMatrix;
                for (int submesh = 0; submesh < mesh.subMeshCount && materials.Length > 0; submesh++)
                {
                    Material material = materials[Math.Min(submesh, materials.Length - 1)];
                    if (material == null)
                        continue;
                    var part = new TilePart(allParts.Count, mesh, submesh, material, local, renderer);
                    allParts.Add(part);
                    geometry.Add(ReadGeometry(mesh, submesh));
                    list.Add(part);
                }
            }
            return parts[prefab] = list.ToArray();
        }

        PartGeometry ReadGeometry(Mesh mesh, int submesh)
        {
            if (!mesh.isReadable)
                throw new InvalidOperationException($"Model {mesh.name} nie ma włączonego odczytu siatki. Uruchom WFC → Importuj reguły i modele.");
            SubMeshDescriptor descriptor = mesh.GetSubMesh(submesh);
            mesh.GetVertices(meshVertices);
            mesh.GetNormals(meshNormals);
            mesh.GetIndices(meshIndices, submesh);
            var part = new PartGeometry
            {
                VertexStart = positions.Count,
                VertexCount = descriptor.vertexCount,
                IndexStart = indices.Count,
                IndexCount = meshIndices.Count
            };
            for (int vertex = descriptor.firstVertex; vertex < descriptor.firstVertex + descriptor.vertexCount; vertex++)
            {
                positions.Add(meshVertices[vertex]);
                normals.Add(meshNormals[vertex]);
            }
            foreach (int index in meshIndices)
                indices.Add(index - descriptor.firstVertex);
            return part;
        }

        /// <summary>Położenie części po obrocie kafelka: macierz względem środka podstawy komórki.</summary>
        public static float4x4 PoseOf(in TilePart part, int rotation) =>
            Matrix4x4.Rotate(UnityGrid.TileRotation(rotation)) * part.Local;
    }
}
