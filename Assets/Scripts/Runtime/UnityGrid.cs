using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Siatka programu w Unity (rozdział 6): x = E, y = piętro, z = N, kostka 3 × 3 × 3 m z punktem obrotu
    /// w środku podstawy. Reguły są zapisane w układzie Blendera (N = +Y, góra = +Z); po eksporcie FBX
    /// (przód −Z, góra Y) i imporcie do Unity model ma E na −X i N na −Z, dlatego w prefabie obracamy go o 180°.
    /// </summary>
    public static class UnityGrid
    {
        public const float CellSize = 3f;

        /// <summary>Obrót modelu FBX wokół osi Y w prefabie kafelka.</summary>
        public const float ModelBaseYaw = 180f;

        /// <summary>Obrót roota prefabu dla obrotu kafelka k (k × 90° przeciwnie do ruchu wskazówek zegara z góry).</summary>
        public static Quaternion TileRotation(int rotation) => Quaternion.Euler(0f, -90f * rotation, 0f);

        /// <summary>Punkt środka podstawy komórki (x, piętro, z): (3x + 1,5; 3 · piętro; 3z + 1,5) m.</summary>
        public static Vector3 CellPosition(int x, int floor, int z) =>
            new Vector3(CellSize * x + CellSize / 2f, CellSize * floor, CellSize * z + CellSize / 2f);

        /// <summary>Wektor jednostkowy kierunku w siatce programu.</summary>
        public static Vector3 ToVector(Direction direction)
        {
            var offset = direction.Offset();
            return new Vector3(offset.x, offset.y, offset.z);
        }
    }
}
