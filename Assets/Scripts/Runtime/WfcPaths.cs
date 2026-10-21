namespace WFC.Runtime
{
    /// <summary>Ścieżki zasobów generatora zamków, względem folderu projektu Unity.</summary>
    public static class WfcPaths
    {
        public const string Models = "Assets/Resources/Models/CastlePieces";
        public const string Materials = "Assets/Materials/CastlePieces";
        public const string TilePrefabs = "Assets/Prefabs/CastlePieces";
        public const string Rules = "Assets/Data/Rules";
        public const string RulesJson = Rules + "/wfc_rules_zamek.json";

        /// <summary>Arkusz z tymi samymi regułami dla człowieka; testy porównują z nim reguły po kompilacji.</summary>
        public const string RulesSheet = Rules + "/Zasady_laczenia_WFC_zamek.xlsx";

        public const string Tilesets = "Assets/Data/Tilesets";
        public const string Tiles = "Assets/Data/Tiles";
        public const string Scenes = "Assets/Scenes";
        public const string TilesetPreviewScene = Scenes + "/TilesetPreview.unity";
        public const string GeneratorScene = Scenes + "/Generator.unity";

        /// <summary>Eksperymenty E1–E6 (BenchmarkConfig).</summary>
        public const string Experiments = "Assets/Data/Experiments";

        /// <summary>Ustawienia panelu UI Toolkit i jego motyw.</summary>
        public const string UiSettings = "Assets/Settings/UI";

        /// <summary>Zbudowana aplikacja, względem folderu projektu (folder Build jest w .gitignore).</summary>
        public const string BuildExecutable = "Build/Zamek.exe";

        public static string Experiment(string experimentName) => $"{Experiments}/{experimentName}.asset";

        public static string TilePrefab(string model) => $"{TilePrefabs}/{model}.prefab";

        public static string Tile(string tileName) => $"{Tiles}/{tileName}.asset";

        public static string Tileset(string setName) => $"{Tilesets}/{setName}.asset";
    }
}
