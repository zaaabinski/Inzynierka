namespace WFC.Core
{
    /// <summary>
    /// Kolejka komórek FIFO bez powtórzeń: znacznik na komórkę mówi, czy już w niej czeka. Tablica cykliczna
    /// o pojemności równej liczbie komórek, bo każda komórka jest w kolejce najwyżej raz. Pola są wewnętrzne,
    /// żeby propagacja na wskaźnikach (BitsetPropagation, także w Burst) mogła pracować na tych samych tablicach.
    /// </summary>
    public sealed class CellQueue
    {
        internal int[] Items;
        internal byte[] Flags;
        internal int Head;
        internal int Count;

        public CellQueue(int capacity)
        {
            Items = new int[capacity];
            Flags = new byte[capacity];
        }

        public int Capacity => Items.Length;

        public int Length => Count;

        /// <summary>Największa liczba komórek naraz od utworzenia kolejki.</summary>
        public int Peak { get; internal set; }

        /// <summary>Komórka (4 bajty) i znacznik (1 bajt) na każde miejsce.</summary>
        public long MemoryBytes => (long)Capacity * 5;

        public bool Contains(int cell) => Flags[cell] != 0;

        public void Enqueue(int cell)
        {
            if (Flags[cell] != 0)
                return;
            Flags[cell] = 1;
            int tail = Head + Count;
            Items[tail < Items.Length ? tail : tail - Items.Length] = cell;
            Count++;
            if (Count > Peak)
                Peak = Count;
        }

        public bool TryDequeue(out int cell)
        {
            if (Count == 0)
            {
                cell = -1;
                return false;
            }
            cell = Items[Head];
            Head = Head + 1 == Items.Length ? 0 : Head + 1;
            Count--;
            Flags[cell] = 0;
            return true;
        }

        /// <summary>Opróżnia kolejkę, np. po sprzeczności w połowie propagacji.</summary>
        public void Clear()
        {
            while (TryDequeue(out _))
            {
            }
            Head = 0;
        }
    }
}
