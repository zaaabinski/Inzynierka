using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WFC.Runtime
{
    /// <summary>Jak Unity łączy obiekty przy rysowaniu: ustawienia URP porównywane w eksperymencie E7.</summary>
    public enum RenderBatching
    {
        [InspectorName("SRP Batcher + Resident Drawer")]
        SrpBatcherAndResidentDrawer,

        [InspectorName("Sam SRP Batcher")]
        SrpBatcher,

        [InspectorName("Samo instancjonowanie")]
        InstancingOnly
    }

    /// <summary>
    /// Przełącza RenderBatching w działającej aplikacji. Nowe ustawienie działa od następnej klatki. W edytorze zmiana
    /// trafia do zasobu URP, dlatego BenchmarkRunner przywraca na koniec ustawienie projektu (RestoreProjectSetting).
    /// </summary>
    public static class RenderBatchingSwitch
    {
        static RenderBatching? projectSetting;

        static UniversalRenderPipelineAsset Asset => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        public static RenderBatching Current
        {
            get
            {
                UniversalRenderPipelineAsset asset = Asset;
                if (asset == null || !asset.useSRPBatcher)
                    return RenderBatching.InstancingOnly;
                return asset.gpuResidentDrawerMode == GPUResidentDrawerMode.Disabled ? RenderBatching.SrpBatcher : RenderBatching.SrpBatcherAndResidentDrawer;
            }
        }

        public static void Apply(RenderBatching batching)
        {
            UniversalRenderPipelineAsset asset = Asset;
            if (asset == null || batching == Current)
                return;
            projectSetting ??= Current;
            bool srpBatcher = batching != RenderBatching.InstancingOnly;
            asset.useSRPBatcher = srpBatcher;
            GraphicsSettings.useScriptableRenderPipelineBatching = srpBatcher;
            asset.gpuResidentDrawerMode = batching == RenderBatching.SrpBatcherAndResidentDrawer ? GPUResidentDrawerMode.InstancedDrawing : GPUResidentDrawerMode.Disabled;
        }

        public static void RestoreProjectSetting()
        {
            if (projectSetting is RenderBatching setting)
                Apply(setting);
            projectSetting = null;
        }
    }
}
