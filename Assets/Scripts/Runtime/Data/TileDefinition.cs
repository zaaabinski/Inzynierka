using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Jeden kafelek jako zasób Unity. Tworzy i aktualizuje go RulesImporter z pliku reguł; etykiety są dla obrotu 0.
    /// </summary>
    [CreateAssetMenu(menuName = "WFC/Kafelek", fileName = "Kafelek")]
    public sealed class TileDefinition : ScriptableObject
    {
        [SerializeField] string tileName;
        [Tooltip("Prefab z Prefabs/CastlePieces; pusty dla kafelków niewidocznych.")]
        [SerializeField] GameObject prefab;
        [Tooltip("Nazwa modelu FBX z pliku reguł.")]
        [SerializeField] string model;
        [SerializeField] bool modelAvailable = true;
        [SerializeField] string group;
        [Tooltip("Waga losowania; każdy obrót ma pełną wagę kafelka.")]
        [SerializeField] double weight = 1.0;
        [SerializeField] bool symmetric;
        [Tooltip("1 dla kafelków symetrycznych, 4 dla pozostałych.")]
        [SerializeField, Range(1, 4)] int rotations = 1;
        [Tooltip("Losowy obrót samego modelu, tylko dla wyglądu.")]
        [SerializeField] bool randomModelRotation;

        [Header("Etykiety ścian w obrocie 0")]
        [SerializeField] string labelE;
        [SerializeField] string labelN;
        [SerializeField] string labelW;
        [SerializeField] string labelS;
        [SerializeField] string labelUp;
        [SerializeField] string labelDown;

        [SerializeField, TextArea] string description;

        public string TileName => tileName;
        public GameObject Prefab => prefab;
        public string Model => model;
        public bool ModelAvailable => modelAvailable;
        public string Group => group;
        public double Weight => weight;
        public bool Symmetric => symmetric;
        public int Rotations => rotations;
        public bool RandomModelRotation => randomModelRotation;
        public string Description => description;

        public string GetLabel(Direction direction)
        {
            switch (direction)
            {
                case Direction.E: return labelE;
                case Direction.N: return labelN;
                case Direction.W: return labelW;
                case Direction.S: return labelS;
                case Direction.Up: return labelUp;
                default: return labelDown;
            }
        }

        public TileData ToTileData()
        {
            var data = new TileData
            {
                Name = tileName,
                Model = model,
                ModelAvailable = modelAvailable,
                Group = group,
                Weight = weight,
                Symmetric = symmetric,
                Rotations = rotations,
                RandomModelRotation = randomModelRotation,
                Description = description
            };
            foreach (Direction direction in DirectionExtensions.All)
                data.SetLabel(direction, GetLabel(direction));
            return data;
        }

        /// <summary>Nadpisuje wszystkie pola danymi z pliku reguł (używa RulesImporter).</summary>
        public void CopyFrom(TileData data, GameObject tilePrefab)
        {
            tileName = data.Name;
            prefab = tilePrefab;
            model = data.Model;
            modelAvailable = data.ModelAvailable;
            group = data.Group;
            weight = data.Weight;
            symmetric = data.Symmetric;
            rotations = data.Rotations;
            randomModelRotation = data.RandomModelRotation;
            description = data.Description;
            labelE = data.GetLabel(Direction.E);
            labelN = data.GetLabel(Direction.N);
            labelW = data.GetLabel(Direction.W);
            labelS = data.GetLabel(Direction.S);
            labelUp = data.GetLabel(Direction.Up);
            labelDown = data.GetLabel(Direction.Down);
        }
    }
}
