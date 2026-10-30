using System;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;

namespace WFC.Core
{
    /// <summary>Typy naruszeń, w kolejności z zakładki Sprawdzenie arkusza reguł.</summary>
    public enum ViolationType
    {
        NeighborPair,
        MapEdge,
        GridBottom,
        GridTop,
        StairsWithoutAirAbove,
        StairsNotAtTowerOrKeep,
        StairsWithoutDoorAbove,
        DoorWithoutBridgeOrStairs,
        DoorAtMapEdge,
        IncompleteBridge,
        BridgeNotAboveAir,
        UnevenTower,
        EmptyCell
    }

    /// <summary>
    /// Naruszenia jednej mapy według typu. Typy wyłączone w zestawie (wariant reguł bez danej zasady) liczą się osobno
    /// w DisabledTotal: to skutek wyłączenia zasady, a nie błąd generatora.
    /// </summary>
    public sealed class RuleCheckReport
    {
        public static readonly int TypeCount = Enum.GetValues(typeof(ViolationType)).Length;
        const int MaxExamples = 20;

        readonly int[] counts = new int[TypeCount];
        readonly bool[] disabled = new bool[TypeCount];

        public RuleCheckReport(IEnumerable<ViolationType> disabledChecks = null)
        {
            if (disabledChecks != null)
                foreach (ViolationType type in disabledChecks)
                    disabled[(int)type] = true;
        }

        /// <summary>Liczba przypadków danego typu, także wyłączonego.</summary>
        public int this[ViolationType type] => counts[(int)type];

        /// <summary>Naruszenia zasad obowiązujących w zestawie; cel: 0.</summary>
        public int Total { get; private set; }

        /// <summary>Przypadki zasad wyłączonych w zestawie, np. schody bez drzwi w wariancie PelnyBezDrzwiNadSchodami.</summary>
        public int DisabledTotal { get; private set; }

        public bool IsDisabled(ViolationType type) => disabled[(int)type];

        /// <summary>Do 20 przykładów z położeniem, do szukania błędu.</summary>
        public List<string> Examples { get; } = new List<string>();

        public void Add(ViolationType type, int3 cell, string detail)
        {
            counts[(int)type]++;
            if (disabled[(int)type])
            {
                DisabledTotal++;
                return;
            }
            Total++;
            if (Examples.Count < MaxExamples)
                Examples.Add($"{Describe(type)} w (x {cell.x}, piętro {cell.y}, z {cell.z}): {detail}");
        }

        public static string Describe(ViolationType type)
        {
            switch (type)
            {
                case ViolationType.NeighborPair: return "Para sąsiadów niezgodna z regułami";
                case ViolationType.MapEdge: return "Coś urwane na krawędzi mapy";
                case ViolationType.GridBottom: return "Zły kafelek na dnie siatki";
                case ViolationType.GridTop: return "Zły kafelek na górze siatki";
                case ViolationType.StairsWithoutAirAbove: return "Schody bez powietrza nad sobą";
                case ViolationType.StairsNotAtTowerOrKeep: return "Schody nie do wieży ani donżonu";
                case ViolationType.StairsWithoutDoorAbove: return "Schody bez drzwi piętro wyżej";
                case ViolationType.DoorWithoutBridgeOrStairs: return "Drzwi bez mostu i bez schodów";
                case ViolationType.DoorAtMapEdge: return "Drzwi na krawędzi mapy";
                case ViolationType.IncompleteBridge: return "Most niepełny albo bez drzwi na końcu";
                case ViolationType.BridgeNotAboveAir: return "Most nad czymś innym niż Powietrze";
                case ViolationType.UnevenTower: return "Wieża z ćwiartkami różnej wysokości";
                case ViolationType.EmptyCell: return "Komórka bez kafelka";
                default: return type.ToString();
            }
        }

        public override string ToString()
        {
            var text = new StringBuilder($"Naruszenia zasad: {Total}");
            for (int type = 0; type < TypeCount; type++)
                if (counts[type] > 0 && !disabled[type])
                    text.Append($"\n  {Describe((ViolationType)type)}: {counts[type]}");
            for (int type = 0; type < TypeCount; type++)
                if (counts[type] > 0 && disabled[type])
                    text.Append($"\n  {Describe((ViolationType)type)}: {counts[type]} (zasada wyłączona w tym zestawie, nie liczy się)");
            foreach (string example in Examples)
                text.Append("\n  przykład: ").Append(example);
            return text.ToString();
        }
    }

