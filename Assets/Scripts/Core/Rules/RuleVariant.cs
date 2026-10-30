using System;
using System.Collections.Generic;

namespace WFC.Core
{
    /// <summary>
    /// Wariant reguł do eksperymentu E6: zestaw bazowy z dopisanymi parami zgodnych etykiet (wyłączona zasada),
    /// mnożnikami wag i listą sprawdzeń RuleChecker, które w tym wariancie nie są naruszeniem.
    /// </summary>
    public sealed class RuleVariant
    {
        public RuleVariant(string name, string baseSet, string description, LabelPair[] extraPairs = null,
            WeightMultiplier[] weightMultipliers = null, ViolationType[] disabledChecks = null)
        {
            Name = name;
            BaseSet = baseSet;
            Description = description;
            ExtraPairs = extraPairs ?? Array.Empty<LabelPair>();
            WeightMultipliers = weightMultipliers ?? Array.Empty<WeightMultiplier>();
            DisabledChecks = disabledChecks ?? Array.Empty<ViolationType>();
        }

        public string Name { get; }

        public string BaseSet { get; }

        public string Description { get; }

        public IReadOnlyList<LabelPair> ExtraPairs { get; }

        public IReadOnlyList<WeightMultiplier> WeightMultipliers { get; }

        public IReadOnlyList<ViolationType> DisabledChecks { get; }

        /// <summary>Zmienia zestaw bazowy w ten wariant: nowa nazwa, dopisane pary, mnożniki i wyłączone sprawdzenia.</summary>
        public TilesetData Apply(TilesetData baseSet)
        {
            if (baseSet.Name != BaseSet)
                throw new ArgumentException($"Wariant {Name} powstaje z zestawu {BaseSet}, a nie {baseSet.Name}.", nameof(baseSet));
            baseSet.Name = Name;
            baseSet.CompatiblePairs.AddRange(ExtraPairs);
            baseSet.WeightMultipliers.AddRange(WeightMultipliers);
            baseSet.DisabledChecks.AddRange(DisabledChecks);
            return baseSet;
        }
    }

    /// <summary>
    /// Warianty reguł z rozdziału 7 handoffu (E6), wszystkie z zestawu Pelny. Zasady według numerów z zakładki Zasady
    /// arkusza reguł: 12 i 23 (wolne pole przy ścianach), 35 (drzwi nad schodami); wagi trawy jak w tabeli
    /// „Gęstość zabudowy a wagi trawy” z zakładki Sprawdzenie.
    /// </summary>
    public static class RuleVariants
    {
        const string BaseSet = "Pelny";

        /// <summary>Boki budynków, które według zasady 12 stykają się tylko z wolnym polem (gruntem albo powietrzem).</summary>
        static readonly string[] BuildingSides = { "mur_bok_parter", "mur_bok_wyzej", "wieza_sciana", "donzon_sciana" };

        public static readonly RuleVariant WithoutFreeField = new RuleVariant(
            "PelnyBezWolnegoPola", BaseSet,
            "Bez zasady wolnego pola przy ścianach (zasady 12 i 23): boki murów i zewnętrzne ściany wież i donżonów mogą się " +
            "stykać ze sobą bezpośrednio, także dwie wieże obok siebie. Okap dachu dalej wymaga powietrza.",
            extraPairs: AllPairs(BuildingSides));

        public static readonly RuleVariant WithoutDoorAboveStairs = new RuleVariant(
            "PelnyBezDrzwiNadSchodami", BaseSet,
            "Bez zasady drzwi nad schodami (zasada 35): powietrze nad schodami może stać także przed ślepą ścianą wieży " +
            "albo donżonu i przed okapem dachu, więc schody nie muszą prowadzić do drzwi. RuleChecker liczy takie schody osobno.",
            extraPairs: new[]
            {
                new LabelPair("przed_drzwiami", "wieza_sciana"),
                new LabelPair("przed_drzwiami", "donzon_sciana"),
                new LabelPair("przed_drzwiami", "okap")
            },
            disabledChecks: new[] { ViolationType.StairsWithoutDoorAbove });

        public static readonly RuleVariant GrassWeight040 = new RuleVariant(
            "PelnyTrawa040", BaseSet,
            "Wagi wszystkich 5 kafelków trawy × 0,4, proporcje między nimi bez zmian. Prototyp: 44% sprzeczności, 15,0 wieży na mapę.",
            weightMultipliers: new[] { new WeightMultiplier(CastleVocabulary.GroupTerrain, 0.4) });

        public static readonly RuleVariant GrassWeight025 = new RuleVariant(
            "PelnyTrawa025", BaseSet,
            "Wagi wszystkich 5 kafelków trawy × 0,25, proporcje między nimi bez zmian. Prototyp: 60% sprzeczności, 16,8 wieży na mapę.",
            weightMultipliers: new[] { new WeightMultiplier(CastleVocabulary.GroupTerrain, 0.25) });

        public static readonly IReadOnlyList<RuleVariant> All = new[] { WithoutFreeField, WithoutDoorAboveStairs, GrassWeight040, GrassWeight025 };

        public static RuleVariant Find(string name)
        {
            foreach (RuleVariant variant in All)
                if (variant.Name == name)
                    return variant;
            return null;
        }

        /// <summary>Każda para z listy, także etykieta sama ze sobą.</summary>
        static LabelPair[] AllPairs(string[] labels)
        {
            var pairs = new List<LabelPair>();
            for (int a = 0; a < labels.Length; a++)
                for (int b = a; b < labels.Length; b++)
                    pairs.Add(new LabelPair(labels[a], labels[b]));
            return pairs.ToArray();
        }
    }
}
