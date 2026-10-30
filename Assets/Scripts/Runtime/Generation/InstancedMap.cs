using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Kostki mapy rysowane instancjami (Graphics.RenderMeshInstanced), bez obiektów w scenie. Każdy prefab kafelka
    /// rozkłada się na części (siatka, podsiatka, materiał, położenie w prefabie); części o tej samej siatce i materiale
    /// z całej mapy trafiają na jedną listę macierzy, rysowaną co klatkę po 1023 instancje. Mapa 80 × 80 × 13
    /// (14 652 kostki) to 102 wywołania rysowania zamiast 14 652 obiektów z MeshRenderer; stawia się szybciej (9 zamiast
    /// 69 ms), ale klatka trwa dłużej (2,96 zamiast 0,85 ms), więc MapBuilder domyślnie używa obiektów.
    /// </summary>
    public sealed class InstancedMap
    {
        /// <summary>Najwięcej instancji w jednym wywołaniu rysowania.</summary>
        const int MaxPerDraw = 1023;

        /// <summary>Margines granic mapy na okapy, chorągwie i łuki mostów wystające z kostek [m].</summary>
        const float BoundsMargin = 6f;

        sealed class Batch
        {
            public Mesh Mesh;
            public int Submesh;
            public RenderParams Params;
            public Matrix4x4[] Matrices = new Matrix4x4[64];
            public int Count;

            public void Add(in Matrix4x4 matrix)
            {
                if (Count == Matrices.Length)
                    Array.Resize(ref Matrices, Count * 2);
                Matrices[Count++] = matrix;
            }
        }

        readonly struct Part
        {
            public Part(Batch batch, Matrix4x4 local)
            {
                Batch = batch;
                Local = local;
            }

            public Batch Batch { get; }

            /// <summary>Położenie części względem roota prefabu.</summary>
            public Matrix4x4 Local { get; }
        }

        readonly Dictionary<(Mesh, int, Material, ShadowCastingMode, bool, int), Batch> batches = new Dictionary<(Mesh, int, Material, ShadowCastingMode, bool, int), Batch>();
        readonly Dictionary<GameObject, Part[]> parts = new Dictionary<GameObject, Part[]>();
        readonly Dictionary<Material, Material> instancedMaterials = new Dictionary<Material, Material>();

        /// <summary>Komórki z modelem w ostatnio zbudowanej mapie.</summary>
        public int InstanceCount { get; private set; }

        /// <summary>Wywołania rysowania na klatkę.</summary>
        public int DrawCalls
        {
            get
            {
                int calls = 0;
                foreach (Batch batch in batches.Values)
                    calls += (batch.Count + MaxPerDraw - 1) / MaxPerDraw;
                return calls;
            }
        }

        public void Build(TilesetDefinition tileset, CompiledRules rules, GridShape grid, int[] states, uint seed, Matrix4x4 mapToWorld)
        {
            foreach (Batch batch in batches.Values)
                batch.Count = 0;
            InstanceCount = 0;

            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int state = states[cell];
                if (state < 0)
                    continue;
                TileDefinition tile = tileset.Tiles[rules.GetTileIndex(state)];
                if (tile.Prefab == null)
                    continue;
                int3 position = grid.Coordinates(cell);
                Matrix4x4 root = mapToWorld * Matrix4x4.TRS(UnityGrid.CellPosition(position.x, position.y, position.z),
                    UnityGrid.TileRotation(MapBuilder.ModelRotation(tile, rules, state, seed, cell)), Vector3.one);
                foreach (Part part in PartsOf(tile.Prefab))
                    part.Batch.Add(root * part.Local);
                InstanceCount++;
            }

            Vector3 size = new Vector3(grid.Width, grid.Floors, grid.Depth) * UnityGrid.CellSize;
            var bounds = new Bounds(mapToWorld.MultiplyPoint3x4(size / 2f), size + Vector3.one * (2f * BoundsMargin));
            foreach (Batch batch in batches.Values)
                batch.Params.worldBounds = bounds;
        }

        /// <summary>Wszystkie rysowane części z położeniem w świecie (do testów i sprawdzania).</summary>
        public IEnumerable<(Mesh Mesh, int Submesh, Material Material, Matrix4x4 Matrix)> Instances()
        {
            foreach (Batch batch in batches.Values)
                for (int i = 0; i < batch.Count; i++)
                    yield return (batch.Mesh, batch.Submesh, batch.Params.material, batch.Matrices[i]);
        }

        /// <summary>Wywoływać co klatkę (MapBuilder.LateUpdate): instancje rysują się tylko w klatce, w której je zgłoszono.</summary>
        public void Draw()
        {
            foreach (Batch batch in batches.Values)
                for (int start = 0; start < batch.Count; start += MaxPerDraw)
                    Graphics.RenderMeshInstanced(batch.Params, batch.Mesh, batch.Submesh, batch.Matrices, Math.Min(MaxPerDraw, batch.Count - start), start);
        }

        Part[] PartsOf(GameObject prefab)
        {
            if (parts.TryGetValue(prefab, out Part[] cached))
                return cached;
            var list = new List<Part>();
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
                    if (material != null)
                        list.Add(new Part(BatchFor(mesh, submesh, Instanced(material), renderer), local));
                }
            }
            return parts[prefab] = list.ToArray();
        }

        Batch BatchFor(Mesh mesh, int submesh, Material material, MeshRenderer renderer)
        {
            var key = (mesh, submesh, material, renderer.shadowCastingMode, renderer.receiveShadows, renderer.gameObject.layer);
            if (batches.TryGetValue(key, out Batch batch))
                return batch;
            batch = new Batch
            {
                Mesh = mesh,
                Submesh = submesh,
                Params = new RenderParams(material)
                {
                    shadowCastingMode = renderer.shadowCastingMode,
                    receiveShadows = renderer.receiveShadows,
                    layer = renderer.gameObject.layer
                }
            };
            return batches[key] = batch;
        }

        /// <summary>
        /// Materiał z włączonym instancjonowaniem. Importer włącza je w zasobach (ModelLibrary); materiał spoza zestawu
        /// dostaje kopię, ale w buildzie może wtedy brakować wariantu shadera.
        /// </summary>
        Material Instanced(Material material)
        {
            if (material.enableInstancing)
                return material;
            if (!instancedMaterials.TryGetValue(material, out Material copy))
            {
                Debug.LogWarning($"[MapBuilder] Materiał {material.name} nie ma włączonego instancjonowania; rysuję kopią. Włącz „Enable GPU Instancing” w materiale.");
                copy = new Material(material) { name = material.name + " (instancje)", enableInstancing = true };
                instancedMaterials[material] = copy;
            }
            return copy;
        }
    }
}
