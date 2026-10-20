using System;
using System.Collections.Generic;

namespace WFC.Core
{
    /// <summary>
    /// Zamienia zestaw kafelków w CompiledRules: rozwija obroty, liczy dla każdego stanu i kierunku dozwolonych
    /// sąsiadów i buduje maski krawędzi siatki. Zasady jak w pliku reguł (sekcja konwencje):
    /// boki pasują, gdy para etykiet jest na liście par zgodnych; pionowo góra stanu pod spodem musi być równa
    /// dołowi stanu nad nim, z wyjątkami z listy wyjatkiPionowe.
    /// </summary>
    public static class RuleCompiler
    {
        /// <summary>Gwiazdka na końcu etykiety pionowej: kostka nad i pod musi mieć ten sam obrót.</summary>
        public const char RotationDependentMark = '*';

        /// <summary>Znak między etykietą a numerem obrotu w etykiecie pionowej po kompilacji, np. kolumna_muru@90.</summary>
        public const char RotationSeparator = '@';

        public static CompiledRules Compile(TilesetData tileset)
        {
            if (tileset == null)
                throw new ArgumentNullException(nameof(tileset));

            var pairs = new HashSet<(string, string)>();
            foreach (LabelPair pair in tileset.CompatiblePairs)
            {
                pairs.Add((pair.A, pair.B));
                pairs.Add((pair.B, pair.A));
            }
            var exceptions = new HashSet<(string, string)>();
            foreach (VerticalException exception in tileset.VerticalExceptions)
                exceptions.Add((exception.Below, exception.Above));

            // Stany: kafelki w kolejności zestawu, każdy w obrotach 0 … obroty − 1.
            var tiles = new List<int>();
            var rotations = new List<int>();
            var weights = new List<double>();
            for (int tileIndex = 0; tileIndex < tileset.Tiles.Count; tileIndex++)
            {
                TileData tile = tileset.Tiles[tileIndex];
                CheckTile(tile);
                double weight = tile.Weight * tileset.GetWeightFactor(tile);
                for (int rotation = 0; rotation < tile.Rotations; rotation++)
                {
                    tiles.Add(tileIndex);
                    rotations.Add(rotation);
                    weights.Add(weight);
                }
            }

            int stateCount = tiles.Count;
            int words = BitMask.WordCount(stateCount);
            var names = new string[stateCount];
            var labels = new string[stateCount * DirectionExtensions.Count];
            var weightLogWeight = new double[stateCount];
            for (int state = 0; state < stateCount; state++)
            {
                TileData tile = tileset.Tiles[tiles[state]];
                names[state] = $"{tile.Name}{RotationSeparator}{rotations[state] * 90}";
                foreach (Direction direction in DirectionExtensions.All)
                    labels[state * DirectionExtensions.Count + (int)direction] = RotatedLabel(tile, direction, rotations[state]);
                weightLogWeight[state] = weights[state] > 0 ? weights[state] * Math.Log(weights[state]) : 0;
            }

            string Label(int state, Direction direction) => labels[state * DirectionExtensions.Count + (int)direction];
            bool VerticalFits(string below, string above) => below == above || exceptions.Contains((below, above));

            bool Fits(int state, Direction direction, int neighbor)
            {
                switch (direction)
                {
                    case Direction.Up: return VerticalFits(Label(state, Direction.Up), Label(neighbor, Direction.Down));
                    case Direction.Down: return VerticalFits(Label(neighbor, Direction.Up), Label(state, Direction.Down));
                    default: return pairs.Contains((Label(state, direction), Label(neighbor, direction.Opposite())));
                }
            }

            // Sąsiedztwo: lista i maska dla każdej pary (stan, kierunek).
            var neighborStart = new int[stateCount * DirectionExtensions.Count + 1];
            var neighborStates = new List<int>();
            var neighborMasks = new ulong[stateCount * DirectionExtensions.Count * words];
            for (int state = 0; state < stateCount; state++)
                foreach (Direction direction in DirectionExtensions.All)
                {
                    int index = state * DirectionExtensions.Count + (int)direction;
                    neighborStart[index] = neighborStates.Count;
                    Span<ulong> mask = neighborMasks.AsSpan(index * words, words);
                    for (int neighbor = 0; neighbor < stateCount; neighbor++)
                        if (Fits(state, direction, neighbor))
                        {
                            neighborStates.Add(neighbor);
                            BitMask.Set(mask, neighbor);
                        }
                }
            neighborStart[stateCount * DirectionExtensions.Count] = neighborStates.Count;

            // Krawędzie siatki: za bokiem grunt (parter) albo powietrze (wyżej), pod dnem podloze,
            // na najwyższym poziomie tylko góra z listy dozwolonych.
            MapEdgeRules edge = tileset.MapEdge;
            var bottomMask = new ulong[words];
            var topMask = new ulong[words];
            var sideMasksGround = new ulong[DirectionExtensions.SideCount * words];
            var sideMasksAbove = new ulong[DirectionExtensions.SideCount * words];
            for (int state = 0; state < stateCount; state++)
            {
                if (VerticalFits(edge.GridBottom, Label(state, Direction.Down)))
                    BitMask.Set(bottomMask, state);
                if (edge.GridTopAllowed.Contains(Label(state, Direction.Up)))
                    BitMask.Set(topMask, state);
                foreach (Direction side in DirectionExtensions.Sides)
                {
                    if (pairs.Contains((Label(state, side), edge.SideAtGround)))
                        BitMask.Set(sideMasksGround.AsSpan((int)side * words, words), state);
                    if (pairs.Contains((Label(state, side), edge.SideAbove)))
                        BitMask.Set(sideMasksAbove.AsSpan((int)side * words, words), state);
                }
            }

            return new CompiledRules(
                tileset,
                tiles.ToArray(), rotations.ToArray(), weights.ToArray(), weightLogWeight,
                names, labels,
                neighborStart, neighborStates.ToArray(), neighborMasks,
                bottomMask, topMask, sideMasksGround, sideMasksAbove,
                pairs);
        }

