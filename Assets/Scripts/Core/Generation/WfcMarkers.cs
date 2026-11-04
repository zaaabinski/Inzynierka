#if UNITY_5_3_OR_NEWER
using Unity.Profiling;
#endif

namespace WFC.Core
{
    /// <summary>
    /// Znaczniki etapów generacji i stawiania mapy, widoczne w oknie Unity Profiler (tryb Play albo build Development,
    /// w wersji Release nic nie kosztują). Czasy w pliku wyników liczy osobno Stopwatch.
    /// </summary>
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

#if !UNITY_5_3_OR_NEWER
    /// <summary>Zaślepka poza Unity, gdzie rdzeń liczy się bez silnika.</summary>
    public readonly struct ProfilerMarker
    {
        public ProfilerMarker(string name)
        {
        }

        public void Begin()
        {
        }

        public void End()
        {
        }
    }
#endif
}
