namespace WFC.Core
{
    /// <summary>
    /// Jeden kafelek w postaci niezależnej od Unity: z pliku JSON albo z zasobu TileDefinition.
    /// Etykiety są zapisane dla obrotu 0; obroty rozwija RuleCompiler.
    /// </summary>
    public sealed class TileData
    {
        public string Name { get; set; }

        /// <summary>Nazwa modelu (pliku FBX i prefabu); pusta dla kafelków niewidocznych.</summary>
        public string Model { get; set; }

        /// <summary>Fałsz, gdy kafelek jest w regułach, ale nie ma jeszcze modelu (Dach_Szczyt_Choragiew).</summary>
        public bool ModelAvailable { get; set; } = true;

        public string Group { get; set; }

        /// <summary>Waga losowania; każdy obrót kafelka ma pełną wagę kafelka.</summary>
        public double Weight { get; set; } = 1.0;

        public bool Symmetric { get; set; }

        /// <summary>Liczba obrotów: 1 dla kafelków symetrycznych, 4 dla pozostałych.</summary>
        public int Rotations { get; set; } = 1;

        /// <summary>Losowy obrót samego modelu, tylko dla wyglądu (kafelki symetryczne).</summary>
        public bool RandomModelRotation { get; set; }

        public string Description { get; set; }

        /// <summary>Etykiety 6 ścian w obrocie 0, indeks = (int)Direction.</summary>
        public string[] Labels { get; } = new string[DirectionExtensions.Count];

        public string GetLabel(Direction direction) => Labels[(int)direction];

        public void SetLabel(Direction direction, string label) => Labels[(int)direction] = label;

        public bool HasModel => !string.IsNullOrEmpty(Model);

        public TileData Clone()
        {
            var copy = new TileData
            {
                Name = Name,
                Model = Model,
                ModelAvailable = ModelAvailable,
                Group = Group,
                Weight = Weight,
                Symmetric = Symmetric,
                Rotations = Rotations,
                RandomModelRotation = RandomModelRotation,
                Description = Description
            };
            Labels.CopyTo(copy.Labels, 0);
            return copy;
        }

        public override string ToString() => Name;
    }
}
