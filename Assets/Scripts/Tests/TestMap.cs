using NUnit.Framework;
using Unity.Mathematics;
using WFC.Core;

namespace WFC.Tests
{
    /// <summary>
    /// Ręcznie układana mapa do testów: parter z Teren_Trawa, wyżej Powietrze; wybrane komórki ustawiamy po nazwie
    /// kafelka i obrocie (k × 90°).
    /// </summary>
    sealed class TestMap
    {
        public TestMap(CompiledRules rules, int3 size)
        {
            Rules = rules;
            Grid = new GridShape(size);
            States = new int[Grid.CellCount];
            int grass = State("Teren_Trawa", 0);
            int air = State("Powietrze", 0);
            for (int cell = 0; cell < Grid.CellCount; cell++)
                States[cell] = Grid.Floor(cell) == 0 ? grass : air;
        }

        public CompiledRules Rules { get; }
        public GridShape Grid { get; }
        public int[] States { get; }

        public TestMap Set(int x, int floor, int z, string tile, int rotation = 0)
        {
            States[Grid.Index(x, floor, z)] = State(tile, rotation);
            return this;
        }

        public RuleCheckReport Check() => RuleChecker.Check(Rules, Grid.Size, States);

        public StructureStats Analyze() => StructureAnalyzer.Analyze(Rules, Grid.Size, States);

        int State(string tile, int rotation)
        {
            int state = Rules.FindState(tile, rotation);
            Assert.GreaterOrEqual(state, 0, $"Zestaw {Rules.TilesetName} nie ma stanu {tile} w obrocie {rotation}.");
            return state;
        }

        /// <summary>
        /// Mapa 4 × 3 × 4 z jedną wieżą 2 × 2 o wysokości 2 kostek (podstawa i blanki) w komórkach x 1–2, z 1–2.
        /// Ćwiartka w obrocie 0 ma boki wewnętrzne E i N, więc stoi w rogu SW; kolejne obroty idą przeciwnie
        /// do ruchu wskazówek zegara: SE (90°), NE (180°), NW (270°).
        /// </summary>
        public static TestMap WithTower(CompiledRules rules)
        {
            var map = new TestMap(rules, new int3(4, 3, 4));
            int[,] quarters = { { 1, 1, 0 }, { 2, 1, 1 }, { 2, 2, 2 }, { 1, 2, 3 } };
            for (int i = 0; i < 4; i++)
            {
                map.Set(quarters[i, 0], 0, quarters[i, 1], "Wieza_Podstawa", quarters[i, 2]);
                map.Set(quarters[i, 0], 1, quarters[i, 1], "Wieza_Szczyt", quarters[i, 2]);
            }
            return map;
        }
    }
}
