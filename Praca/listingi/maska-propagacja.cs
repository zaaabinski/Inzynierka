// Fragment wygenerowany z pomocą AI (Claude Code).
while (data.QueueCount > 0)
{
  // ...
  int cell = data.Queue[data.QueueHead];
  data.QueueHead = data.QueueHead + 1 == data.CellCount ? 0 : data.QueueHead + 1;
  data.QueueCount--;
  data.Queued[cell] = 0;

  for (int i = 0; i < span; i++)
    allowed[i] = 0;
  ulong* source = data.Possibilities + cell * words;
  for (int word = 0; word < words; word++)
    for (ulong bits = source[word]; bits != 0; bits &= bits - 1)
    {
      ulong* masks = data.NeighborMasks + (word * BitMask.BitsPerWord + math.tzcnt(bits)) * span;
      for (int i = 0; i < span; i++)
        allowed[i] |= masks[i];
    }

  int* around = data.Neighbors + cell * Directions;
  for (int direction = 0; direction < Directions; direction++)
  {
    int neighbor = around[direction];
    if (neighbor < 0)
      continue;
    data.PropagationSteps++;
    ulong* target = data.Possibilities + neighbor * words;
    ulong* mask = allowed + direction * words;
    int removed = 0;
    ulong left = 0;
    for (int word = 0; word < words; word++)
    {
      ulong kept = target[word] & mask[word];
      removed += math.countbits(target[word] ^ kept);
      left |= kept;
    }
    if (removed == 0)
      continue;
    // ...
    for (int word = 0; word < words; word++)
      target[word] &= mask[word];
    data.RemovedPossibilities += removed;
    if (left == 0)
    {
      data.ContradictionCell = neighbor;
      return;
    }
    Push(data.Changed, data.ChangedFlags, data.ChangedHead, ref data.ChangedCount, ref data.ChangedPeak, data.CellCount, neighbor);
    Push(data.Queue, data.Queued, data.QueueHead, ref data.QueueCount, ref data.QueuePeak, data.CellCount, neighbor);
  }
}
