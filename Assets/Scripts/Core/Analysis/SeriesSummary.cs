using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WFC.Core
{
    /// <summary>
    /// Podsumowanie serii generacji (np. ziarna 1–100): próby, sprzeczności, naruszenia i średnie wyglądu.
    /// Średnie wysokości i długości liczymy po wszystkich budowlach serii, tak jak w tabeli z rozdziału 9.
    /// </summary>
    public sealed class SeriesSummary
    {
        public int Maps;
        public int SuccessfulMaps;
        public int Attempts;
        public int Contradictions;
        public int Violations;
        public double SolverMs;
        public double SuccessfulMapsSolverMs;
        public int Towers;
        public int Keeps;
        public int Bridges;
        public int Stairs;
        public int MapsWithBridges;
        public int MapsWithStairs;
        public int EnclosedCourtyards;
        public double ReachableTerrainPercentSum;
        public readonly List<int> TowerHeights = new List<int>();
        public readonly List<int> WallHeights = new List<int>();
        public readonly List<int> KeepHeights = new List<int>();
        public readonly List<int> BridgeLengths = new List<int>();
        public readonly Dictionary<string, long> TerrainCells = new Dictionary<string, long>();
        public readonly int[] ViolationsByType = new int[RuleCheckReport.TypeCount];

        public void Add(CompiledRules rules, GenerationResult result, RuleCheckReport check, StructureStats stats)
        {
            Maps++;
            Attempts += result.Metrics.Attempts;
            Contradictions += result.Metrics.Contradictions;
            SolverMs += result.Metrics.TotalMs;
            if (!result.Success)
                return;

            SuccessfulMaps++;
            SuccessfulMapsSolverMs += result.Metrics.TotalMs;
            Violations += check.Total;
            for (int type = 0; type < ViolationsByType.Length; type++)
                if (!check.IsDisabled((ViolationType)type))
                    ViolationsByType[type] += check[(ViolationType)type];
            Towers += stats.Towers;
            Keeps += stats.Keeps;
            Bridges += stats.Bridges;
            Stairs += stats.Stairs;
            MapsWithBridges += stats.Bridges > 0 ? 1 : 0;
            MapsWithStairs += stats.Stairs > 0 ? 1 : 0;
            EnclosedCourtyards += stats.EnclosedCourtyards;
            ReachableTerrainPercentSum += stats.ReachableTerrainPercent;
            TowerHeights.AddRange(stats.TowerHeights);
            WallHeights.AddRange(stats.WallHeights);
            KeepHeights.AddRange(stats.KeepHeights);
            BridgeLengths.AddRange(stats.BridgeLengths);
            for (int tile = 0; tile < rules.TileCount; tile++)
                if (rules.Tiles[tile].Group == CastleVocabulary.GroupTerrain)
                {
                    TerrainCells.TryGetValue(rules.Tiles[tile].Name, out long cells);
                    TerrainCells[rules.Tiles[tile].Name] = cells + stats.TileCounts[tile];
                }
        }

        public double ContradictionRate => Attempts == 0 ? 0 : (double)Contradictions / Attempts;

        public double PerMap(int total) => SuccessfulMaps == 0 ? 0 : (double)total / SuccessfulMaps;

        public double TerrainShare(string tileName)
        {
            long all = TerrainCells.Values.Sum();
            return all == 0 || !TerrainCells.TryGetValue(tileName, out long cells) ? 0 : (double)cells / all;
        }

        /// <summary>„min–max, średnio x” jak w tabeli prototypu.</summary>
        public static string Range(List<int> values) =>
            values.Count == 0 ? "brak" : $"{values.Min()}–{values.Max()}, średnio {Number(values.Average())}";

        public static string Number(double value, int decimals = 1) =>
            Math.Round(value, decimals).ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
