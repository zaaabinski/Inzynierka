using System;

namespace WFC.Core
{
    /// <summary>
    /// Restrykcyjność reguł policzona z samych reguł, bez generacji. Mniejsza gęstość i większe κ oznaczają ciaśniejsze
    /// reguły. Wszystkie trzy miary są zwykłymi przybliżeniami: zakładają, że dozwolone pary są rozłożone przypadkowo,
    /// a w regułach zamku tak nie jest, więc powodzenia generacji mogą nie przewidywać (eksperymenty E8 i E9 to mierzą).
    /// </summary>
    public readonly struct RuleRestrictiveness
    {
        /// <summary>Ograniczenia na komórkę siatki 3D: każda komórka ma 6 sąsiadów, a każda para liczy się raz.</summary>
        const double ConstraintsPerCell = 3.0;

        public RuleRestrictiveness(double pairDensityPercent, double weightedDensityPercent, double kappa)
        {
            PairDensityPercent = pairDensityPercent;
            WeightedDensityPercent = weightedDensityPercent;
            Kappa = kappa;
        }

        /// <summary>Odsetek dozwolonych trójek (stan, kierunek, sąsiad) wśród wszystkich możliwych.</summary>
        public double PairDensityPercent { get; }

        /// <summary>Szansa w procentach, że dwa stany wylosowane według wag mogą stać obok siebie, średnia z 6 kierunków.</summary>
        public double WeightedDensityPercent { get; }

        /// <summary>
        /// Współczynnik κ = −(c / n) · log_m(d) z pracy Gent, MacIntyre, Prosser, Walsh, „The Constrainedness of Search”
        /// (1996): c / n = 3 ograniczenia na komórkę, m stanów, d gęstość par. Dla reguł losowych κ = 1 to próg, powyżej
        /// którego oczekiwana liczba rozwiązań spada poniżej jednego.
        /// </summary>
        public double Kappa { get; }

        public static RuleRestrictiveness Of(CompiledRules rules)
        {
            int states = rules.StateCount;
            double totalWeight = 0;
            for (int state = 0; state < states; state++)
                totalWeight += rules.GetWeight(state);

            double weighted = 0;
            foreach (Direction direction in DirectionExtensions.All)
                for (int state = 0; state < states; state++)
                {
                    double allowedWeight = 0;
                    foreach (int neighbor in rules.GetNeighbors(state, direction))
                        allowedWeight += rules.GetWeight(neighbor);
                    weighted += rules.GetWeight(state) / totalWeight * (allowedWeight / totalWeight);
                }
            weighted /= DirectionExtensions.Count;

            double density = rules.NeighborPairCount / ((double)states * states * DirectionExtensions.Count);
            double kappa = density > 0 && states > 1 ? -ConstraintsPerCell * Math.Log(density) / Math.Log(states) : double.NaN;
            return new RuleRestrictiveness(density * 100.0, weighted * 100.0, kappa);
        }
    }
}