        /// <summary>
        /// Etykieta ściany kafelka obróconego o rotation × 90° przeciwnie do ruchu wskazówek zegara.
        /// Bok: obrót przenosi etykietę z E na N, z N na W, z W na S i z S na E, więc na stronie direction stoi
        /// etykieta, która w obrocie 0 była na stronie obróconej o −rotation.
        /// Pion: etykieta z gwiazdką dostaje numer obrotu (kolumna_muru* w obrocie 1 → kolumna_muru@90);
        /// pozostałe są niezależne od obrotu.
        /// </summary>
        public static string RotatedLabel(TileData tile, Direction direction, int rotation)
        {
            if (direction.IsSide())
                return tile.GetLabel(direction.Rotate(-rotation));
            string label = tile.GetLabel(direction);
            if (label.Length > 0 && label[label.Length - 1] == RotationDependentMark)
                return $"{label.Substring(0, label.Length - 1)}{RotationSeparator}{(((rotation % 4) + 4) % 4) * 90}";
            return label;
        }

        static void CheckTile(TileData tile)
        {
            if (string.IsNullOrEmpty(tile.Name))
                throw new ArgumentException("Kafelek bez nazwy.");
            if (tile.Rotations < 1 || tile.Rotations > DirectionExtensions.SideCount)
                throw new ArgumentException($"Kafelek {tile.Name} ma {tile.Rotations} obrotów; dozwolone 1–4.");
            foreach (Direction direction in DirectionExtensions.All)
                if (string.IsNullOrEmpty(tile.GetLabel(direction)))
                    throw new ArgumentException($"Kafelek {tile.Name} nie ma etykiety ściany {direction.RulesKey()}.");
        }
    }
}
