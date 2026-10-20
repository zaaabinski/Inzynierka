namespace WFC.Core
{
    /// <summary>
    /// Kolejność wyboru komórek (heurystyka). Rozwiązywacz zgłasza każdą zmianę liczby możliwości komórki,
    /// a selektor na tej podstawie wskazuje komórkę do następnej decyzji.
    /// </summary>
    public interface ICellSelector
    {
        string Name { get; }

        /// <summary>
        /// Początek próby. Tu (i tylko tu) selektor może losować, np. drobny dodatek do entropii każdej komórki;
        /// potem generator liczb losowych służy już tylko do wyboru stanów.
        /// </summary>
        void Reset(GridShape grid, ref DeterministicRandom random);

        /// <param name="remaining">Liczba możliwości komórki, co najmniej 1.</param>
        /// <param name="entropy">Entropia Shannona komórki z wagami.</param>
        void OnCellChanged(int cell, int remaining, double entropy);

        /// <summary>Komórka do następnej decyzji (ma więcej niż 1 możliwość) albo −1, gdy takich nie ma.</summary>
        int SelectNext();
    }
}
