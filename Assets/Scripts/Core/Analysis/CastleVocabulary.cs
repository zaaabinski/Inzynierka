namespace WFC.Core
{
    /// <summary>
    /// Nazwy grup, kafelków i etykiet z pliku reguł, których używają RuleChecker i StructureAnalyzer.
    /// Sprawdzenia opierają się na znaczeniu kafelków, a nie na liście par zgodnych, więc działają też dla wariantów
    /// reguł z eksperymentu E6, w których część par jest wyłączona.
    /// </summary>
    public static class CastleVocabulary
    {
        public const string GroupTerrain = "Teren";
        public const string GroupStairs = "Schody";
        public const string GroupAir = "Powietrze";
        public const string GroupWall = "Mur obronny";
        public const string GroupWallCorner = "Narożnik muru";
        public const string GroupTower = "Wieża";
        public const string GroupKeep = "Donżon";
        public const string GroupKeepRoof = "Dach donżonu";
        public const string GroupBridge = "Most";

        public const string TileAirAboveStairs = "Powietrze_Nad_Schodami";

        public const string LabelStairsTop = "schody_szczyt";
        public const string LabelDoor = "drzwi";
        public const string LabelBeforeDoor = "przed_drzwiami";
        public const string LabelEmpty = "puste";
        public const string LabelBridgeAbutment = "most_przyczolek";
        public const string LabelBridgeStart = "most_poczatek";
        public const string LabelBridgeMiddleFromStart = "most_srodek_od_poczatku";
        public const string LabelBridgeMiddleToEnd = "most_srodek_do_konca";
        public const string LabelBridgeEnd = "most_koniec";

        /// <summary>Czy dwie etykiety mostu tworzą ciągłe połączenie (w dowolnej kolejności).</summary>
        public static bool BridgeConnects(string a, string b) =>
            IsPair(a, b, LabelBridgeStart, LabelBridgeMiddleFromStart) ||
            IsPair(a, b, LabelBridgeMiddleToEnd, LabelBridgeMiddleFromStart) ||
            IsPair(a, b, LabelBridgeMiddleToEnd, LabelBridgeEnd) ||
            IsPair(a, b, LabelBridgeAbutment, LabelDoor);

        public static bool IsBridgeLabel(string label) =>
            label == LabelBridgeStart || label == LabelBridgeMiddleFromStart || label == LabelBridgeMiddleToEnd ||
            label == LabelBridgeEnd || label == LabelBridgeAbutment;

        static bool IsPair(string a, string b, string x, string y) => (a == x && b == y) || (a == y && b == x);
    }
}
