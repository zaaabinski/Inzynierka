// Fragment wygenerowany z pomocą AI (Claude Code).
string Label(int state, Direction direction) => labels[state * DirectionExtensions.Count + (int)direction];
bool VerticalFits(string below, string above) => below == above || exceptions.Contains((below, above));

bool Fits(int state, Direction direction, int neighbor)
{
  switch (direction)
  {
    case Direction.Up: return VerticalFits(Label(state, Direction.Up), Label(neighbor, Direction.Down));
    case Direction.Down: return VerticalFits(Label(neighbor, Direction.Up), Label(state, Direction.Down));
    default: return pairs.Contains((Label(state, direction), Label(neighbor, direction.Opposite())));
  }
}

// Sąsiedztwo: lista i maska dla każdej pary (stan, kierunek).
var neighborStart = new int[stateCount * DirectionExtensions.Count + 1];
var neighborStates = new List<int>();
var neighborMasks = new ulong[stateCount * DirectionExtensions.Count * words];
for (int state = 0; state < stateCount; state++)
  foreach (Direction direction in DirectionExtensions.All)
  {
    int index = state * DirectionExtensions.Count + (int)direction;
    neighborStart[index] = neighborStates.Count;
    Span<ulong> mask = neighborMasks.AsSpan(index * words, words);
    for (int neighbor = 0; neighbor < stateCount; neighbor++)
      if (Fits(state, direction, neighbor) && !RemovedByTightening(state, direction, neighbor, tileset))
      {
        neighborStates.Add(neighbor);
        BitMask.Set(mask, neighbor);
      }
  }
neighborStart[stateCount * DirectionExtensions.Count] = neighborStates.Count;
