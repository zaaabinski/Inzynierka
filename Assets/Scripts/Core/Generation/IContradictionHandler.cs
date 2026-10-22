using System;

namespace WFC.Core
{
    public enum ContradictionAction
    {
        /// <summary>Start od nowa z kolejną próbą (nowy generator liczb losowych z tego samego ziarna mapy).</summary>
        Restart,

        /// <summary>Koniec generacji bez mapy.</summary>
        Abort,

        /// <summary>Cofnięcie ostatniej decyzji: przywrócenie stanu sprzed niej i wykreślenie wybranego stanu.</summary>
        Backtrack,

        /// <summary>Przywrócenie początkowych możliwości w sześcianie o promieniu ResetRadius wokół sprzeczności.</summary>
        LocalReset
    }

    /// <summary>Czego reakcja na sprzeczność potrzebuje od rozwiązywacza.</summary>
    [Flags]
    public enum ContradictionNeeds
    {
        None = 0,

        /// <summary>Dziennik zmian do cofania decyzji.</summary>
        UndoLog = 1,

        /// <summary>Przywracanie początkowych możliwości wybranych komórek.</summary>
        LocalReset = 2
    }

    /// <summary>Sytuacja w chwili sprzeczności, na podstawie której reakcja wybiera, co dalej.</summary>
    public readonly struct ContradictionContext
    {
        public ContradictionContext(int attempts, int maxAttempts, int decisionDepth, int backtracksInAttempt, int localResetsInAttempt, int cell)
        {
            Attempts = attempts;
            MaxAttempts = maxAttempts;
            DecisionDepth = decisionDepth;
            BacktracksInAttempt = backtracksInAttempt;
            LocalResetsInAttempt = localResetsInAttempt;
            Cell = cell;
        }

        /// <summary>Rozpoczęte próby, łącznie z tą, w której wystąpiła sprzeczność.</summary>
        public int Attempts { get; }

        /// <summary>Limit prób z ustawień.</summary>
        public int MaxAttempts { get; }

        /// <summary>Decyzje w bieżącej próbie, które da się jeszcze cofnąć (0 bez dziennika zmian).</summary>
        public int DecisionDepth { get; }

        public int BacktracksInAttempt { get; }

        public int LocalResetsInAttempt { get; }

        /// <summary>Komórka bez żadnej możliwości.</summary>
        public int Cell { get; }

        /// <summary>Start od nowa, jeśli limit prób na to pozwala, inaczej koniec bez mapy.</summary>
        public ContradictionAction RestartOrAbort => Attempts < MaxAttempts ? ContradictionAction.Restart : ContradictionAction.Abort;
    }

    /// <summary>Reakcja na sprzeczność, czyli komórkę bez żadnej możliwości.</summary>
    public interface IContradictionHandler
    {
        string Name { get; }

        ContradictionNeeds Needs { get; }

        /// <summary>Promień resetu lokalnego w komórkach; 0, gdy reakcja go nie używa.</summary>
        int ResetRadius { get; }

        ContradictionAction OnContradiction(in ContradictionContext context);
    }

    /// <summary>Start od nowa z kolejną próbą, bez cofania decyzji (tak działał prototyp).</summary>
    public sealed class RestartHandler : IContradictionHandler
    {
        public string Name => "Restart";

        public ContradictionNeeds Needs => ContradictionNeeds.None;

        public int ResetRadius => 0;

        public ContradictionAction OnContradiction(in ContradictionContext context) => context.RestartOrAbort;
    }

    /// <summary>
    /// Cofanie decyzji (backtracking chronologiczny): przy sprzeczności rozwiązywacz przywraca z dziennika zmian stan
    /// sprzed ostatniej decyzji, wykreśla wybrany wtedy stan i propaguje dalej; jeśli znowu jest sprzeczność, cofa
    /// kolejną decyzję. Po MaxBacktracks cofnięciach w jednej próbie albo po cofnięciu wszystkich decyzji – start od nowa.
    /// </summary>
    public sealed class BacktrackHandler : IContradictionHandler
    {
        public const int DefaultMaxBacktracks = 1000;

        public BacktrackHandler(int maxBacktracks = DefaultMaxBacktracks)
        {
            MaxBacktracks = Math.Max(1, maxBacktracks);
        }

        /// <summary>Limit cofnięć w jednej próbie.</summary>
        public int MaxBacktracks { get; }

        public string Name => "Backtrack";

        public ContradictionNeeds Needs => ContradictionNeeds.UndoLog;

        public int ResetRadius => 0;

        public ContradictionAction OnContradiction(in ContradictionContext context) =>
            context.DecisionDepth > 0 && context.BacktracksInAttempt < MaxBacktracks ? ContradictionAction.Backtrack : context.RestartOrAbort;
    }

    /// <summary>
    /// Reset lokalny: komórki w sześcianie o promieniu Radius wokół sprzeczności (także w pionie) wracają do możliwości
    /// z początku próby, a propagacja od ich sąsiadów zostawia tylko stany zgodne z resztą mapy. Decyzje poza sześcianem
    /// zostają. Po MaxResets resetach w jednej próbie – start od nowa.
    /// </summary>
    public sealed class LocalResetHandler : IContradictionHandler
    {
        public const int DefaultRadius = 2;
        public const int DefaultMaxResets = 100;

        public LocalResetHandler(int radius = DefaultRadius, int maxResets = DefaultMaxResets)
        {
            Radius = Math.Max(0, radius);
            MaxResets = Math.Max(1, maxResets);
        }

        public int Radius { get; }

        /// <summary>Limit resetów w jednej próbie.</summary>
        public int MaxResets { get; }

        public string Name => "LocalReset";

        public ContradictionNeeds Needs => ContradictionNeeds.LocalReset;

        public int ResetRadius => Radius;

        public ContradictionAction OnContradiction(in ContradictionContext context) =>
            context.Cell >= 0 && context.LocalResetsInAttempt < MaxResets ? ContradictionAction.LocalReset : context.RestartOrAbort;
    }
}
