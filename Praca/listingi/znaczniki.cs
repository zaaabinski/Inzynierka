// Fragment wygenerowany z pomocą AI (Claude Code).
public static class WfcMarkers
{
  public static readonly ProfilerMarker Generation = new ProfilerMarker("WFC.Generacja");
  public static readonly ProfilerMarker CellSelection = new ProfilerMarker("WFC.WyborKomorki");
  public static readonly ProfilerMarker Propagation = new ProfilerMarker("WFC.Propagacja");
  public static readonly ProfilerMarker Backtracking = new ProfilerMarker("WFC.Cofanie");
  public static readonly ProfilerMarker MapCheck = new ProfilerMarker("WFC.SprawdzenieMapy");
  public static readonly ProfilerMarker MapShow = new ProfilerMarker("WFC.StawianieMapy");
  public static readonly ProfilerMarker MeshCombine = new ProfilerMarker("WFC.Scalanie");
}

// ...
bool Propagate(bool initial)
{
  long start = Stopwatch.GetTimestamp();
  WfcMarkers.Propagation.Begin();
  try
  {
    return initial ? PropagateInitial() : PropagateDecision();
  }
  finally
  {
    WfcMarkers.Propagation.End();
    metrics.PropagationTicks += Stopwatch.GetTimestamp() - start;
  }
}
