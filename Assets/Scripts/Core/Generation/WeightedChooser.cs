using System;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>
    /// Entropia komórki i losowanie stanu z wagami. Stany zawsze przechodzimy rosnąco, a sumy liczymy w double,
    /// więc każdy rozwiązywacz z tymi samymi możliwościami dostaje identyczną liczbę i wylosuje ten sam stan.
    /// </summary>
    public static class WeightedChooser
    {
        /// <summary>
        /// Entropia Shannona z wagami: H = ln(Σw) − Σ(w · ln w) / Σw. Dla jednej możliwości 0.
        /// </summary>
        public static double Entropy(CompiledRules rules, ReadOnlySpan<int> states)
        {
            if (states.Length <= 1)
                return 0;
            double sum = 0;
            double sumLog = 0;
            foreach (int state in states)
            {
                sum += rules.GetWeight(state);
                sumLog += rules.GetWeightLogWeight(state);
            }
            return Math.Log(sum) - sumLog / sum;
        }

        /// <summary>Losuje jeden ze stanów (podanych rosnąco) z prawdopodobieństwem proporcjonalnym do wagi.</summary>
        public static int Choose(CompiledRules rules, ReadOnlySpan<int> states, ref DeterministicRandom random)
        {
            if (states.Length == 0)
                throw new ArgumentException("Brak stanów do wylosowania.", nameof(states));
            double total = 0;
            foreach (int state in states)
                total += rules.GetWeight(state);
            double target = random.NextDouble() * total;
            foreach (int state in states)
            {
                target -= rules.GetWeight(state);
                if (target < 0)
                    return state;
            }
            return states[states.Length - 1];
        }

        /// <summary>Entropia jak wyżej dla stanów zapisanych jako maska bitowa (bity rosnąco, te same sumy).</summary>
        public static double Entropy(CompiledRules rules, ReadOnlySpan<ulong> mask)
        {
            if (BitMask.Count(mask) <= 1)
                return 0;
            double sum = 0;
            double sumLog = 0;
            for (int word = 0; word < mask.Length; word++)
                for (ulong bits = mask[word]; bits != 0; bits &= bits - 1)
                {
                    int state = word * BitMask.BitsPerWord + math.tzcnt(bits);
                    sum += rules.GetWeight(state);
                    sumLog += rules.GetWeightLogWeight(state);
                }
            return Math.Log(sum) - sumLog / sum;
        }

        /// <summary>Losowanie jak wyżej dla stanów zapisanych jako maska bitowa: ta sama liczba losowa daje ten sam stan.</summary>
        public static int Choose(CompiledRules rules, ReadOnlySpan<ulong> mask, ref DeterministicRandom random)
        {
            double total = 0;
            int last = -1;
            for (int word = 0; word < mask.Length; word++)
                for (ulong bits = mask[word]; bits != 0; bits &= bits - 1)
                {
                    last = word * BitMask.BitsPerWord + math.tzcnt(bits);
                    total += rules.GetWeight(last);
                }
            if (last < 0)
                throw new ArgumentException("Brak stanów do wylosowania.", nameof(mask));
            double target = random.NextDouble() * total;
            for (int word = 0; word < mask.Length; word++)
                for (ulong bits = mask[word]; bits != 0; bits &= bits - 1)
                {
                    int state = word * BitMask.BitsPerWord + math.tzcnt(bits);
                    target -= rules.GetWeight(state);
                    if (target < 0)
                        return state;
                }
            return last;
        }
    }
}
