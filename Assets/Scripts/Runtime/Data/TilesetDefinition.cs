using System.Collections.Generic;
using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Zestaw kafelków jako zasób Unity: Pelny, PelnyBezDekoracji, Sredni, Prosty. Warianty reguł do eksperymentu E6
    /// to osobne zasoby z inną listą par zgodnych etykiet albo innymi mnożnikami wag.
    /// </summary>
    [CreateAssetMenu(menuName = "WFC/Zestaw kafelków", fileName = "Zestaw")]
    public sealed class TilesetDefinition : ScriptableObject
    {
        [Tooltip("Kolejność kafelków wyznacza numerację stanów.")]
        [SerializeField] List<TileDefinition> tiles = new List<TileDefinition>();
        [Tooltip("Pary etykiet bocznych, które mogą się stykać; kolejność w parze bez znaczenia.")]
        [SerializeField] List<LabelPair> compatiblePairs = new List<LabelPair>();
        [SerializeField] List<VerticalException> verticalExceptions = new List<VerticalException>();
        [SerializeField] MapEdgeRules mapEdge = new MapEdgeRules();
        [Tooltip("Mnożnik wagi dla nazwy kafelka albo grupy, np. Teren × 0,4.")]
        [SerializeField] List<WeightMultiplier> weightMultipliers = new List<WeightMultiplier>();

        [Header("Kontrola (0 = bez sprawdzania)")]
        [SerializeField, Min(0)] int expectedTileCount;
        [SerializeField, Min(0)] int expectedStateCount;
        [Tooltip("Wersja pliku reguł, z którego zaimportowano zestaw.")]
        [SerializeField] string rulesVersion;

        public IReadOnlyList<TileDefinition> Tiles => tiles;
        public IReadOnlyList<LabelPair> CompatiblePairs => compatiblePairs;
        public IReadOnlyList<VerticalException> VerticalExceptions => verticalExceptions;
        public MapEdgeRules MapEdge => mapEdge;
        public IReadOnlyList<WeightMultiplier> WeightMultipliers => weightMultipliers;
        public int ExpectedTileCount => expectedTileCount;
        public int ExpectedStateCount => expectedStateCount;
        public string RulesVersion => rulesVersion;

        public TilesetData ToTilesetData()
        {
            var data = new TilesetData
            {
                Name = name,
                MapEdge = mapEdge.Clone(),
                ExpectedTileCount = expectedTileCount,
                ExpectedStateCount = expectedStateCount
            };
            foreach (TileDefinition tile in tiles)
            {
                if (tile == null)
                    throw new System.InvalidOperationException($"Zestaw {name} ma pusty wpis na liście kafelków.");
                data.Tiles.Add(tile.ToTileData());
            }
            data.CompatiblePairs.AddRange(compatiblePairs);
            data.VerticalExceptions.AddRange(verticalExceptions);
            data.WeightMultipliers.AddRange(weightMultipliers);
            return data;
        }

        /// <summary>Nadpisuje zestaw danymi z pliku reguł (używa RulesImporter); mnożniki wag zostają bez zmian.</summary>
        public void CopyFrom(TilesetData data, IEnumerable<TileDefinition> tileAssets, string version)
        {
            tiles = new List<TileDefinition>(tileAssets);
            compatiblePairs = new List<LabelPair>(data.CompatiblePairs);
            verticalExceptions = new List<VerticalException>(data.VerticalExceptions);
            mapEdge = data.MapEdge.Clone();
            expectedTileCount = data.ExpectedTileCount;
            expectedStateCount = data.ExpectedStateCount;
            rulesVersion = version;
        }
    }
}
