// Fragment wygenerowany z pomocą AI (Claude Code).
public void Dispose()
{
  foreach (Chunk chunk in chunks)
  {
    DestroyObject(chunk.Mesh);
    DestroyObject(chunk.Object);
  }
  chunks = Array.Empty<Chunk>();
  atlas?.Dispose();
  atlas = null;
  shownTileset = null;
  hasMap = false;
}

// ...
finally
{
  // Zadania muszą skończyć, zanim zwolnimy ich dane, także po wyjątku w połowie.
  JobHandle.CombineDependencies(handles).Complete();
  if (!applied)
    output.Dispose();
  handles.Dispose();
  positions.Dispose();
  normals.Dispose();
  indices.Dispose();
  geometry.Dispose();
  poses.Dispose();
  instances.Dispose();
  boundsMin.Dispose();
  boundsMax.Dispose();
}
