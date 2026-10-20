using System;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Kształt siatki w układzie programu: x = E, y = piętro, z = N. Komórki są numerowane piętrami od dołu,
    /// w piętrze rzędami od S do N, w rzędzie od W do E, więc jedno piętro to ciągły zakres numerów.
    /// </summary>
    public readonly struct GridShape : IEquatable<GridShape>
    {
        public GridShape(int3 size)
        {
            if (math.any(size < 1))
                throw new ArgumentOutOfRangeException(nameof(size), size, "Siatka musi mieć co najmniej 1 komórkę w każdym kierunku.");
            Size = size;
        }

        public int3 Size { get; }

        public int Width => Size.x;

        public int Floors => Size.y;

        public int Depth => Size.z;

        public int CellsPerFloor => Size.x * Size.z;

        public int CellCount => Size.x * Size.y * Size.z;

        public int Index(int x, int floor, int z) => (floor * Size.z + z) * Size.x + x;

        public int Index(int3 cell) => Index(cell.x, cell.y, cell.z);

        public int3 Coordinates(int cell)
        {
            int x = cell % Size.x;
            int rest = cell / Size.x;
            return new int3(x, rest / Size.z, rest % Size.z);
        }

        public int Floor(int cell) => cell / CellsPerFloor;

        public bool Contains(int3 cell) => math.all(cell >= 0) && math.all(cell < Size);

        /// <summary>Sąsiad komórki w danym kierunku albo −1, gdy kierunek wychodzi poza siatkę.</summary>
        public int Neighbor(int cell, Direction direction)
        {
            int3 next = Coordinates(cell) + direction.Offset();
            return Contains(next) ? Index(next) : -1;
        }

        /// <summary>Tablica sąsiadów: indeks komórka × 6 + kierunek, −1 poza siatką.</summary>
        public int[] BuildNeighborTable()
        {
            var table = new int[CellCount * DirectionExtensions.Count];
            for (int cell = 0; cell < CellCount; cell++)
                foreach (Direction direction in DirectionExtensions.All)
                    table[cell * DirectionExtensions.Count + (int)direction] = Neighbor(cell, direction);
            return table;
        }

        public bool Equals(GridShape other) => math.all(Size == other.Size);

        public override bool Equals(object obj) => obj is GridShape other && Equals(other);

        public override int GetHashCode() => Size.GetHashCode();

        public override string ToString() => $"{Size.x} × {Size.z} × {Size.y}";
    }
}
