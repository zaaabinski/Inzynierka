using System;
using System.Collections.Generic;

namespace WFC.Core
{
    /// <summary>
    /// Reguły zestawu po kompilacji, tylko do odczytu. Stan = kafelek w jednym obrocie; stany są numerowane
    /// w kolejności kafelków zestawu, a w kafelku w kolejności obrotów 0 … obroty − 1.
    /// Sąsiedztwo jest zapisane dwa razy: jako listy numerów stanów i jako maski bitowe (WordCount liczb ulong
    /// na stan i kierunek). Wszystkie rozwiązywacze dostają te same dane.
    /// </summary>
    public sealed class CompiledRules
    {
        readonly int[] stateTile;
        readonly int[] stateRotation;
        readonly double[] stateWeight;
        readonly double[] stateWeightLogWeight;
        readonly string[] stateNames;
        readonly string[] stateLabels;
        readonly int[] neighborStart;
        readonly int[] neighborStates;
        readonly ulong[] neighborMasks;
        readonly ulong[] bottomMask;
        readonly ulong[] topMask;
        readonly ulong[] sideMasksGround;
        readonly ulong[] sideMasksAbove;
        readonly HashSet<(string, string)> compatibleSideLabels;

        internal CompiledRules(
            TilesetData tileset,
            int[] stateTile, int[] stateRotation, double[] stateWeight, double[] stateWeightLogWeight,
            string[] stateNames, string[] stateLabels,
            int[] neighborStart, int[] neighborStates, ulong[] neighborMasks,
            ulong[] bottomMask, ulong[] topMask, ulong[] sideMasksGround, ulong[] sideMasksAbove,
            HashSet<(string, string)> compatibleSideLabels)
        {
            TilesetName = tileset.Name;
            Tiles = tileset.Tiles.ConvertAll(tile => tile.Clone()).ToArray();
            MapEdge = tileset.MapEdge.Clone();
            VerticalExceptions = tileset.VerticalExceptions.ToArray();
            ExpectedTileCount = tileset.ExpectedTileCount;
            ExpectedStateCount = tileset.ExpectedStateCount;
            DisabledChecks = tileset.DisabledChecks.ToArray();
            StateCount = stateTile.Length;
            WordCount = BitMask.WordCount(StateCount);
            this.stateTile = stateTile;
            this.stateRotation = stateRotation;
            this.stateWeight = stateWeight;
            this.stateWeightLogWeight = stateWeightLogWeight;
            this.stateNames = stateNames;
            this.stateLabels = stateLabels;
            this.neighborStart = neighborStart;
            this.neighborStates = neighborStates;
            this.neighborMasks = neighborMasks;
            this.bottomMask = bottomMask;
            this.topMask = topMask;
            this.sideMasksGround = sideMasksGround;
            this.sideMasksAbove = sideMasksAbove;
            this.compatibleSideLabels = compatibleSideLabels;
        }

        public string TilesetName { get; }

        public IReadOnlyList<TileData> Tiles { get; }

        public MapEdgeRules MapEdge { get; }

        public IReadOnlyList<VerticalException> VerticalExceptions { get; }

        /// <summary>Liczba kafelków z sekcji kontrola; 0 = bez sprawdzania.</summary>
        public int ExpectedTileCount { get; }

        /// <summary>Liczba stanów z sekcji kontrola; 0 = bez sprawdzania.</summary>
        public int ExpectedStateCount { get; }

        /// <summary>Sprawdzenia RuleChecker wyłączone w tym zestawie (warianty reguł E6).</summary>
        public IReadOnlyList<ViolationType> DisabledChecks { get; }

        public int TileCount => Tiles.Count;

        public int StateCount { get; }

        /// <summary>Liczba słów ulong w jednej masce stanów (3 dla 170 stanów).</summary>
        public int WordCount { get; }

        /// <summary>Liczba uporządkowanych par (stan, kierunek, sąsiad) dozwolonych przez reguły.</summary>
        public int NeighborPairCount => neighborStates.Length;

        public int GetTileIndex(int state) => stateTile[state];

        public TileData GetTile(int state) => Tiles[stateTile[state]];

        /// <summary>Numer obrotu k: kafelek obrócony o k × 90° przeciwnie do ruchu wskazówek zegara.</summary>
        public int GetRotation(int state) => stateRotation[state];

        public double GetWeight(int state) => stateWeight[state];

        /// <summary>Waga × logarytm naturalny wagi, do liczenia entropii Shannona.</summary>
        public double GetWeightLogWeight(int state) => stateWeightLogWeight[state];

        /// <summary>Nazwa stanu, np. Mur_Dol@90.</summary>
        public string GetStateName(int state) => stateNames[state];

        /// <summary>Etykieta ściany stanu po obrocie; pionowe z gwiazdką mają numer obrotu, np. kolumna_muru@90.</summary>
        public string GetLabel(int state, Direction direction) => stateLabels[state * DirectionExtensions.Count + (int)direction];

        /// <summary>Stany, które mogą stać w kierunku direction od stanu state, rosnąco.</summary>
        public ReadOnlySpan<int> GetNeighbors(int state, Direction direction)
        {
            int index = state * DirectionExtensions.Count + (int)direction;
            return new ReadOnlySpan<int>(neighborStates, neighborStart[index], neighborStart[index + 1] - neighborStart[index]);
        }

        /// <summary>Wszystkie maski sąsiedztwa w jednej tablicy: indeks (stan × 6 + kierunek) × WordCount, do propagacji na wskaźnikach.</summary>
        internal ulong[] NeighborMaskArray => neighborMasks;

        /// <summary>Te same stany co GetNeighbors jako maska bitowa.</summary>
        public ReadOnlySpan<ulong> GetNeighborMask(int state, Direction direction) =>
            new ReadOnlySpan<ulong>(neighborMasks, (state * DirectionExtensions.Count + (int)direction) * WordCount, WordCount);

        /// <summary>Czy stan neighbor może stać w kierunku direction od stanu state.</summary>
        public bool CanBeNeighbors(int state, Direction direction, int neighbor) =>
            BitMask.Get(GetNeighborMask(state, direction), neighbor);

        /// <summary>Stany dozwolone na najniższym poziomie siatki (dół pasuje do „podloze”).</summary>
        public ReadOnlySpan<ulong> BottomMask => bottomMask;

        /// <summary>Stany dozwolone na najwyższym poziomie siatki (góra „powietrze” albo „puste”).</summary>
        public ReadOnlySpan<ulong> TopMask => topMask;

        /// <summary>Stany, które mogą stać bokiem side do krawędzi siatki: na parterze grunt, wyżej powietrze.</summary>
        public ReadOnlySpan<ulong> GetSideEdgeMask(Direction side, bool groundFloor)
        {
            if (!side.IsSide())
                throw new ArgumentException($"Kierunek {side} nie jest bokiem.", nameof(side));
            return new ReadOnlySpan<ulong>(groundFloor ? sideMasksGround : sideMasksAbove, (int)side * WordCount, WordCount);
        }

        public bool AreSideLabelsCompatible(string a, string b) => compatibleSideLabels.Contains((a, b));

        /// <summary>Numer stanu kafelka w danym obrocie albo −1, gdy go nie ma.</summary>
        public int FindState(string tileName, int rotation)
        {
            for (int state = 0; state < StateCount; state++)
                if (stateRotation[state] == rotation && Tiles[stateTile[state]].Name == tileName)
                    return state;
            return -1;
        }
    }
}