    /// <summary>
    /// Sprawdza gotową mapę wszystkimi zasadami: pary sąsiadów i krawędzie według skompilowanych reguł oraz znaczenie
    /// kafelków (schody z drzwiami nad sobą, drzwi z mostem albo schodami, pełne mosty nad pustym, wieże 2 × 2 równej
    /// wysokości). Cel: 0 naruszeń na każdej mapie.
    /// </summary>
    public static class RuleChecker
    {
        public static RuleCheckReport Check(CompiledRules rules, int3 size, int[] states)
        {
            var grid = new GridShape(size);
            if (states.Length != grid.CellCount)
                throw new ArgumentException($"Mapa ma {states.Length} komórek, a siatka {grid} ma {grid.CellCount}.", nameof(states));
            var report = new RuleCheckReport(rules.DisabledChecks);
            var map = new MapView(rules, grid, states);

            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int3 position = grid.Coordinates(cell);
                int state = states[cell];
                if (state < 0)
                {
                    report.Add(ViolationType.EmptyCell, position, "brak stanu");
                    continue;
                }

                if (position.y == 0 && !BitMask.Get(rules.BottomMask, state))
                    report.Add(ViolationType.GridBottom, position, rules.GetStateName(state));
                if (position.y == grid.Floors - 1 && !BitMask.Get(rules.TopMask, state))
                    report.Add(ViolationType.GridTop, position, rules.GetStateName(state));

                foreach (Direction direction in DirectionExtensions.All)
                {
                    int neighbor = grid.Neighbor(cell, direction);
                    if (neighbor < 0)
                    {
                        if (direction.IsSide() && !BitMask.Get(rules.GetSideEdgeMask(direction, position.y == 0), state))
                            report.Add(ViolationType.MapEdge, position, $"{rules.GetStateName(state)} stroną {direction}");
                        continue;
                    }
                    // Każdą parę sprawdzamy raz: w kierunkach E, N i w górę.
                    bool counted = direction == Direction.E || direction == Direction.N || direction == Direction.Up;
                    if (counted && states[neighbor] >= 0 && !rules.CanBeNeighbors(state, direction, states[neighbor]))
                        report.Add(ViolationType.NeighborPair, position, $"{rules.GetStateName(state)} i {rules.GetStateName(states[neighbor])} w kierunku {direction}");
                }

                CheckStairs(map, cell, position, report);
                CheckDoors(map, cell, position, report);
                CheckBridge(map, cell, position, report);
            }

            foreach (List<int> tower in map.TowerFootprints())
            {
                var heights = new HashSet<int>();
                foreach (int cell in tower)
                    heights.Add(map.ColumnHeight(cell, CastleVocabulary.GroupTower));
                if (tower.Count != 4 || heights.Count != 1)
                    report.Add(ViolationType.UnevenTower, grid.Coordinates(tower[0]),
                        $"{tower.Count} ćwiartek, wysokości: {string.Join(", ", heights)}");
            }

            return report;
        }

        static void CheckStairs(MapView map, int cell, int3 position, RuleCheckReport report)
        {
            if (map.Group(cell) != CastleVocabulary.GroupStairs)
                return;
            Direction? top = map.SideWithLabel(cell, CastleVocabulary.LabelStairsTop);
            if (top == null)
                return;
            Direction high = top.Value;

            int above = map.Grid.Neighbor(cell, Direction.Up);
            if (above < 0 || map.Name(above) != CastleVocabulary.TileAirAboveStairs || map.Rotation(above) != map.Rotation(cell))
                report.Add(ViolationType.StairsWithoutAirAbove, position, above < 0 ? "brak piętra nad schodami" : map.StateName(above));

            int front = map.Grid.Neighbor(cell, high);
            if (front < 0 || (map.Group(front) != CastleVocabulary.GroupTower && map.Group(front) != CastleVocabulary.GroupKeep))
                report.Add(ViolationType.StairsNotAtTowerOrKeep, position, front < 0 ? "wyższy koniec przy krawędzi" : map.StateName(front));

            int door = front < 0 ? -1 : map.Grid.Neighbor(front, Direction.Up);
            if (door < 0 || map.Label(door, high.Opposite()) != CastleVocabulary.LabelDoor)
                report.Add(ViolationType.StairsWithoutDoorAbove, position, door < 0 ? "brak komórki nad wyższym końcem" : map.StateName(door));
        }

        static void CheckDoors(MapView map, int cell, int3 position, RuleCheckReport report)
        {
            foreach (Direction side in DirectionExtensions.Sides)
            {
                if (map.Label(cell, side) != CastleVocabulary.LabelDoor)
                    continue;
                int front = map.Grid.Neighbor(cell, side);
                if (front < 0)
                {
                    report.Add(ViolationType.DoorAtMapEdge, position, $"{map.StateName(cell)} drzwiami na {side}");
                    continue;
                }
                string facing = map.Label(front, side.Opposite());
                if (facing != CastleVocabulary.LabelBridgeAbutment && facing != CastleVocabulary.LabelBeforeDoor)
                    report.Add(ViolationType.DoorWithoutBridgeOrStairs, position, $"{map.StateName(cell)}, przed drzwiami {map.StateName(front)}");
            }
        }

