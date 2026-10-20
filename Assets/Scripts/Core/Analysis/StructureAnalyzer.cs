using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>Wygląd jednej mapy: budowle, ich wysokości, udziały kafelków i dostępność terenu.</summary>
    public sealed class StructureStats
    {
        public int Towers;
        public int Keeps;
        public int Bridges;
        public int Stairs;

        /// <summary>Wysokość każdej wieży w kostkach, od parteru do iglicy albo blanków.</summary>
        public readonly List<int> TowerHeights = new List<int>();

        /// <summary>Wysokość każdej kolumny muru (odcinek albo narożnik) w kostkach.</summary>
        public readonly List<int> WallHeights = new List<int>();

        /// <summary>Wysokość każdego donżonu w kostkach, razem z dachem.</summary>
        public readonly List<int> KeepHeights = new List<int>();

        /// <summary>Długość każdego mostu w kostkach, od początku do końca.</summary>
        public readonly List<int> BridgeLengths = new List<int>();

        /// <summary>Liczba komórek każdego kafelka (indeks kafelka w zestawie).</summary>
        public int[] TileCounts = Array.Empty<int>();

        /// <summary>Komórki terenu na parterze i ile z nich da się dojść od krawędzi mapy po terenie.</summary>
        public int GroundTerrainCells;
        public int ReachableTerrainCells;

        /// <summary>Obszary terenu na parterze bez połączenia z krawędzią mapy.</summary>
        public int EnclosedCourtyards;

        public double ReachableTerrainPercent => GroundTerrainCells == 0 ? 0 : 100.0 * ReachableTerrainCells / GroundTerrainCells;
    }

    /// <summary>
    /// Liczy budowle na gotowej mapie. Wieża = spójny obszar ćwiartek wieży na parterze (poprawna ma 2 × 2),
    /// donżon = spójny obszar kafelków donżonu na parterze, most = kafelek z początkiem mostu,
    /// dziedziniec zamknięty = obszar terenu na parterze bez dojścia od krawędzi mapy.
    /// </summary>
    public static class StructureAnalyzer
    {
        public static StructureStats Analyze(CompiledRules rules, int3 size, int[] states)
        {
            var grid = new GridShape(size);
            var map = new MapView(rules, grid, states);
            var stats = new StructureStats { TileCounts = new int[rules.TileCount] };

            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int state = states[cell];
                if (state < 0)
                    continue;
                stats.TileCounts[rules.GetTileIndex(state)]++;
                string group = map.Group(cell);
                if (group == CastleVocabulary.GroupStairs)
                    stats.Stairs++;
                if (group == CastleVocabulary.GroupBridge && map.SideWithLabel(cell, CastleVocabulary.LabelBridgeStart) is Direction direction)
                {
                    stats.Bridges++;
                    stats.BridgeLengths.Add(BridgeLength(map, cell, direction));
                }
            }

            foreach (List<int> tower in map.TowerFootprints())
            {
                stats.Towers++;
                stats.TowerHeights.Add(tower.Max(cell => map.ColumnHeight(cell, CastleVocabulary.GroupTower)));
            }

            foreach (List<int> keep in map.GroundComponents(CastleVocabulary.GroupKeep))
            {
                stats.Keeps++;
                stats.KeepHeights.Add(keep.Max(cell => map.ColumnHeight(cell, CastleVocabulary.GroupKeep, CastleVocabulary.GroupKeepRoof)));
            }

            for (int cell = 0; cell < grid.CellsPerFloor; cell++)
            {
                string group = map.Group(cell);
                if (group == CastleVocabulary.GroupWall || group == CastleVocabulary.GroupWallCorner)
                    stats.WallHeights.Add(map.ColumnHeight(cell, CastleVocabulary.GroupWall, CastleVocabulary.GroupWallCorner));
            }

            foreach (List<int> area in map.GroundComponents(CastleVocabulary.GroupTerrain))
            {
                stats.GroundTerrainCells += area.Count;
                if (area.Any(cell => TouchesMapEdge(grid, cell)))
                    stats.ReachableTerrainCells += area.Count;
                else
                    stats.EnclosedCourtyards++;
            }

            return stats;
        }

        /// <summary>Od początku mostu w kierunku jego środka aż do końca (ostatni kafelek ma przyczółek po tej stronie).</summary>
        static int BridgeLength(MapView map, int start, Direction direction)
        {
            int length = 1;
            int cell = map.Grid.Neighbor(start, direction);
            while (cell >= 0 && map.Group(cell) == CastleVocabulary.GroupBridge && length < map.Grid.Width + map.Grid.Depth)
            {
                length++;
                if (map.Label(cell, direction) == CastleVocabulary.LabelBridgeAbutment)
                    break;
                cell = map.Grid.Neighbor(cell, direction);
            }
            return length;
        }

        static bool TouchesMapEdge(GridShape grid, int cell)
        {
            int3 position = grid.Coordinates(cell);
            return position.x == 0 || position.z == 0 || position.x == grid.Width - 1 || position.z == grid.Depth - 1;
        }
    }
}
