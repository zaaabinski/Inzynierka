using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>Sposób dopasowania etykiety bocznej (kolumna „rodzaj” w pliku reguł).</summary>
    public enum SideLabelKind
    {
        /// <summary>do_siebie: etykieta pasuje do samej siebie, np. grunt do gruntu.</summary>
        Self,

        /// <summary>para_odbita: X pasuje do X_odbity, ta sama krawędź widziana z drugiej strony.</summary>
        MirroredPair,

        /// <summary>z_listy: etykieta pasuje tylko do etykiet wypisanych w parach zgodnych.</summary>
        FromList
    }

    public sealed class SideLabelInfo
    {
        public string Name { get; set; }
        public SideLabelKind Kind { get; set; }
        public string Description { get; set; }
    }

    public sealed class VerticalLabelInfo
    {
        /// <summary>Nazwa z gwiazdką, jeśli etykieta zależy od obrotu, np. kolumna_muru*.</summary>
        public string Name { get; set; }
        public bool SameRotation { get; set; }
        public string Description { get; set; }
    }

    /// <summary>Rozmiar siatki w układzie reguł: x = E, y = N, z = góra.</summary>
    public sealed class GridSettings
    {
        public int CellsEast { get; set; } = 20;
        public int CellsNorth { get; set; } = 20;
        public int Floors { get; set; } = 13;
        public double CellSize { get; set; } = 3.0;

        /// <summary>Rozmiar w siatce programu w Unity: x = E, y = piętro, z = N.</summary>
        public int3 UnitySize => new int3(CellsEast, Floors, CellsNorth);
    }

    /// <summary>Nazwany zestaw: lista nazw kafelków w kolejności z pliku.</summary>
    public sealed class TileList
    {
        public string Name { get; set; }
        public List<string> TileNames { get; } = new List<string>();
    }

    public struct ControlCounts
    {
        public int Tiles;
        public int States;

        public ControlCounts(int tiles, int states)
        {
            Tiles = tiles;
            States = states;
        }
    }

    /// <summary>Sekcja „kontrola” pliku reguł: liczby, które program musi odtworzyć.</summary>
    public sealed class ControlSection
    {
        public int Tiles { get; set; }
        public int States { get; set; }
        public int SideLabels { get; set; }
        public int VerticalLabels { get; set; }
        public int CompatiblePairs { get; set; }
        public Dictionary<string, ControlCounts> Sets { get; } = new Dictionary<string, ControlCounts>();
    }

    /// <summary>Cała zawartość pliku wfc_rules_zamek.json po wczytaniu przez RulesJsonReader.</summary>
    public sealed class RulesFile
    {
        public string Version { get; set; }
        public string Description { get; set; }
        public GridSettings Grid { get; set; } = new GridSettings();
        public MapEdgeRules MapEdge { get; set; } = new MapEdgeRules();
        public List<VerticalException> VerticalExceptions { get; } = new List<VerticalException>();
        public List<TileData> Tiles { get; } = new List<TileData>();
        public List<SideLabelInfo> SideLabels { get; } = new List<SideLabelInfo>();
        public List<VerticalLabelInfo> VerticalLabels { get; } = new List<VerticalLabelInfo>();
        public List<LabelPair> CompatiblePairs { get; } = new List<LabelPair>();

        /// <summary>Lista paryDodatkowe: te same pary co w paryZgodne, zapisane w drugiej kolejności.</summary>
        public List<LabelPair> ExtraPairs { get; } = new List<LabelPair>();

        public List<TileList> Sets { get; } = new List<TileList>();
        public ControlSection Control { get; set; } = new ControlSection();

        public TileData FindTile(string name) => Tiles.Find(tile => tile.Name == name);

        public TileList FindSet(string name) => Sets.Find(set => set.Name == name);

        /// <summary>Zestaw ze wszystkimi kafelkami z pliku, w kolejności listy kafelki.</summary>
        public TilesetData CreateTilesetWithAllTiles(string name = "Wszystkie")
        {
            var names = new List<string>();
            foreach (TileData tile in Tiles)
                names.Add(tile.Name);
            TilesetData tileset = CreateTileset(name, names);
            tileset.ExpectedTileCount = Control.Tiles;
            tileset.ExpectedStateCount = Control.States;
            return tileset;
        }

        /// <summary>
        /// Zestaw o nazwie z sekcji zestawy, z oczekiwanymi liczbami z sekcji kontrola, albo wariant reguł z RuleVariants
        /// (zestaw bazowy z dopisanymi parami i mnożnikami wag).
        /// </summary>
        public TilesetData CreateTileset(string setName)
        {
            TileList set = FindSet(setName);
            if (set == null && RuleVariants.Find(setName) is RuleVariant variant)
                return variant.Apply(CreateTileset(variant.BaseSet));
            if (set == null)
                throw new ArgumentException($"W pliku reguł nie ma zestawu ani wariantu reguł „{setName}”.", nameof(setName));
            TilesetData tileset = CreateTileset(setName, set.TileNames);
            if (Control.Sets.TryGetValue(setName, out ControlCounts counts))
            {
                tileset.ExpectedTileCount = counts.Tiles;
                tileset.ExpectedStateCount = counts.States;
            }
            return tileset;
        }

        /// <summary>Zestaw z podanych kafelków; reguły łączenia i krawędzie mapy są wspólne dla całego pliku.</summary>
        public TilesetData CreateTileset(string name, IEnumerable<string> tileNames)
        {
            var tileset = new TilesetData { Name = name, MapEdge = MapEdge.Clone() };
            foreach (string tileName in tileNames)
            {
                TileData tile = FindTile(tileName);
                if (tile == null)
                    throw new ArgumentException($"Zestaw „{name}” zawiera kafelek „{tileName}”, którego nie ma na liście kafelki.");
                tileset.Tiles.Add(tile.Clone());
            }
            tileset.CompatiblePairs.AddRange(CompatiblePairs);
            tileset.VerticalExceptions.AddRange(VerticalExceptions);
            return tileset;
        }
    }
}
