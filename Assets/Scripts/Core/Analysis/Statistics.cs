using System;
using System.Collections.Generic;
using System.Linq;

namespace WFC.Core
{
    /// <summary>Mediana, kwantyle i korelacja do podsumowań serii, bez zewnętrznych bibliotek.</summary>
    public static class Statistics
    {
        public static double Median(IEnumerable<double> values) => Quantile(values, 0.5);

        /// <summary>Kwantyl z interpolacją liniową między sąsiednimi wartościami; NaN dla pustej listy.</summary>
        public static double Quantile(IEnumerable<double> values, double quantile)
        {
            double[] sorted = values.OrderBy(value => value).ToArray();
            if (sorted.Length == 0)
                return double.NaN;
            double position = (sorted.Length - 1) * quantile;
            int below = (int)Math.Floor(position);
            int above = Math.Min(below + 1, sorted.Length - 1);
            return sorted[below] + (sorted[above] - sorted[below]) * (position - below);
        }

        /// <summary>Współczynnik korelacji rang Spearmana; remisy dostają średnią rangę. NaN, gdy jedna z list jest stała albo są mniej niż 3 pary.</summary>
        public static double Spearman(IReadOnlyList<double> first, IReadOnlyList<double> second)
        {
            if (first.Count != second.Count)
                throw new ArgumentException($"Listy mają {first.Count} i {second.Count} wartości.");
            return first.Count < 3 ? double.NaN : Pearson(Ranks(first), Ranks(second));
        }

        public static double[] Ranks(IReadOnlyList<double> values)
        {
            int[] order = Enumerable.Range(0, values.Count).OrderBy(index => values[index]).ToArray();
            var ranks = new double[values.Count];
            for (int from = 0; from < order.Length;)
            {
                int to = from;
                while (to + 1 < order.Length && values[order[to + 1]] == values[order[from]])
                    to++;
                for (int index = from; index <= to; index++)
                    ranks[order[index]] = (from + to) / 2.0 + 1;
                from = to + 1;
            }
            return ranks;
        }

        static double Pearson(double[] first, double[] second)
        {
            double meanFirst = first.Average();
            double meanSecond = second.Average();
            double product = 0, squareFirst = 0, squareSecond = 0;
            for (int index = 0; index < first.Length; index++)
            {
                product += (first[index] - meanFirst) * (second[index] - meanSecond);
                squareFirst += (first[index] - meanFirst) * (first[index] - meanFirst);
                squareSecond += (second[index] - meanSecond) * (second[index] - meanSecond);
            }
            return squareFirst == 0 || squareSecond == 0 ? double.NaN : product / Math.Sqrt(squareFirst * squareSecond);
        }
    }
}
