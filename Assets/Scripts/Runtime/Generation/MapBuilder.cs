using System;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Stawia prefaby kafelków pod [Mapa], w osobnym obiekcie na każde piętro (Pietro_00, Pietro_01, …).
    /// Zmienia tylko komórki, których stan się zmienił, więc w trybie krok po kroku dokłada pojedyncze kostki.
    /// Czas stawiania mierzymy osobno, żeby nie fałszował czasu generacji.
    /// </summary>
    public sealed class MapBuilder : MonoBehaviour
    {
        [Tooltip("Rodzic kostek, w scenie Generator obiekt [Mapa].")]
        [SerializeField] Transform mapRoot;

        GameObject[] instances = Array.Empty<GameObject>();
        int[] shownStates = Array.Empty<int>();
        Transform[] floors = Array.Empty<Transform>();
        GridShape grid;
        TilesetDefinition shownTileset;

        public Transform MapRoot
        {
            get => mapRoot != null ? mapRoot : transform;
            set => mapRoot = value;
        }

        /// <summary>Czas ostatniego stawiania kostek w milisekundach.</summary>
        public double LastBuildMs { get; private set; }

        /// <summary>Liczba postawionych kostek (kafelki bez modelu, np. Powietrze, nic nie stawiają).</summary>
        public int InstanceCount { get; private set; }

        /// <param name="states">Stan każdej komórki albo −1 dla komórki jeszcze nieustalonej.</param>
        /// <param name="seed">Ziarno mapy; z niego losowy obrót modelu kafelków symetrycznych.</param>
        public void Show(TilesetDefinition tileset, CompiledRules rules, GridShape newGrid, int[] states, uint seed)
        {
            var watch = Stopwatch.StartNew();
            if (tileset != shownTileset || !grid.Equals(newGrid) || instances.Length != newGrid.CellCount || Array.Exists(floors, floor => floor == null))
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
                int rotation = tile.RandomModelRotation
                    ? (int)(math.hash(new uint2(seed, (uint)cell)) & 3u)
                    : rules.GetRotation(state);
                GameObject instance = Instantiate(tile.Prefab, floors[position.y]);
                instance.name = tile.Prefab.name;
                instance.transform.SetLocalPositionAndRotation(UnityGrid.CellPosition(position.x, position.y, position.z), UnityGrid.TileRotation(rotation));
                instances[cell] = instance;
                InstanceCount++;
            }
            LastBuildMs = watch.Elapsed.TotalMilliseconds;
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
            InstanceCount = 0;
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
