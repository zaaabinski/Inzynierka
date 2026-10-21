using System;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Kierunek ściany kafelka. Boki E, N, W, S są ułożone w kolejności obrotu o 90° przeciwnie do ruchu
    /// wskazówek zegara (patrząc z góry), więc obrót o k kroków to dodanie k modulo 4.
    /// </summary>
    public enum Direction
    {
        E = 0,
        N = 1,
        W = 2,
        S = 3,
        Up = 4,
        Down = 5
    }

    public static class DirectionExtensions
    {
        /// <summary>Liczba wszystkich kierunków: 4 boki, góra i dół.</summary>
        public const int Count = 6;

        /// <summary>Liczba boków: E, N, W, S.</summary>
        public const int SideCount = 4;

        public static readonly Direction[] All = { Direction.E, Direction.N, Direction.W, Direction.S, Direction.Up, Direction.Down };

        public static readonly Direction[] Sides = { Direction.E, Direction.N, Direction.W, Direction.S };

        public static bool IsSide(this Direction direction) => (int)direction < SideCount;

        public static Direction Opposite(this Direction direction)
        {
            switch (direction)
            {
                case Direction.E: return Direction.W;
                case Direction.N: return Direction.S;
                case Direction.W: return Direction.E;
                case Direction.S: return Direction.N;
                case Direction.Up: return Direction.Down;
                case Direction.Down: return Direction.Up;
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        /// <summary>
        /// Kierunek po obrocie kafelka o rotation × 90° przeciwnie do ruchu wskazówek zegara:
        /// jeden krok przenosi E na N, N na W, W na S i S na E. Góra i dół się nie zmieniają.
        /// </summary>
        public static Direction Rotate(this Direction direction, int rotation)
        {
            if (!direction.IsSide())
                return direction;
            int steps = ((rotation % SideCount) + SideCount) % SideCount;
            return (Direction)(((int)direction + steps) % SideCount);
        }

        /// <summary>
        /// Przesunięcie do sąsiedniej komórki w siatce programu: x = E, y = piętro, z = N (rozdział 6).
        /// </summary>
        public static int3 Offset(this Direction direction)
        {
            switch (direction)
            {
                case Direction.E: return new int3(1, 0, 0);
                case Direction.N: return new int3(0, 0, 1);
                case Direction.W: return new int3(-1, 0, 0);
                case Direction.S: return new int3(0, 0, -1);
                case Direction.Up: return new int3(0, 1, 0);
                case Direction.Down: return new int3(0, -1, 0);
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        /// <summary>Nazwa kierunku jak w pliku reguł: E, N, W, S, gora, dol.</summary>
        public static string RulesKey(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return "gora";
                case Direction.Down: return "dol";
                default: return direction.ToString();
            }
        }
    }
}
