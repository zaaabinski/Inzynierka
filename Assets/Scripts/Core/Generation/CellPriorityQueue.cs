using System.Collections.Generic;

namespace WFC.Core
{
    /// <summary>
    /// Kopiec binarny: najmniejszy najpierw według (Primary, Key, Cell). Wpisy się nie usuwa: po zmianie komórki
    /// dochodzi nowy wpis z wyższym numerem wersji, a stary jest pomijany przy zdjęciu (unieważnione wpisy).
    /// </summary>
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

        /// <summary>Rozmiar jednego wpisu w bajtach: 3 liczby int i 1 double, z wyrównaniem do 8 bajtów.</summary>
        public const int EntryBytes = 24;

        public int Count => heap.Count;

        /// <summary>Największa liczba wpisów od ostatniego Clear.</summary>
        public int PeakCount { get; private set; }

        public void Clear()
        {
            heap.Clear();
            PeakCount = 0;
        }

        public void Push(in Entry entry)
        {
            heap.Add(entry);
            if (heap.Count > PeakCount)
                PeakCount = heap.Count;
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
