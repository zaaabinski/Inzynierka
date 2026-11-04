using System;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>Sposób pokazania mapy.</summary>
    public enum MapDisplay
    {
        [InspectorName("Obiekty w scenie")]
        GameObjects,

        [InspectorName("Instancje na karcie graficznej")]
        Instanced,

        [InspectorName("Scalone siatki")]
        Combined,

        [InspectorName("Scalone siatki z atlasem")]
        CombinedAtlas
    }

    /// <summary>
    /// Stawia prefaby kafelków pod [Mapa], w osobnym obiekcie na każde piętro (Pietro_00, Pietro_01, …).
    /// Zmienia tylko komórki, których stan się zmienił, więc w trybie krok po kroku dokłada pojedyncze kostki.
    /// Czas stawiania mierzymy osobno, żeby nie fałszował czasu generacji.
    /// <para>
    /// Inne sposoby: instancje na karcie graficznej bez obiektów w scenie (InstancedMap, tylko w trybie Play i w zbudowanej
    /// aplikacji) oraz mapa scalona w statyczne siatki, z podsiatką na materiał albo z atlasem i jednym materiałem (CombinedMap).
    /// Domyślny jest atlas. Pomiar z 3.11.2026 (build Mono, okno 1600 × 900, mapa 80 × 80 × 13, SRP Batcher i GPU Resident
    /// Drawer włączone): obiekty – stawianie 75,6 ms, klatka 1,07 ms; atlas – stawianie 20,6 ms, klatka 0,57 ms.
    /// </para>
    /// </summary>
    public sealed class MapBuilder : MonoBehaviour
    {
        // Bez DontUnloadUnusedAsset: siatki osierocone po przeładowaniu kodu w edytorze sprząta Unity przy wczytaniu sceny.
        const HideFlags EditModeCombinedFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        [Tooltip("Rodzic kostek, w scenie Generator obiekt [Mapa].")]
        [SerializeField] Transform mapRoot;
        [Tooltip("Obiekty w scenie, instancje na karcie graficznej (tylko w trybie Play i w zbudowanej aplikacji) albo mapa scalona w statyczne siatki, bez atlasu lub z atlasem (domyślnie). Poza trybem Play scalona mapa to podgląd, który nie zapisuje się ze sceną.")]
        [SerializeField] MapDisplay display = MapDisplay.CombinedAtlas;
        [Tooltip("Scalanie: bok fragmentu mapy w kolumnach siatki. Każdy fragment to osobna siatka, którą kamera może pominąć.")]
        [SerializeField, Min(1)] int combineChunkSize = 16;
        [Tooltip("Po zmianie rozmiaru siatki ustaw kamerę (FlyCamera) tak, żeby widać było całą mapę.")]
        [SerializeField] bool frameCameraOnResize = true;

        GameObject[] instances = Array.Empty<GameObject>();
        int[] shownStates = Array.Empty<int>();
        Transform[] floors = Array.Empty<Transform>();
        GridShape grid;
        TilesetDefinition shownTileset;
        InstancedMap instanced;
        CombinedMap combined;
        int3 framedSize;

        public Transform MapRoot
        {
            get => mapRoot != null ? mapRoot : transform;
            set => mapRoot = value;
        }

        public MapDisplay Display
        {
            get => display;
            set => display = value;
        }

        public int CombineChunkSize
        {
            get => combineChunkSize;
            set => combineChunkSize = Mathf.Max(1, value);
        }

        /// <summary>Czas ostatniego stawiania kostek w milisekundach.</summary>
        public double LastBuildMs { get; private set; }

        /// <summary>Liczba postawionych kostek (kafelki bez modelu, np. Powietrze, nic nie stawiają).</summary>
        public int InstanceCount { get; private set; }

        /// <summary>Czy ostatnia mapa jest rysowana instancjami (bez obiektów w scenie).</summary>
        public bool IsInstanced => instanced != null;

        /// <summary>Wywołania rysowania na klatkę w trybie instancji, inaczej 0.</summary>
        public int InstancedDrawCalls => instanced?.DrawCalls ?? 0;

        /// <summary>Scalona mapa i jej statystyki albo null w innych trybach.</summary>
        public CombinedMap Combined => combined;

        /// <summary>Czy następna mapa będzie rysowana instancjami (tylko w trybie Play, bo rysuje je LateUpdate).</summary>
        public bool UsesInstancing => Application.isPlaying && display == MapDisplay.Instanced;

        /// <summary>
        /// Obrót modelu w komórce: obrót stanu albo, dla kafelków z losowym obrotem modelu (symetrycznych), obrót
        /// z ziarna mapy i numeru komórki. Wspólny dla obiektów w scenie i instancji.
        /// </summary>
        public static int ModelRotation(TileDefinition tile, CompiledRules rules, int state, uint seed, int cell) =>
            tile.RandomModelRotation ? (int)(math.hash(new uint2(seed, (uint)cell)) & 3u) : rules.GetRotation(state);

        /// <param name="states">Stan każdej komórki albo −1 dla komórki jeszcze nieustalonej.</param>
        /// <param name="seed">Ziarno mapy; z niego losowy obrót modelu kafelków symetrycznych.</param>
        public void Show(TilesetDefinition tileset, CompiledRules rules, GridShape newGrid, int[] states, uint seed)
        {
            var watch = Stopwatch.StartNew();
            using (WfcMarkers.MapShow.Auto())
            {
                if (display == MapDisplay.Combined || display == MapDisplay.CombinedAtlas)
                    ShowCombined(tileset, rules, newGrid, states, seed);
                else if (UsesInstancing)
                    ShowInstanced(tileset, rules, newGrid, states, seed);
                else
                    ShowObjects(tileset, rules, newGrid, states, seed);
            }
            LastBuildMs = watch.Elapsed.TotalMilliseconds;
            FrameCameraIfResized(newGrid);
        }

        [ContextMenu("Wyczyść mapę")]
        public void Clear()
        {
            combined?.Dispose();
            combined = null;
            Transform root = MapRoot;
            for (int child = root.childCount - 1; child >= 0; child--)
                DestroyObject(root.GetChild(child).gameObject);
            instances = Array.Empty<GameObject>();
            shownStates = Array.Empty<int>();
            floors = Array.Empty<Transform>();
            shownTileset = null;
            instanced = null;
            InstanceCount = 0;
        }

        void LateUpdate() => instanced?.Draw();

        void OnDestroy()
        {
            combined?.Dispose();
            combined = null;
        }

        void ShowCombined(TilesetDefinition tileset, CompiledRules rules, GridShape newGrid, int[] states, uint seed)
        {
            bool atlas = display == MapDisplay.CombinedAtlas;
            if (combined == null || combined.UsesAtlas != atlas || combined.ChunkSize != combineChunkSize)
            {
                Clear();
                combined = new CombinedMap(MapRoot, combineChunkSize, atlas, false, Application.isPlaying ? HideFlags.None : EditModeCombinedFlags);
            }
            combined.Build(tileset, rules, newGrid, states, seed);
            grid = newGrid;
            shownTileset = tileset;
            InstanceCount = combined.InstanceCount;
        }

        void ShowInstanced(TilesetDefinition tileset, CompiledRules rules, GridShape newGrid, int[] states, uint seed)
        {
            if (instanced == null || tileset != shownTileset || !grid.Equals(newGrid))
            {
                Clear();
                instanced = new InstancedMap();
                grid = newGrid;
                shownTileset = tileset;
            }
            instanced.Build(tileset, rules, newGrid, states, seed, MapRoot.localToWorldMatrix);
            InstanceCount = instanced.InstanceCount;
        }

        void ShowObjects(TilesetDefinition tileset, CompiledRules rules, GridShape newGrid, int[] states, uint seed)
        {
            if (instanced != null || combined != null || tileset != shownTileset || !grid.Equals(newGrid) || instances.Length != newGrid.CellCount || Array.Exists(floors, floor => floor == null))
                Prepare(tileset, newGrid);

            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int state = states[cell];
                if (shownStates[cell] == state)
                    continue;
                if (instances[cell] != null)
                {
                    DestroyObject(instances[cell]);
                    instances[cell] = null;
                    InstanceCount--;
                }
                shownStates[cell] = state;
                if (state < 0)
                    continue;

                TileDefinition tile = tileset.Tiles[rules.GetTileIndex(state)];
                if (tile.Prefab == null)
                    continue;
                int3 position = grid.Coordinates(cell);
                GameObject instance = Instantiate(tile.Prefab, floors[position.y]);
                instance.name = tile.Prefab.name;
                instance.transform.SetLocalPositionAndRotation(UnityGrid.CellPosition(position.x, position.y, position.z),
                    UnityGrid.TileRotation(ModelRotation(tile, rules, state, seed, cell)));
                instances[cell] = instance;
                InstanceCount++;
            }
        }

        /// <summary>
        /// Po zmianie rozmiaru siatki kamera patrzy na całą mapę pod tymi samymi kątami. Pierwsza mapa nie przestawia
        /// kamery, bo scena Generator ma ją już ustawioną na siatkę z GeneratorController.
        /// </summary>
        void FrameCameraIfResized(GridShape shown)
        {
            if (math.all(framedSize == shown.Size))
                return;
            bool first = math.all(framedSize == 0);
            framedSize = shown.Size;
            if (first || !frameCameraOnResize || !Application.isPlaying || Camera.main == null || !Camera.main.TryGetComponent(out FlyCamera flyCamera))
                return;
            // Tak jak w scenie Generator dla 20 × 20 × 13: 95 m od punktu 6 m nad środkiem mapy szerokości 60 m.
            float extent = Mathf.Max(shown.Width, shown.Depth) * UnityGrid.CellSize;
            Vector3 center = MapRoot.TransformPoint(new Vector3(shown.Width, 0f, shown.Depth) * (UnityGrid.CellSize / 2f));
            flyCamera.Frame(center + Vector3.up * (extent * 0.1f), extent * 95f / 60f);
        }

        void Prepare(TilesetDefinition tileset, GridShape newGrid)
        {
            Clear();
            grid = newGrid;
            shownTileset = tileset;
            instances = new GameObject[grid.CellCount];
            shownStates = new int[grid.CellCount];
            Array.Fill(shownStates, -1);
            floors = new Transform[grid.Floors];
            for (int floor = 0; floor < grid.Floors; floor++)
            {
                var floorObject = new GameObject($"Pietro_{floor:00}");
                floorObject.transform.SetParent(MapRoot, false);
                floors[floor] = floorObject.transform;
            }
        }

        static void DestroyObject(GameObject target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
