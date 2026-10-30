using System;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>Różnica między dwiema mapami tej samej siatki i zestawu, w procentach komórek z innym kafelkiem.</summary>
    public readonly struct MapDifference
    {
        public MapDifference(double allPercent, double groundPercent, double builtPercent, int builtCells)
        {
            AllPercent = allPercent;
            GroundPercent = groundPercent;
            BuiltPercent = builtPercent;
            BuiltCells = builtCells;
        }

        /// <summary>Wszystkie komórki siatki. Zaniżone przez puste powietrze na wyższych piętrach.</summary>
        public double AllPercent { get; }

        /// <summary>Komórki parteru.</summary>
        public double GroundPercent { get; }

        /// <summary>Komórki zabudowy: takie, w których choć na jednej mapie stoi coś innego niż teren i powietrze.</summary>
        public double BuiltPercent { get; }

        /// <summary>Liczba komórek zabudowy, z których liczy się BuiltPercent.</summary>
        public int BuiltCells { get; }

        public override string ToString() => $"{AllPercent:0.#}% komórek, parter {GroundPercent:0.#}%, zabudowa {BuiltPercent:0.#}%";
    }

    /// <summary>
    /// Różnorodność map (rozdział 2 handoffu): odsetek komórek, w których dwie mapy mają inny kafelek. Obrót się nie liczy,
    /// więc ta sama wieża obrócona inaczej nie jest różnicą. Ta sama mapa daje 0%, a mapy z różnych ziaren pokazują,
    /// jak bardzo generator zmienia zamek między ziarnami.
    /// </summary>
    public static class DiversityAnalyzer
    {
        public static MapDifference Compare(CompiledRules rules, int3 size, int[] first, int[] second)
        {
            var grid = new GridShape(size);
            if (first.Length != grid.CellCount || second.Length != grid.CellCount)
                throw new ArgumentException($"Mapy mają {first.Length} i {second.Length} komórek, a siatka {grid} ma {grid.CellCount}.");

            int different = 0;
            int groundDifferent = 0;
            int built = 0;
            int builtDifferent = 0;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int a = first[cell] < 0 ? -1 : rules.GetTileIndex(first[cell]);
                int b = second[cell] < 0 ? -1 : rules.GetTileIndex(second[cell]);
                bool differs = a != b;
                different += differs ? 1 : 0;
                if (cell < grid.CellsPerFloor)
                    groundDifferent += differs ? 1 : 0;
                if (IsBuilding(rules, a) || IsBuilding(rules, b))
                {
                    built++;
                    builtDifferent += differs ? 1 : 0;
                }
            }
            return new MapDifference(
                Percent(different, grid.CellCount),
                Percent(groundDifferent, grid.CellsPerFloor),
                Percent(builtDifferent, built),
                built);
        }

        static bool IsBuilding(CompiledRules rules, int tile)
        {
            if (tile < 0)
                return false;
            string group = rules.Tiles[tile].Group;
            return group != CastleVocabulary.GroupTerrain && group != CastleVocabulary.GroupAir;
        }

        static double Percent(int part, int whole) => whole == 0 ? 0 : 100.0 * part / whole;
    }
}
