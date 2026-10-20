namespace WFC.Core
{
    public enum ContradictionAction
    {
        /// <summary>Start od nowa z kolejną próbą (nowy generator liczb losowych z tego samego ziarna mapy).</summary>
        Restart,

        /// <summary>Koniec generacji bez mapy.</summary>
        Abort
    }

    /// <summary>Reakcja na sprzeczność, czyli komórkę bez żadnej możliwości.</summary>
    public interface IContradictionHandler
    {
        string Name { get; }

        /// <param name="attempts">Liczba rozpoczętych prób, łącznie z tą, która skończyła się sprzecznością.</param>
        /// <param name="maxAttempts">Limit prób z ustawień.</param>
        ContradictionAction OnContradiction(int attempts, int maxAttempts);
    }

    /// <summary>Start od nowa z kolejną próbą, bez cofania decyzji (tak działał prototyp).</summary>
    public sealed class RestartHandler : IContradictionHandler
    {
        public string Name => "Restart";

        public ContradictionAction OnContradiction(int attempts, int maxAttempts) =>
            attempts < maxAttempts ? ContradictionAction.Restart : ContradictionAction.Abort;
    }
}
