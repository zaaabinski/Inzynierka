using System.Collections.Generic;
using UnityEngine;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Scena TilesetPreview: każdy kafelek zestawu w 4 obrotach (0°, 90°, 180°, 270°) z etykietami ścian jako napisami,
    /// żeby sprawdzić orientację modeli. Wiersz = kafelek, kolumny = obroty; co rowsPerBlock wierszy zaczyna się
    /// nowy blok obok. Napisy E i W są czerwone (oś X), N i S niebieskie (oś Z), jaśniejsze po stronie dodatniej.
    /// </summary>
    public sealed class TilesetPreviewer : MonoBehaviour
    {
        [SerializeField] TilesetDefinition tileset;
        [SerializeField] bool buildOnStart = true;
        [SerializeField] bool showLabels = true;
        [Tooltip("Odstęp między środkami komórek podglądu [m].")]
        [SerializeField, Min(3f)] float spacing = 6f;
        [Tooltip("Liczba kafelków w jednym bloku; kolejne bloki stają obok w kierunku +X.")]
        [SerializeField, Min(1)] int rowsPerBlock = 10;
        [SerializeField, Min(0.01f)] float labelSize = 0.06f;

        static readonly Color ColorE = new Color(1f, 0.35f, 0.3f);
        static readonly Color ColorW = new Color(0.6f, 0.1f, 0.1f);
        static readonly Color ColorN = new Color(0.35f, 0.6f, 1f);
        static readonly Color ColorS = new Color(0.1f, 0.2f, 0.6f);

        Font font;

        public TilesetDefinition Tileset
        {
            get => tileset;
            set => tileset = value;
        }

        /// <summary>Środek obszaru zajętego przez podgląd, do ustawienia kamery.</summary>
        public Vector3 Center
        {
            get
            {
                int count = tileset != null ? tileset.Tiles.Count : 0;
                int blocks = Mathf.Max(1, Mathf.CeilToInt(count / (float)rowsPerBlock));
                int rows = Mathf.Min(count, rowsPerBlock);
                float blockWidth = (DirectionExtensions.SideCount + 1) * spacing;
                return transform.TransformPoint(new Vector3((blocks * blockWidth - 2f * spacing) / 2f, 0f, -(rows - 1) * spacing / 2f));
            }
        }

        void Start()
        {
            if (buildOnStart)
                Build();
        }

        [ContextMenu("Zbuduj podgląd")]
        public void Build()
        {
            Clear();
            if (tileset == null)
            {
                Debug.LogWarning("[TilesetPreviewer] Nie wybrano zestawu kafelków.", this);
                return;
            }

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            float blockWidth = (DirectionExtensions.SideCount + 1) * spacing;
            for (int index = 0; index < tileset.Tiles.Count; index++)
            {
                TileDefinition tile = tileset.Tiles[index];
                TileData data = tile.ToTileData();
                var origin = new Vector3((index / rowsPerBlock) * blockWidth, 0f, -(index % rowsPerBlock) * spacing);
                for (int rotation = 0; rotation < DirectionExtensions.SideCount; rotation++)
                    BuildCell(tile, data, rotation, origin + new Vector3(rotation * spacing, 0f, 0f));
            }
        }

        [ContextMenu("Wyczyść podgląd")]
        public void Clear()
        {
            var children = new List<GameObject>();
            foreach (Transform child in transform)
                children.Add(child.gameObject);
            foreach (GameObject child in children)
            {
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        void BuildCell(TileDefinition tile, TileData data, int rotation, Vector3 position)
        {
            // Kafelek symetryczny ma w regułach 1 obrót; obracamy wtedy tylko model, etykiety zostają z obrotu 0.
            int labelRotation = rotation % data.Rotations;
            var cell = new GameObject($"{tile.TileName}@{rotation * 90}");
            cell.transform.SetParent(transform, false);
            cell.transform.localPosition = position;

            if (tile.Prefab != null)
            {
                GameObject instance = Instantiate(tile.Prefab, cell.transform);
                instance.name = tile.Prefab.name;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = UnityGrid.TileRotation(rotation);
            }

            if (!showLabels)
                return;

            // Napisy leżą poziomo nad krawędziami kostki i czytają się z południa (kamera patrzy w stronę N):
            // N i S wzdłuż osi X, E i W wzdłuż osi Z, od dołu do góry.
            const float half = UnityGrid.CellSize / 2f;
            foreach (Direction side in DirectionExtensions.Sides)
            {
                Vector3 outward = UnityGrid.ToVector(side);
                Vector3 textUp = side == Direction.N || side == Direction.S ? Vector3.forward : Vector3.left;
                AddLabel(cell.transform, RuleCompiler.RotatedLabel(data, side, labelRotation), SideColor(side),
                    outward * (half + 0.15f) + Vector3.up * (UnityGrid.CellSize + 0.4f),
                    Quaternion.LookRotation(Vector3.down, textUp));
            }
            string caption = $"{tile.TileName} {rotation * 90}°\n" +
                             $"góra: {RuleCompiler.RotatedLabel(data, Direction.Up, labelRotation)}\n" +
                             $"dół: {RuleCompiler.RotatedLabel(data, Direction.Down, labelRotation)}";
            AddLabel(cell.transform, caption, Color.white,
                Vector3.back * (half + 1.0f) + Vector3.up * 0.05f,
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
        }

        void AddLabel(Transform parent, string text, Color color, Vector3 localPosition, Quaternion localRotation)
        {
            var label = new GameObject("Etykieta");
            label.transform.SetParent(parent, false);
            label.transform.localPosition = localPosition;
            label.transform.localRotation = localRotation;
            var mesh = label.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 48;
            mesh.characterSize = labelSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        static Color SideColor(Direction side)
        {
            switch (side)
            {
                case Direction.E: return ColorE;
                case Direction.W: return ColorW;
                case Direction.N: return ColorN;
                default: return ColorS;
            }
        }

        void OnDrawGizmos()
        {
            // Osie siatki programu: E = +X (czerwona), N = +Z (niebieska).
            Vector3 origin = transform.position + new Vector3(-spacing, 0f, spacing);
            Gizmos.color = ColorE;
            Gizmos.DrawLine(origin, origin + Vector3.right * UnityGrid.CellSize);
            Gizmos.color = ColorN;
            Gizmos.DrawLine(origin, origin + Vector3.forward * UnityGrid.CellSize);
        }
    }
}