        static void CheckBridge(MapView map, int cell, int3 position, RuleCheckReport report)
        {
            if (map.Group(cell) != CastleVocabulary.GroupBridge)
                return;

            int below = map.Grid.Neighbor(cell, Direction.Down);
            if (below < 0 || map.Label(below, Direction.Up) != CastleVocabulary.LabelEmpty)
                report.Add(ViolationType.BridgeNotAboveAir, position, below < 0 ? "most na dnie siatki" : $"pod mostem {map.StateName(below)}");

            foreach (Direction side in DirectionExtensions.Sides)
            {
                string label = map.Label(cell, side);
                if (!CastleVocabulary.IsBridgeLabel(label))
                    continue;
                int next = map.Grid.Neighbor(cell, side);
                if (next < 0 || !CastleVocabulary.BridgeConnects(label, map.Label(next, side.Opposite())))
                {
                    report.Add(ViolationType.IncompleteBridge, position, $"{map.StateName(cell)} urwany na {side}");
                    return;
                }
            }
        }
    }

    /// <summary>Wygodny dostęp do mapy: grupa, nazwa, obrót i etykiety stanu komórki.</summary>
    sealed class MapView
    {
        readonly CompiledRules rules;
        readonly int[] states;

        public MapView(CompiledRules rules, GridShape grid, int[] states)
        {
            this.rules = rules;
            Grid = grid;
            this.states = states;
        }

        public GridShape Grid { get; }

        public int State(int cell) => states[cell];

        public string Group(int cell) => states[cell] < 0 ? null : rules.GetTile(states[cell]).Group;

        public string Name(int cell) => states[cell] < 0 ? null : rules.GetTile(states[cell]).Name;

        public int Rotation(int cell) => states[cell] < 0 ? -1 : rules.GetRotation(states[cell]);

        public string Label(int cell, Direction direction) => states[cell] < 0 ? null : rules.GetLabel(states[cell], direction);

        public string StateName(int cell) => states[cell] < 0 ? "pusta komórka" : rules.GetStateName(states[cell]);

        public Direction? SideWithLabel(int cell, string label)
        {
            foreach (Direction side in DirectionExtensions.Sides)
                if (Label(cell, side) == label)
                    return side;
            return null;
        }

        /// <summary>Liczba kolejnych komórek z podanych grup od tej komórki w górę.</summary>
        public int ColumnHeight(int cell, params string[] groups)
        {
            int height = 0;
            while (cell >= 0 && Array.IndexOf(groups, Group(cell)) >= 0)
            {
                height++;
                cell = Grid.Neighbor(cell, Direction.Up);
            }
            return height;
        }

        /// <summary>
        /// Spójne obszary komórek parteru z podanych grup (sąsiedztwo przez boki). Bok z etykietą outerWall nie łączy
        /// komórek: to zewnętrzna ściana budynku, więc za nią stoi już inny budynek (wariant reguł bez wolnego pola).
        /// </summary>
        public List<List<int>> GroundComponents(string outerWall, params string[] groups)
        {
            var components = new List<List<int>>();
            var visited = new bool[Grid.CellsPerFloor];
            var stack = new Stack<int>();
            for (int start = 0; start < Grid.CellsPerFloor; start++)
            {
                if (visited[start] || Array.IndexOf(groups, Group(start)) < 0)
                    continue;
                var component = new List<int>();
                visited[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int cell = stack.Pop();
                    component.Add(cell);
                    foreach (Direction side in DirectionExtensions.Sides)
                    {
                        int next = Grid.Neighbor(cell, side);
                        if (next >= 0 && !visited[next] && Array.IndexOf(groups, Group(next)) >= 0 && (outerWall == null || Label(cell, side) != outerWall))
                        {
                            visited[next] = true;
                            stack.Push(next);
                        }
                    }
                }
                component.Sort();
                components.Add(component);
            }
            return components;
        }

        /// <summary>Podstawy wież: ćwiartki wieży na parterze połączone stykami ćwiartek; poprawna wieża to 4 ćwiartki 2 × 2.</summary>
        public List<List<int>> TowerFootprints() => GroundComponents(CastleVocabulary.LabelTowerWall, CastleVocabulary.GroupTower);

        /// <summary>Podstawy donżonów: kafelki donżonu na parterze połączone wewnętrznymi stykami pierścienia.</summary>
        public List<List<int>> KeepFootprints() => GroundComponents(CastleVocabulary.LabelKeepWall, CastleVocabulary.GroupKeep);
    }
}
