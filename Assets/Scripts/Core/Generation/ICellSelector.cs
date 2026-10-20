using System.Collections.Generic;

namespace WFC.Core
{
    /// <summary>
    /// Kolejność wyboru komórek. Rozwiązywacz zgłasza każdą zmianę liczby możliwości komórki,
    /// a selektor trzyma kolejkę priorytetową z unieważnianymi wpisami: stary wpis komórki zostaje w kolejce,
    /// ale przy zdjęciu jest pomijany, bo ma nieaktualny numer zmiany.
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

    /// <summary>
    /// Piętrami od dołu, a w piętrze komórka o najmniejszej entropii Shannona; remis rozstrzyga drobny losowy
    /// dodatek, stały dla komórki w całej próbie. Domyślna kolejność, taka jak w prototypie.
    /// </summary>
    public sealed class LevelFirstSelector : ICellSelector
    {
        /// <summary>Największy losowy dodatek do entropii; mniejszy niż różnice entropii między różnymi zbiorami możliwości.</summary>
        public const double NoiseScale = 1e-6;

        readonly CellPriorityQueue queue = new CellPriorityQueue();
        GridShape grid;
        double[] noise = System.Array.Empty<double>();
        int[] remaining = System.Array.Empty<int>();
        int[] version = System.Array.Empty<int>();

        public string Name => "LevelFirst";

        public void Reset(GridShape newGrid, ref DeterministicRandom random)
        {
            grid = newGrid;
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
                queue.Push(new CellPriorityQueue.Entry(grid.Floor(cell), entropy + noise[cell], cell, version[cell]));
        }

        public int SelectNext()
        {
            while (queue.TryPop(out CellPriorityQueue.Entry entry))
                if (entry.Version == version[entry.Cell] && remaining[entry.Cell] > 1)
                    return entry.Cell;
            return -1;
        }
    }

    /// <summary>Kopiec binarny: najmniejszy najpierw według (Primary, Key, Cell).</summary>
    public sealed class CellPriorityQueue
    {
        public readonly struct Entry
        {
            public Entry(int primary, double key, int cell, int version)
            {
                Primary = primary;
                Key = key;
                Cell = cell;
                Version = version;
            }

            public int Primary { get; }
            public double Key { get; }
            public int Cell { get; }
            public int Version { get; }

            public bool Precedes(in Entry other)
            {
                if (Primary != other.Primary)
                    return Primary < other.Primary;
                if (Key != other.Key)
                    return Key < other.Key;
                return Cell < other.Cell;
            }
        }

        readonly List<Entry> heap = new List<Entry>();

        public int Count => heap.Count;

        public void Clear() => heap.Clear();

        public void Push(in Entry entry)
        {
            heap.Add(entry);
            int index = heap.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!heap[index].Precedes(heap[parent]))
                    break;
                (heap[index], heap[parent]) = (heap[parent], heap[index]);
                index = parent;
            }
        }

        public bool TryPop(out Entry entry)
        {
            if (heap.Count == 0)
            {
                entry = default;
                return false;
            }
            entry = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= heap.Count)
                    break;
                int smallest = left + 1 < heap.Count && heap[left + 1].Precedes(heap[left]) ? left + 1 : left;
                if (!heap[smallest].Precedes(heap[index]))
                    break;
                (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
                index = smallest;
            }
            return true;
        }
    }
}
