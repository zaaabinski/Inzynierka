using System;

namespace WFC.Core
{
    /// <summary>
    /// Wspólna część heurystyk z kolejką priorytetową: komórka trafia do kolejki przy każdej zmianie, z kluczem
    /// liczonym przez heurystykę; remis rozstrzyga drobny losowy dodatek, losowany raz na komórkę na początku próby.
    /// </summary>
    public abstract class PriorityCellSelector : ICellSelector
    {
        /// <summary>Największy losowy dodatek; mniejszy niż różnice entropii między różnymi zbiorami możliwości.</summary>
        public const double NoiseScale = 1e-6;

        readonly CellPriorityQueue queue = new CellPriorityQueue();
        double[] noise = Array.Empty<double>();
        int[] remaining = Array.Empty<int>();
        int[] version = Array.Empty<int>();

        protected GridShape Grid { get; private set; }

        public abstract string Name { get; }

        public void Reset(GridShape grid, ref DeterministicRandom random)
        {
            Grid = grid;
            int cells = grid.CellCount;
            if (noise.Length != cells)
            {
                noise = new double[cells];
                remaining = new int[cells];
                version = new int[cells];
            }
            for (int cell = 0; cell < cells; cell++)
            {
                noise[cell] = random.NextDouble() * NoiseScale;
                remaining[cell] = 0;
                version[cell] = 0;
            }
            queue.Clear();
        }

        public void OnCellChanged(int cell, int count, double entropy)
        {
            remaining[cell] = count;
            version[cell]++;
            if (count > 1)
                queue.Push(new CellPriorityQueue.Entry(Primary(cell, count), Key(count, entropy) + noise[cell], cell, version[cell]));
        }

        public int SelectNext()
        {
            while (queue.TryPop(out CellPriorityQueue.Entry entry))
                if (entry.Version == version[entry.Cell] && remaining[entry.Cell] > 1)
                    return entry.Cell;
            return -1;
        }

        /// <summary>Dodatek losowy (8 bajtów), liczba możliwości i numer wersji (po 4 bajty) na komórkę oraz kolejka.</summary>
        public long MemoryBytes => (long)noise.Length * 16 + (long)queue.PeakCount * CellPriorityQueue.EntryBytes;

        /// <summary>Pierwsze kryterium porównania (mniejsze najpierw).</summary>
        protected abstract int Primary(int cell, int remaining);

        /// <summary>Drugie kryterium porównania (mniejsze najpierw), przed dodaniem losowego dodatku.</summary>
        protected abstract double Key(int remaining, double entropy);
    }

    /// <summary>
    /// Piętrami od dołu, a w piętrze komórka o najmniejszej entropii Shannona. Domyślna kolejność, taka jak w prototypie.
    /// </summary>
    public sealed class LevelFirstSelector : PriorityCellSelector
    {
        public override string Name => "LevelFirst";

        protected override int Primary(int cell, int remaining) => Grid.Floor(cell);

        protected override double Key(int remaining, double entropy) => entropy;
    }

    /// <summary>Komórka o najmniejszej entropii Shannona z wagami w całej siatce (klasyczne WFC).</summary>
    public sealed class EntropySelector : PriorityCellSelector
    {
        public override string Name => "Entropy";

        protected override int Primary(int cell, int remaining) => 0;

        protected override double Key(int remaining, double entropy) => entropy;
    }

    /// <summary>
    /// MRV (minimum remaining values): komórka z najmniejszą liczbą możliwości w całej siatce, bez patrzenia na wagi.
    /// </summary>
    public sealed class MinRemainingSelector : PriorityCellSelector
    {
        public override string Name => "MinRemaining";

        protected override int Primary(int cell, int remaining) => remaining;

        protected override double Key(int remaining, double entropy) => 0;
    }

    /// <summary>
    /// Scanline: komórki po kolei według numeracji siatki, czyli piętrami od dołu, w piętrze rzędami od S do N,
    /// w rzędzie od W do E. Pomija komórki, które propagacja już ustaliła. Niczego nie losuje.
    /// </summary>
    public sealed class ScanlineSelector : ICellSelector
    {
        int[] remaining = Array.Empty<int>();
        int cursor;

        public string Name => "Scanline";

        public void Reset(GridShape grid, ref DeterministicRandom random)
        {
            if (remaining.Length != grid.CellCount)
                remaining = new int[grid.CellCount];
            Array.Clear(remaining, 0, remaining.Length);
            cursor = 0;
        }

        public void OnCellChanged(int cell, int count, double entropy) => remaining[cell] = count;

        /// <summary>Liczba możliwości (4 bajty) na komórkę.</summary>
        public long MemoryBytes => (long)remaining.Length * 4;

        public int SelectNext()
        {
            // Możliwości tylko ubywają, więc komórka przed kursorem nie może znowu mieć więcej niż 1 możliwości.
            while (cursor < remaining.Length && remaining[cursor] <= 1)
                cursor++;
            return cursor < remaining.Length ? cursor : -1;
        }
    }
}
