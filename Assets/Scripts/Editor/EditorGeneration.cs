using System;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>Parametry generatora w oknie edytora; serializowane z oknem, więc przetrwają przeładowanie kodu.</summary>
    [Serializable]
    public sealed class GeneratorToolSettings
    {
        public TilesetDefinition Tileset;
        public int East = 20;
        public int North = 20;
        public int Floors = 13;
        public SolverKind Solver = SolverKind.Burst;
        public SelectorKind Selector = SelectorKind.LevelFirst;
        public ContradictionKind Contradiction = ContradictionKind.Backtrack;
        public uint Seed = 1;
        public int MaxAttempts = 1000;
        public int ChunkSize = 16;
        public bool UseAtlas = true;

        public int3 Size => new int3(East, Floors, North);
    }

    /// <summary>
    /// Jedna generacja rozłożona na kawałki: Advance wykonuje kroki rozwiązywacza przez podany czas i oddaje sterowanie
    /// edytorowi, więc duża mapa nie blokuje okien, a porzucenie obiektu przerywa generację. Rozwiązywacze trzymają dane
    /// w zwykłych tablicach, więc przerwana generacja nie zostawia nic do zwolnienia.
    /// </summary>
    public sealed class EditorGeneration
    {
        readonly IWfcSolver solver;

        public EditorGeneration(GeneratorToolSettings settings)
        {
            Tileset = settings.Tileset;
            Rules = RuleCompiler.Compile(Tileset.ToTilesetData());
            solver = WfcFactory.CreateSolver(settings.Solver);
            solver.Initialize(Rules, settings.Size, settings.Seed, WfcFactory.CreateSettings(settings.Selector, settings.Contradiction, settings.MaxAttempts));
        }

        public TilesetDefinition Tileset { get; }

        public CompiledRules Rules { get; }

        public bool IsFinished => solver.IsFinished;

        /// <summary>Wynik po zakończeniu generacji, wcześniej null.</summary>
        public GenerationResult Result => solver.Result;

        /// <summary>Numer bieżącej próby.</summary>
        public int Attempt => solver.Metrics.Attempts;

        public int CellCount => solver.Grid.CellCount;

        /// <summary>Komórki z jednym stanem w bieżącej próbie; po sprzeczności ta liczba może spaść.</summary>
        public int DecidedCells
        {
            get
            {
                int decided = 0;
                for (int cell = 0; cell < CellCount; cell++)
                    decided += solver.GetState(cell) >= 0 ? 1 : 0;
                return decided;
            }
        }

        /// <summary>Co najmniej jeden krok rozwiązywacza, potem kolejne, aż minie budżet czasu.</summary>
        public void Advance(double budgetMs)
        {
            var watch = Stopwatch.StartNew();
            do
                solver.Step();
            while (!solver.IsFinished && watch.Elapsed.TotalMilliseconds < budgetMs);
        }

        /// <summary>Opis wyniku po polsku, ten sam co w scenie Generator.</summary>
        public string Describe(double buildMs)
        {
            GenerationResult result = solver.Result;
            RuleCheckReport check = result.Success ? RuleChecker.Check(Rules, result.Size, result.States) : null;
            StructureStats stats = result.Success ? StructureAnalyzer.Analyze(Rules, result.Size, result.States) : null;
            return GeneratorController.Describe(result, check, stats, buildMs);
        }
    }
}
