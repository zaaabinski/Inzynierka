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
        Instanced
    }

    /// <summary>
    /// Stawia prefaby kafelków pod [Mapa], w osobnym obiekcie na każde piętro (Pietro_00, Pietro_01, …).
    /// Zmienia tylko komórki, których stan się zmienił, więc w trybie krok po kroku dokłada pojedyncze kostki.
    /// Czas stawiania mierzymy osobno, żeby nie fałszował czasu generacji.
    /// <para>
    /// Drugi sposób, instancje na karcie graficznej bez obiektów w scenie (InstancedMap), działa w trybie Play i w zbudowanej
    /// aplikacji. Pomiar z 30.10.2026 (build Mono, okno 1600 × 900, mapa 80 × 80 × 13 z 14 652 kostkami): obiekty – stawianie
    /// 69 ms, klatka 0,85 ms; instancje – stawianie 9 ms, klatka 2,96 ms, bo co klatkę wysyłają około 30 tysięcy macierzy,
    /// a obiekty z SRP Batcher mają dane na karcie na stałe. Dlatego domyślnie obiekty.
    /// </para>
    /// </summary>
    public sealed class MapBuilder : MonoBehaviour
    {
        [Tooltip("Rodzic kostek, w scenie Generator obiekt [Mapa].")]
        [SerializeField] Transform mapRoot;
        [Tooltip("Obiekty w scenie (domyślnie) albo instancje na karcie graficznej; instancje działają tylko w trybie Play i w zbudowanej aplikacji.")]
        [SerializeField] MapDisplay display = MapDisplay.GameObjects;
        [Tooltip("Po zmianie rozmiaru siatki ustaw kamerę (FlyCamera) tak, żeby widać było całą mapę.")]
        [SerializeField] bool frameCameraOnResize = true;

        GameObject[] instances = Array.Empty<GameObject>();
        int[] shownStates = Array.Empty<int>();
        Transform[] floors = Array.Empty<Transform>();
        GridShape grid;
        TilesetDefinition shownTileset;
        InstancedMap instanced;
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

        /// <summary>Czas ostatniego stawiania kostek w milisekundach.</summary>
        public double LastBuildMs { get; private set; }

        /// <summary>Liczba postawionych kostek (kafelki bez modelu, np. Powietrze, nic nie stawiają).</summary>
        public int InstanceCount { get; private set; }

        /// <summary>Czy ostatnia mapa jest rysowana instancjami (bez obiektów w scenie).</summary>
        public bool IsInstanced => instanced != null;

        /// <summary>Wywołania rysowania na klatkę w trybie instancji, inaczej 0.</summary>
        public int InstancedDrawCalls => instanced?.DrawCalls ?? 0;

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
            if (UsesInstancing)
                ShowInstanced(tileset, rules, newGrid, states, seed);
            else
                ShowObjects(tileset, rules, newGrid, states, seed);
            LastBuildMs = watch.Elapsed.TotalMilliseconds;
            FrameCameraIfResized(newGrid);
        }

        [ContextMenu("Wyczyść mapę")]
        public void Clear()
        {
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
            if (instanced != null || tileset != shownTileset || !grid.Equals(newGrid) || instances.Length != newGrid.CellCount || Array.Exists(floors, floor => floor == null))
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
