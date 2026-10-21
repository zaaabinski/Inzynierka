using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>Kierunki, obroty i pozycje z rozdziału 6.</summary>
    public sealed class DirectionTests
    {
        [TestCase(Direction.E, Direction.W)]
        [TestCase(Direction.N, Direction.S)]
        [TestCase(Direction.W, Direction.E)]
        [TestCase(Direction.S, Direction.N)]
        [TestCase(Direction.Up, Direction.Down)]
        [TestCase(Direction.Down, Direction.Up)]
        public void Przeciwny(Direction direction, Direction opposite)
        {
            Assert.AreEqual(opposite, direction.Opposite());
        }

        [TestCase(Direction.E, Direction.N)]
        [TestCase(Direction.N, Direction.W)]
        [TestCase(Direction.W, Direction.S)]
        [TestCase(Direction.S, Direction.E)]
        [TestCase(Direction.Up, Direction.Up)]
        [TestCase(Direction.Down, Direction.Down)]
        public void ObrotO90Stopni(Direction direction, Direction rotated)
        {
            Assert.AreEqual(rotated, direction.Rotate(1));
        }

        [Test]
        public void ObrotO360StopniIOMinus90()
        {
            foreach (Direction direction in DirectionExtensions.All)
            {
                Assert.AreEqual(direction, direction.Rotate(4));
                Assert.AreEqual(direction.Rotate(3), direction.Rotate(-1));
            }
        }

        [Test]
        public void PrzesuniecieWSiatce_xE_yPietro_zN()
        {
            Assert.AreEqual(new int3(1, 0, 0), Direction.E.Offset());
            Assert.AreEqual(new int3(0, 0, 1), Direction.N.Offset());
            Assert.AreEqual(new int3(-1, 0, 0), Direction.W.Offset());
            Assert.AreEqual(new int3(0, 0, -1), Direction.S.Offset());
            Assert.AreEqual(new int3(0, 1, 0), Direction.Up.Offset());
            Assert.AreEqual(new int3(0, -1, 0), Direction.Down.Offset());
        }

        [Test]
        public void ObrotWUnity_ZgodnyZObrotemEtykiet()
        {
            // Quaternion.Euler(0, −90k, 0) musi przenosić stronę d tam, gdzie reguły przenoszą jej etykietę.
            for (int rotation = 0; rotation < 4; rotation++)
                foreach (Direction side in DirectionExtensions.Sides)
                {
                    Vector3 actual = UnityGrid.TileRotation(rotation) * UnityGrid.ToVector(side);
                    Vector3 expected = UnityGrid.ToVector(side.Rotate(rotation));
                    Assert.Less(Vector3.Distance(expected, actual), 1e-5f, $"Obrót {rotation}: strona {side} trafia w {actual}, a powinna w {expected}.");
                }
        }

        [Test]
        public void PozycjaKomorki_SrodekPodstawyKostki()
        {
            Assert.AreEqual(new Vector3(1.5f, 0f, 1.5f), UnityGrid.CellPosition(0, 0, 0));
            Assert.AreEqual(new Vector3(58.5f, 36f, 58.5f), UnityGrid.CellPosition(19, 12, 19));
        }
    }
}
