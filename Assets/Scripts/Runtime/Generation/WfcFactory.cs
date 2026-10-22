using System;
using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>Rozwiązywacz (sposób przechowywania możliwości i propagacji). Wszystkie dają te same mapy.</summary>
    public enum SolverKind
    {
        [InspectorName("Naive: listy możliwości")]
        Naive,

        [InspectorName("SupportCount: liczniki wsparcia")]
        SupportCount,

        [InspectorName("Bitset: maski bitowe")]
        Bitset,

        [InspectorName("Burst: maski bitowe, kompilator Burst")]
        Burst
    }

    /// <summary>Heurystyka wyboru komórki (kolejność decyzji).</summary>
    public enum SelectorKind
    {
        [InspectorName("Piętrami, entropia w piętrze")]
        LevelFirst,

        [InspectorName("Scanline: po kolei")]
        Scanline,

        [InspectorName("MRV: najmniej możliwości")]
        MinRemaining,

        [InspectorName("Najmniejsza entropia")]
        Entropy,

        [InspectorName("Losowo")]
        Random
    }

    /// <summary>Reakcja na sprzeczność.</summary>
    public enum ContradictionKind
    {
        [InspectorName("Start od nowa")]
        Restart,

        [InspectorName("Cofanie decyzji")]
        Backtrack,

        [InspectorName("Reset lokalny")]
        LocalReset
    }

    /// <summary>Tworzy rozwiązywacz, heurystykę i reakcję na sprzeczność z ustawień; wspólne dla sceny, badań i okna.</summary>
    public static class WfcFactory
    {
        public static IWfcSolver CreateSolver(SolverKind kind)
        {
            switch (kind)
            {
                case SolverKind.Naive: return new NaiveSolver();
                case SolverKind.SupportCount: return new SupportCountSolver();
                case SolverKind.Bitset: return new BitsetSolver();
                case SolverKind.Burst: return new BurstSolver();
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static ICellSelector CreateSelector(SelectorKind kind)
        {
            switch (kind)
            {
                case SelectorKind.LevelFirst: return new LevelFirstSelector();
                case SelectorKind.Scanline: return new ScanlineSelector();
                case SelectorKind.MinRemaining: return new MinRemainingSelector();
                case SelectorKind.Entropy: return new EntropySelector();
                case SelectorKind.Random: return new RandomSelector();
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static IContradictionHandler CreateContradictionHandler(ContradictionKind kind)
        {
            switch (kind)
            {
                case ContradictionKind.Restart: return new RestartHandler();
                case ContradictionKind.Backtrack: return new BacktrackHandler();
                case ContradictionKind.LocalReset: return new LocalResetHandler();
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static SolverSettings CreateSettings(SelectorKind selector, ContradictionKind contradiction, int maxAttempts, double timeLimitSeconds = 0) =>
            new SolverSettings
            {
                Selector = CreateSelector(selector),
                ContradictionHandler = CreateContradictionHandler(contradiction),
                MaxAttempts = maxAttempts,
                TimeLimitSeconds = timeLimitSeconds
            };

        /// <summary>Czy rozwiązywacz umie wykonać tę reakcję na sprzeczność (SupportCount na razie tylko start od nowa).</summary>
        public static bool IsSupported(SolverKind solver, ContradictionKind contradiction) =>
            CreateSolver(solver).Supports(CreateContradictionHandler(contradiction));

        /// <summary>Polska nazwa wartości z atrybutu InspectorName, do list w oknie ustawień.</summary>
        public static string Label<T>(T value) where T : Enum
        {
            var member = typeof(T).GetField(value.ToString());
            var attribute = member != null ? (InspectorNameAttribute)Attribute.GetCustomAttribute(member, typeof(InspectorNameAttribute)) : null;
            return attribute != null ? attribute.displayName : value.ToString();
        }
    }
}
