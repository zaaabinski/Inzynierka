using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Tests
{
    /// <summary>Kamień 5: podgląd krok po kroku (StepVisualizer) i kroki w paczkach w GeneratorController.</summary>
    public sealed class StepPreviewTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (GameObject item in created)
                if (item != null)
                    Object.DestroyImmediate(item);
            created.Clear();
        }

        GeneratorController Controller(int3 size)
        {
            var gameObject = new GameObject("Generator testowy");
            created.Add(gameObject);
            var controller = gameObject.AddComponent<GeneratorController>();
            controller.Tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset("Pelny"));
            controller.GridSize = size;
            controller.Solver = SolverKind.Bitset;
            controller.Seed = 1;
            return controller;
        }

        [TestCase(1, -1)]
        [TestCase(2, 0)]
        [TestCase(3, 0)]
        [TestCase(4, 1)]
        [TestCase(15, 2)]
        [TestCase(16, 3)]
        [TestCase(63, 4)]
        [TestCase(64, 5)]
        [TestCase(170, 5)]
        public void Przedzial_LiczbyMozliwosci(int possibilities, int band)
        {
            Assert.AreEqual(band, StepVisualizer.Band(possibilities));
        }

        [Test]
        public void KolorKazdegoPrzedzialu()
        {
            Assert.AreEqual(StepVisualizer.BandStarts.Length, StepVisualizer.BandColors.Length);
        }

        [Test]
        public void KrokiWPaczce_TakSamoJakPojedynczo()
        {
            var size = new int3(10, 8, 10);
            GeneratorController batch = Controller(size);
            batch.Step(25);
            StringAssert.Contains("piętro", batch.LastReport);

            GeneratorController single = Controller(size);
            for (int step = 0; step < 25; step++)
                single.Step();

            Assert.AreEqual(batch.ActiveSolver.Metrics.Decisions, single.ActiveSolver.Metrics.Decisions);
            for (int cell = 0; cell < single.ActiveSolver.Grid.CellCount; cell++)
                Assert.AreEqual(batch.ActiveSolver.GetPossibilityCount(cell), single.ActiveSolver.GetPossibilityCount(cell), $"komórka {cell}");

            Assert.AreEqual(StepStatus.Finished, single.Step(100000));
            Assert.IsTrue(single.LastResult.Success);
            Assert.IsNull(single.Visualizer, "poza trybem Play podglądu nie ma");
        }
    }
}
