using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using WFC.Core;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace WFC.Runtime
{
    /// <summary>
    /// Mapa scalona w statyczne siatki przez Mesh API (Mesh.AllocateWritableMeshData): tysiące kostek stają się kilkoma
    /// siatkami, po jednej na fragment mapy (domyślnie 16 × 16 kolumn, żeby kamera mogła pomijać niewidoczne fragmenty).
    /// Bez atlasu fragment ma podsiatkę na materiał, z atlasem (MaterialAtlas) jedną podsiatkę i jeden materiał, czyli jedno
    /// wywołanie rysowania. Dane wierzchołków wypełnia zadanie Burst, po jednym na fragment, równolegle. Zmiana mapy
    /// przebudowuje tylko fragmenty ze zmienionymi komórkami. Stare siatki są niszczone, a tablice natywne zwalniane
    /// w tym samym wywołaniu, więc nic nie zostaje po Dispose.
    /// </summary>
    public sealed class CombinedMap : IDisposable
    {
        /// <summary>Rozmiar wierzchołka: pozycja 12 bajtów, normalna 8, UV 4.</summary>
        public const int BytesPerVertex = 24;

        const MeshUpdateFlags ApplyFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        /// <summary>Zapis na normalnych w 16 bitach i UV w 16 bitach zamiast float: 24 zamiast 32 bajtów na wierzchołek.</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct PackedVertex
        {
            public float3 Position;
            public short NormalX;
            public short NormalY;
            public short NormalZ;
            public short NormalW;
            public ushort U;
            public ushort V;
        }

        /// <summary>Część po obrocie kafelka: przekształcenie pozycji i normalnych względem środka podstawy komórki.</summary>
        struct PartPose
        {
            public float3x3 Rotation;
            public float3 Offset;
            public float3x3 NormalMatrix;
        }

        /// <summary>Jedna część w jednej komórce i miejsce na jej dane w siatce fragmentu.</summary>
        struct PartInstance
        {
            public int Geometry;
            public int Pose;
            public float3 Cell;
            public int VertexStart;
            public int IndexStart;
            public int Submesh;
            public ushort U;
            public ushort V;
        }

        /// <summary>Jedna podsiatka fragmentu.</summary>
        struct SubmeshRange
        {
            public int Material;
            public int VertexStart;
            public int VertexCount;
            public int IndexStart;
            public int IndexCount;
        }

        /// <summary>Kopiuje części jednego fragmentu do jego siatki; pozycje i normalne przekształca po drodze.</summary>
        [BurstCompile(CompileSynchronously = true)]
        struct WriteChunkJob : IJob
        {
            public Mesh.MeshData Output;
            public bool WideIndices;
            public int FirstInstance;
            public int InstanceCount;
            public int FirstBounds;
            [ReadOnly] public NativeArray<PartInstance> Instances;
            [ReadOnly] public NativeArray<PartGeometry> Geometry;
            [ReadOnly] public NativeArray<PartPose> Poses;
            [ReadOnly] public NativeArray<float3> Positions;
            [ReadOnly] public NativeArray<float3> Normals;
            [ReadOnly] public NativeArray<int> Indices;

            // Każde zadanie pisze tylko we własny zakres, od FirstBounds.
            [NativeDisableContainerSafetyRestriction] public NativeArray<float3> BoundsMin;
            [NativeDisableContainerSafetyRestriction] public NativeArray<float3> BoundsMax;

            public void Execute()
            {
                NativeArray<PackedVertex> vertices = Output.GetVertexData<PackedVertex>();
                NativeArray<ushort> narrow = WideIndices ? default : Output.GetIndexData<ushort>();
                NativeArray<uint> wide = WideIndices ? Output.GetIndexData<uint>() : default;

                for (int i = FirstInstance; i < FirstInstance + InstanceCount; i++)
                {
                    PartInstance instance = Instances[i];
                    PartGeometry geometry = Geometry[instance.Geometry];
                    PartPose pose = Poses[instance.Pose];
                    float3 offset = pose.Offset + instance.Cell;
                    float3 min = BoundsMin[FirstBounds + instance.Submesh];
                    float3 max = BoundsMax[FirstBounds + instance.Submesh];

                    for (int v = 0; v < geometry.VertexCount; v++)
                    {
                        float3 position = math.mul(pose.Rotation, Positions[geometry.VertexStart + v]) + offset;
                        float3 normal = math.normalizesafe(math.mul(pose.NormalMatrix, Normals[geometry.VertexStart + v]));
                        min = math.min(min, position);
                        max = math.max(max, position);
                        vertices[instance.VertexStart + v] = new PackedVertex
                        {
                            Position = position,
                            NormalX = (short)math.round(normal.x * short.MaxValue),
                            NormalY = (short)math.round(normal.y * short.MaxValue),
                            NormalZ = (short)math.round(normal.z * short.MaxValue),
                            U = instance.U,
                            V = instance.V
                        };
                    }

                    if (WideIndices)
                        for (int n = 0; n < geometry.IndexCount; n++)
                            wide[instance.IndexStart + n] = (uint)(instance.VertexStart + Indices[geometry.IndexStart + n]);
                    else
                        for (int n = 0; n < geometry.IndexCount; n++)
                            narrow[instance.IndexStart + n] = (ushort)(instance.VertexStart + Indices[geometry.IndexStart + n]);

                    BoundsMin[FirstBounds + instance.Submesh] = min;
                    BoundsMax[FirstBounds + instance.Submesh] = max;
                }
            }
        }

        sealed class Chunk
        {
            public GameObject Object;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public int3 From;
            public int3 To;
            public int Instances;
            public int FirstInstance;
            public int Vertices;
            public int Indices;
            public int FirstBounds;
            public readonly List<SubmeshRange> Submeshes = new List<SubmeshRange>();
        }

        static readonly VertexAttributeDescriptor[] VertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm16, 2)
        };

        readonly Transform root;
        readonly HideFlags hideFlags;
        readonly bool keepReadable;
        readonly TilePartLibrary library = new TilePartLibrary();
        readonly Dictionary<Material, int> materialIndex = new Dictionary<Material, int>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Chunk> dirty = new List<Chunk>();
        readonly List<Mesh> outputMeshes = new List<Mesh>();

        Chunk[] chunks = Array.Empty<Chunk>();
        MaterialAtlas atlas;
        TilesetDefinition shownTileset;
        GridShape grid;
        int[] shownStates = Array.Empty<int>();
        uint shownSeed;
        bool hasMap;

        /// <param name="root">Rodzic obiektów fragmentów; siatki mają pozycje w jego układzie.</param>
        /// <param name="chunkSize">Bok fragmentu w kolumnach siatki.</param>
        /// <param name="useAtlas">Jeden materiał z paletą kolorów zamiast podsiatki na materiał.</param>
        /// <param name="keepReadable">Zostawia kopię siatek w pamięci procesora, potrzebną do zapisu jako zasób.</param>
        /// <param name="hideFlags">Flagi obiektów i siatek; podgląd w edytorze używa DontSave, żeby nie trafił do sceny.</param>
        public CombinedMap(Transform root, int chunkSize, bool useAtlas, bool keepReadable = false, HideFlags hideFlags = HideFlags.None)
        {
            this.root = root;
            ChunkSize = Mathf.Max(1, chunkSize);
            UsesAtlas = useAtlas;
            this.keepReadable = keepReadable;
            this.hideFlags = hideFlags;
        }

        public int ChunkSize { get; }

        public bool UsesAtlas { get; }

        public MaterialAtlas Atlas => atlas;

        /// <summary>Komórki z modelem.</summary>
        public int InstanceCount { get; private set; }

        /// <summary>Części wszystkich kostek, czyli wywołania rysowania bez scalania (bez cieni).</summary>
        public int PartCount { get; private set; }

        /// <summary>Niepuste fragmenty.</summary>
        public int RendererCount { get; private set; }

        /// <summary>Wywołania rysowania scalonej mapy (bez cieni): podsiatki wszystkich fragmentów.</summary>
        public int DrawCallCount { get; private set; }

        /// <summary>Różne materiały w scalonej mapie.</summary>
        public int MaterialCount { get; private set; }

        public long VertexCount { get; private set; }

        public long TriangleCount { get; private set; }

        /// <summary>Rozmiar danych siatek w bajtach.</summary>
        public long MeshBytes { get; private set; }

        /// <summary>Fragmenty przebudowane przez ostatnie Build.</summary>
        public int RebuiltChunks { get; private set; }

        /// <summary>Czas ostatniego Build w milisekundach.</summary>
        public double LastBuildMs { get; private set; }

        /// <summary>Siatki niepustych fragmentów.</summary>
        public IEnumerable<Mesh> Meshes
        {
            get
            {
                foreach (Chunk chunk in chunks)
                    if (chunk.Mesh != null)
                        yield return chunk.Mesh;
            }
        }

        /// <summary>Obiekty fragmentów (MeshFilter i MeshRenderer).</summary>
        public IEnumerable<MeshRenderer> Renderers
        {
            get
            {
                foreach (Chunk chunk in chunks)
                    if (chunk.Mesh != null)
                        yield return chunk.Renderer;
            }
        }

        /// <summary>Koszt rysowania przed scaleniem i po nim, po polsku.</summary>
        public string Describe()
        {
            double reduction = DrawCallCount > 0 ? (double)PartCount / DrawCallCount : 0;
            return $"Przed scaleniem: kostki {InstanceCount}, części {PartCount}, czyli {PartCount} wywołań rysowania bez cieni.\n" +
                   $"Po scaleniu: fragmenty {RendererCount}, materiały {MaterialCount}, wywołania rysowania bez cieni {DrawCallCount} ({reduction:0.#} razy mniej), " +
                   $"wierzchołki {VertexCount}, trójkąty {TriangleCount}, siatki {MeshBytes / (1024.0 * 1024.0):0.0} MB, scalanie {LastBuildMs:0.0} ms.";
        }

        /// <summary>Stawia mapę albo przebudowuje fragmenty, w których zmieniły się komórki.</summary>
        /// <param name="states">Stan każdej komórki albo −1 dla komórki jeszcze nieustalonej.</param>
        /// <param name="seed">Ziarno mapy; z niego losowy obrót modelu kafelków symetrycznych.</param>
        public void Build(TilesetDefinition tileset, CompiledRules rules, GridShape newGrid, int[] states, uint seed)
        {
            var watch = Stopwatch.StartNew();
            if (tileset != shownTileset || !grid.Equals(newGrid) || chunks.Length == 0)
                Prepare(tileset, newGrid);

            dirty.Clear();
            foreach (Chunk chunk in chunks)
                if (!hasMap || seed != shownSeed || Changed(chunk, states))
                    dirty.Add(chunk);
            RebuiltChunks = dirty.Count;
            if (dirty.Count > 0)
                using (WfcMarkers.MeshCombine.Auto())
                    Rebuild(tileset, rules, states, seed);

            Array.Copy(states, shownStates, states.Length);
            shownSeed = seed;
            hasMap = true;
            UpdateStats(tileset, rules, states);
            LastBuildMs = watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>Niszczy obiekty, siatki i atlas.</summary>
        public void Dispose()
        {
            foreach (Chunk chunk in chunks)
            {
                DestroyObject(chunk.Mesh);
                DestroyObject(chunk.Object);
            }
            chunks = Array.Empty<Chunk>();
            atlas?.Dispose();
            atlas = null;
            shownTileset = null;
            hasMap = false;
        }

        void Prepare(TilesetDefinition tileset, GridShape newGrid)
        {
            Dispose();
            grid = newGrid;
            shownTileset = tileset;
            shownStates = new int[grid.CellCount];

            materials.Clear();
            materialIndex.Clear();
            foreach (TileDefinition tile in tileset.Tiles)
                if (tile.Prefab != null)
                    foreach (TilePart part in library.PartsOf(tile.Prefab))
                        if (!materialIndex.ContainsKey(part.Material))
                        {
                            materialIndex[part.Material] = materials.Count;
                            materials.Add(part.Material);
                        }
            if (UsesAtlas && materials.Count > 0)
            {
                atlas = MaterialAtlas.Build(materials, hideFlags);
                foreach (string difference in atlas.Differences)
                    Debug.LogWarning($"[CombinedMap] Atlas materiałów: {difference}");
            }

            int columns = Mathf.CeilToInt((float)grid.Width / ChunkSize);
            int rows = Mathf.CeilToInt((float)grid.Depth / ChunkSize);
            chunks = new Chunk[columns * rows];
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    var gameObject = new GameObject($"Fragment_{column:00}_{row:00}", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = hideFlags };
                    gameObject.transform.SetParent(root, false);
                    chunks[row * columns + column] = new Chunk
                    {
                        Object = gameObject,
                        Filter = gameObject.GetComponent<MeshFilter>(),
                        Renderer = gameObject.GetComponent<MeshRenderer>(),
                        From = new int3(column * ChunkSize, 0, row * ChunkSize),
                        To = new int3(Mathf.Min((column + 1) * ChunkSize, grid.Width), grid.Floors, Mathf.Min((row + 1) * ChunkSize, grid.Depth))
                    };
                }
        }

        bool Changed(Chunk chunk, int[] states)
        {
            for (int floor = chunk.From.y; floor < chunk.To.y; floor++)
                for (int z = chunk.From.z; z < chunk.To.z; z++)
                    for (int cell = grid.Index(chunk.From.x, floor, z), end = cell + chunk.To.x - chunk.From.x; cell < end; cell++)
                        if (states[cell] != shownStates[cell])
                            return true;
            return false;
        }

        void Rebuild(TilesetDefinition tileset, CompiledRules rules, int[] states, uint seed)
        {
            int totalInstances = 0;
            int totalBounds = 0;
            int written = 0;
            foreach (Chunk chunk in dirty)
            {
                PlanChunk(chunk, tileset, rules, states);
                chunk.FirstInstance = totalInstances;
                chunk.FirstBounds = totalBounds;
                totalInstances += chunk.Instances;
                totalBounds += chunk.Submeshes.Count;
                if (chunk.Instances > 0)
                    written++;
            }

            // Pusta mapa w trybie krok po kroku: tylko zdejmuje poprzednie siatki.
            if (written == 0)
            {
                foreach (Chunk chunk in dirty)
                    ReplaceMesh(chunk);
                return;
            }

            var positions = new NativeArray<float3>(library.Positions.Count, Allocator.TempJob);
            var normals = new NativeArray<float3>(library.Normals.Count, Allocator.TempJob);
            var indices = new NativeArray<int>(library.Indices.Count, Allocator.TempJob);
            var geometry = new NativeArray<PartGeometry>(library.Geometry.Count, Allocator.TempJob);
            var poses = new NativeArray<PartPose>(library.AllParts.Count * DirectionExtensions.SideCount, Allocator.TempJob);
            var instances = new NativeArray<PartInstance>(totalInstances, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var boundsMin = new NativeArray<float3>(totalBounds, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var boundsMax = new NativeArray<float3>(totalBounds, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            Mesh.MeshDataArray output = Mesh.AllocateWritableMeshData(written);
            var handles = new NativeArray<JobHandle>(written, Allocator.TempJob);
            bool applied = false;
            try
            {
                FillSources(positions, normals, indices, geometry, poses);
                for (int i = 0; i < totalBounds; i++)
                {
                    boundsMin[i] = new float3(float.PositiveInfinity);
                    boundsMax[i] = new float3(float.NegativeInfinity);
                }

                // Najpierw całe dane, potem zadania: zadanie czyta tablicę instancji, więc po jego starcie nie wolno w niej pisać.
                foreach (Chunk chunk in dirty)
                {
                    FillInstances(chunk, tileset, rules, states, seed, instances);
                    ReplaceMesh(chunk);
                }

                outputMeshes.Clear();
                int slot = 0;
                foreach (Chunk chunk in dirty)
                {
                    if (chunk.Instances == 0)
                        continue;
                    bool wide = chunk.Vertices > ushort.MaxValue;
                    Mesh.MeshData data = output[slot];
                    data.SetVertexBufferParams(chunk.Vertices, VertexLayout);
                    data.SetIndexBufferParams(chunk.Indices, wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
                    handles[slot] = new WriteChunkJob
                    {
                        Output = data,
                        WideIndices = wide,
                        FirstInstance = chunk.FirstInstance,
                        InstanceCount = chunk.Instances,
                        FirstBounds = chunk.FirstBounds,
                        Instances = instances,
                        Geometry = geometry,
                        Poses = poses,
                        Positions = positions,
                        Normals = normals,
                        Indices = indices,
                        BoundsMin = boundsMin,
                        BoundsMax = boundsMax
                    }.Schedule();
                    outputMeshes.Add(chunk.Mesh);
                    slot++;
                }
                JobHandle.CombineDependencies(handles).Complete();

                slot = 0;
                foreach (Chunk chunk in dirty)
                    if (chunk.Instances > 0)
                        SetSubmeshes(chunk, output[slot++], boundsMin, boundsMax);
                Mesh.ApplyAndDisposeWritableMeshData(output, outputMeshes, ApplyFlags);
                applied = true;

                foreach (Chunk chunk in dirty)
                    if (chunk.Instances > 0)
                        FinishChunk(chunk, boundsMin, boundsMax);
            }
            finally
            {
                // Zadania muszą skończyć, zanim zwolnimy ich dane, także po wyjątku w połowie.
                JobHandle.CombineDependencies(handles).Complete();
                if (!applied)
                    output.Dispose();
                handles.Dispose();
                positions.Dispose();
                normals.Dispose();
                indices.Dispose();
                geometry.Dispose();
                poses.Dispose();
                instances.Dispose();
                boundsMin.Dispose();
                boundsMax.Dispose();
            }
        }

        /// <summary>Podsiatki fragmentu i miejsce na ich dane: wierzchołki i indeksy idą ciągiem, podsiatka po podsiatce.</summary>
        void PlanChunk(Chunk chunk, TilesetDefinition tileset, CompiledRules rules, int[] states)
        {
            int slots = UsesAtlas ? 1 : Mathf.Max(1, materials.Count);
            var vertexCounts = new int[slots];
            var indexCounts = new int[slots];
            chunk.Instances = 0;
            for (int floor = chunk.From.y; floor < chunk.To.y; floor++)
                for (int z = chunk.From.z; z < chunk.To.z; z++)
                    for (int x = chunk.From.x; x < chunk.To.x; x++)
                    {
                        int state = states[grid.Index(x, floor, z)];
                        if (state < 0)
                            continue;
                        GameObject prefab = tileset.Tiles[rules.GetTileIndex(state)].Prefab;
                        if (prefab == null)
                            continue;
                        foreach (TilePart part in library.PartsOf(prefab))
                        {
                            PartGeometry geometry = library.Geometry[part.Id];
                            int slot = UsesAtlas ? 0 : materialIndex[part.Material];
                            vertexCounts[slot] += geometry.VertexCount;
                            indexCounts[slot] += geometry.IndexCount;
                            chunk.Instances++;
                        }
                    }

            chunk.Submeshes.Clear();
            chunk.Vertices = 0;
            chunk.Indices = 0;
            for (int slot = 0; slot < slots; slot++)
            {
                if (vertexCounts[slot] == 0)
                    continue;
                chunk.Submeshes.Add(new SubmeshRange
                {
                    Material = slot,
                    VertexStart = chunk.Vertices,
                    VertexCount = vertexCounts[slot],
                    IndexStart = chunk.Indices,
                    IndexCount = indexCounts[slot]
                });
                chunk.Vertices += vertexCounts[slot];
                chunk.Indices += indexCounts[slot];
            }
        }

        void FillSources(NativeArray<float3> positions, NativeArray<float3> normals, NativeArray<int> indices, NativeArray<PartGeometry> geometry, NativeArray<PartPose> poses)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = library.Positions[i];
                normals[i] = library.Normals[i];
            }
            for (int i = 0; i < indices.Length; i++)
                indices[i] = library.Indices[i];
            for (int i = 0; i < geometry.Length; i++)
                geometry[i] = library.Geometry[i];
            for (int part = 0; part < library.AllParts.Count; part++)
                for (int rotation = 0; rotation < DirectionExtensions.SideCount; rotation++)
                {
                    float4x4 matrix = TilePartLibrary.PoseOf(library.AllParts[part], rotation);
                    var linear = new float3x3(matrix);
                    poses[part * DirectionExtensions.SideCount + rotation] = new PartPose
                    {
                        Rotation = linear,
                        Offset = matrix.c3.xyz,
                        NormalMatrix = math.transpose(math.inverse(linear))
                    };
                }
        }

        void FillInstances(Chunk chunk, TilesetDefinition tileset, CompiledRules rules, int[] states, uint seed, NativeArray<PartInstance> instances)
        {
            int slots = UsesAtlas ? 1 : Mathf.Max(1, materials.Count);
            var vertexCursor = new int[slots];
            var indexCursor = new int[slots];
            var submeshOfSlot = new int[slots];
            for (int s = 0; s < chunk.Submeshes.Count; s++)
            {
                SubmeshRange range = chunk.Submeshes[s];
                vertexCursor[range.Material] = range.VertexStart;
                indexCursor[range.Material] = range.IndexStart;
                submeshOfSlot[range.Material] = s;
            }

            int next = chunk.FirstInstance;
            for (int floor = chunk.From.y; floor < chunk.To.y; floor++)
                for (int z = chunk.From.z; z < chunk.To.z; z++)
                    for (int x = chunk.From.x; x < chunk.To.x; x++)
                    {
                        int cell = grid.Index(x, floor, z);
                        int state = states[cell];
                        if (state < 0)
                            continue;
                        TileDefinition tile = tileset.Tiles[rules.GetTileIndex(state)];
                        if (tile.Prefab == null)
                            continue;
                        int rotation = MapBuilder.ModelRotation(tile, rules, state, seed, cell);
                        float3 position = UnityGrid.CellPosition(x, floor, z);
                        foreach (TilePart part in library.PartsOf(tile.Prefab))
                        {
                            PartGeometry geometry = library.Geometry[part.Id];
                            int slot = UsesAtlas ? 0 : materialIndex[part.Material];
                            Vector2 uv = UsesAtlas ? atlas.UvOf(part.Material) : Vector2.zero;
                            instances[next++] = new PartInstance
                            {
                                Geometry = part.Id,
                                Pose = part.Id * DirectionExtensions.SideCount + rotation,
                                Cell = position,
                                VertexStart = vertexCursor[slot],
                                IndexStart = indexCursor[slot],
                                Submesh = submeshOfSlot[slot],
                                U = (ushort)Mathf.RoundToInt(uv.x * ushort.MaxValue),
                                V = (ushort)Mathf.RoundToInt(uv.y * ushort.MaxValue)
                            };
                            vertexCursor[slot] += geometry.VertexCount;
                            indexCursor[slot] += geometry.IndexCount;
                        }
                    }
        }

        /// <summary>Nowa siatka zamiast starej: siatka bez kopii w pamięci procesora nie da się już zmienić.</summary>
        void ReplaceMesh(Chunk chunk)
        {
            DestroyObject(chunk.Mesh);
            chunk.Mesh = null;
            chunk.Filter.sharedMesh = null;
            chunk.Renderer.enabled = chunk.Instances > 0;
            if (chunk.Instances > 0)
                chunk.Mesh = new Mesh { name = chunk.Object.name, hideFlags = hideFlags };
        }

        static void SetSubmeshes(Chunk chunk, Mesh.MeshData data, NativeArray<float3> boundsMin, NativeArray<float3> boundsMax)
        {
            data.subMeshCount = chunk.Submeshes.Count;
            for (int s = 0; s < chunk.Submeshes.Count; s++)
            {
                SubmeshRange range = chunk.Submeshes[s];
                var bounds = new Bounds();
                bounds.SetMinMax(boundsMin[chunk.FirstBounds + s], boundsMax[chunk.FirstBounds + s]);
                data.SetSubMesh(s, new SubMeshDescriptor(range.IndexStart, range.IndexCount)
                {
                    firstVertex = range.VertexStart,
                    vertexCount = range.VertexCount,
                    bounds = bounds
                }, ApplyFlags);
            }
        }

        void FinishChunk(Chunk chunk, NativeArray<float3> boundsMin, NativeArray<float3> boundsMax)
        {
            float3 min = new float3(float.PositiveInfinity);
            float3 max = new float3(float.NegativeInfinity);
            var rendererMaterials = new Material[chunk.Submeshes.Count];
            for (int s = 0; s < chunk.Submeshes.Count; s++)
            {
                min = math.min(min, boundsMin[chunk.FirstBounds + s]);
                max = math.max(max, boundsMax[chunk.FirstBounds + s]);
                rendererMaterials[s] = UsesAtlas ? atlas.Material : materials[chunk.Submeshes[s].Material];
            }
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            chunk.Mesh.bounds = bounds;
            chunk.Filter.sharedMesh = chunk.Mesh;
            chunk.Renderer.sharedMaterials = rendererMaterials;
            if (!keepReadable)
                chunk.Mesh.UploadMeshData(true);
        }

        void UpdateStats(TilesetDefinition tileset, CompiledRules rules, int[] states)
        {
            InstanceCount = 0;
            PartCount = 0;
            foreach (int state in states)
            {
                if (state < 0)
                    continue;
                GameObject prefab = tileset.Tiles[rules.GetTileIndex(state)].Prefab;
                if (prefab == null)
                    continue;
                InstanceCount++;
                PartCount += library.PartsOf(prefab).Length;
            }

            RendererCount = 0;
            DrawCallCount = 0;
            VertexCount = 0;
            TriangleCount = 0;
            MeshBytes = 0;
            var used = new HashSet<int>();
            foreach (Chunk chunk in chunks)
            {
                if (chunk.Mesh == null)
                    continue;
                RendererCount++;
                DrawCallCount += chunk.Submeshes.Count;
                VertexCount += chunk.Vertices;
                TriangleCount += chunk.Indices / 3;
                MeshBytes += (long)chunk.Vertices * BytesPerVertex + (long)chunk.Indices * (chunk.Vertices > ushort.MaxValue ? 4 : 2);
                foreach (SubmeshRange range in chunk.Submeshes)
                    used.Add(range.Material);
            }
            MaterialCount = used.Count;
        }

        static void DestroyObject(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
