using System;
using System.Collections.Generic;

namespace WFC.Core
{
    /// <summary>Dwie etykiety boczne, które mogą się stykać; kolejność w parze nie ma znaczenia.</summary>
    [Serializable]
    public struct LabelPair
    {
        public string A;
        public string B;

        public LabelPair(string a, string b)
        {
            A = a;
            B = b;
        }

        public override string ToString() => $"{A} – {B}";
    }

    /// <summary>
    /// Wyjątek od zasady „pionowo pasują tylko identyczne etykiety”: na górze Below może stanąć dół Above.
    /// W regułach zamku jedyny wyjątek to puste (góra Powietrza) pod powietrzem (dół Powietrza).
    /// </summary>
    [Serializable]
    public struct VerticalException
    {
        public string Below;
        public string Above;

        public VerticalException(string below, string above)
        {
            Below = below;
            Above = above;
        }

        public override string ToString() => $"{Below} pod {Above}";
    }

    /// <summary>Co algorytm zakłada za krawędzią siatki.</summary>
    [Serializable]
    public sealed class MapEdgeRules
    {
        /// <summary>Etykieta za bokiem siatki na parterze (grunt).</summary>
        public string SideAtGround = "grunt";

        /// <summary>Etykieta za bokiem siatki na piętrach powyżej parteru (powietrze).</summary>
        public string SideAbove = "powietrze";

        /// <summary>Etykieta pod najniższym poziomem siatki (podloze).</summary>
        public string GridBottom = "podloze";

        /// <summary>Etykiety góry dozwolone na najwyższym poziomie siatki (powietrze, puste).</summary>
        public List<string> GridTopAllowed = new List<string> { "powietrze", "puste" };

        public MapEdgeRules Clone() => new MapEdgeRules
        {
            SideAtGround = SideAtGround,
            SideAbove = SideAbove,
            GridBottom = GridBottom,
            GridTopAllowed = new List<string>(GridTopAllowed)
        };
    }

    /// <summary>Mnożnik wagi dla kafelka o nazwie Target albo dla wszystkich kafelków grupy Target.</summary>
    [Serializable]
    public struct WeightMultiplier
    {
        public string Target;
        public double Factor;

        public WeightMultiplier(string target, double factor)
        {
            Target = target;
            Factor = factor;
        }

        public override string ToString() => $"{Target} × {Factor}";
    }

    /// <summary>
    /// Zestaw kafelków z regułami łączenia, gotowy do skompilowania przez RuleCompiler.
    /// Kolejność kafelków wyznacza numerację stanów.
    /// </summary>
    public sealed class TilesetData
    {
        public string Name { get; set; }

        public List<TileData> Tiles { get; } = new List<TileData>();

        public List<LabelPair> CompatiblePairs { get; } = new List<LabelPair>();

        public List<VerticalException> VerticalExceptions { get; } = new List<VerticalException>();

        public MapEdgeRules MapEdge { get; set; } = new MapEdgeRules();

        public List<WeightMultiplier> WeightMultipliers { get; } = new List<WeightMultiplier>();

        /// <summary>Oczekiwana liczba kafelków z sekcji kontrola; 0 = bez sprawdzania.</summary>
        public int ExpectedTileCount { get; set; }

        /// <summary>Oczekiwana liczba stanów z sekcji kontrola; 0 = bez sprawdzania.</summary>
        public int ExpectedStateCount { get; set; }

        /// <summary>Iloczyn mnożników pasujących do nazwy albo grupy kafelka.</summary>
        public double GetWeightFactor(TileData tile)
        {
            double factor = 1.0;
            foreach (WeightMultiplier multiplier in WeightMultipliers)
                if (multiplier.Target == tile.Name || multiplier.Target == tile.Group)
                    factor *= multiplier.Factor;
            return factor;
        }
    }
}
