using System;
using Unity.Profiling;
using UnityEngine;

namespace WFC.Runtime
{
    /// <summary>
    /// Koszt rysowania mapy, średnie z kilku klatek: liczniki renderowania Unity i czasy klatki.
    /// Null oznacza licznik niedostępny w tym środowisku; takie pole w pliku wyników zostaje puste.
    /// </summary>
    public readonly struct RenderStats
    {
        public RenderStats(long? drawCalls, long? drawnInstances, long? setPassCalls, long? triangles, long? vertices, double frameMs, double? cpuMs, double? gpuMs,
            double? longestFrameMs = null, long? processMemoryBytes = null)
        {
            LongestFrameMs = longestFrameMs;
            ProcessMemoryBytes = processMemoryBytes;
            DrawCalls = drawCalls;
            DrawnInstances = drawnInstances;
            SetPassCalls = setPassCalls;
            Triangles = triangles;
            Vertices = vertices;
            FrameMs = frameMs;
            CpuMs = cpuMs;
            GpuMs = gpuMs;
        }

        /// <summary>Wywołania rysowania na klatkę, razem z cieniami i oknem ustawień.</summary>
        public long? DrawCalls { get; }

        /// <summary>Obiekty narysowane przez te wywołania. Iloraz z DrawCalls mówi, ile obiektów przypada na jedno wywołanie.</summary>
        public long? DrawnInstances { get; }

        /// <summary>Przełączenia stanu renderowania (SetPass), najdroższa część komunikacji z kartą graficzną.</summary>
        public long? SetPassCalls { get; }

        public long? Triangles { get; }

        public long? Vertices { get; }

        /// <summary>Średni czas między klatkami w milisekundach.</summary>
        public double FrameMs { get; }

        /// <summary>Najdłuższa klatka z pomiaru: spadek płynności, którego średnia nie pokaże.</summary>
        public double? LongestFrameMs { get; }

        /// <summary>Pamięć procesu w systemie (licznik System Used Memory).</summary>
        public long? ProcessMemoryBytes { get; }

        /// <summary>Czas pracy procesora nad klatką (FrameTimingManager).</summary>
        public double? CpuMs { get; }

        /// <summary>Czas pracy karty graficznej nad klatką (FrameTimingManager).</summary>
        public double? GpuMs { get; }

        public override string ToString() =>
            $"klatka {FrameMs:0.00} ms, najdłuższa {LongestFrameMs:0.00} ms (procesor {CpuMs:0.00} ms, karta graficzna {GpuMs:0.00} ms), wywołania rysowania {DrawCalls}, narysowane obiekty {DrawnInstances}, " +
            $"SetPass {SetPassCalls}, trójkąty {Triangles}";
    }

    /// <summary>
    /// Zbiera RenderStats przez ProfilerRecorder i FrameTimingManager. Sample wywoływać raz na klatkę; ProfilerRecorder
    /// podaje wartości z poprzedniej klatki, więc pomiar zaczynać (Reset) dopiero kilka klatek po zmianie mapy.
    /// <para>
    /// Wywołania rysowania to suma liczników osobnych dla każdej drogi rysowania. Ogólne „Draw Calls Count” i „Batches Count”
    /// się nie nadają: w wersji Release ich nie ma, a w wersji Development pokazują te same 105 i 7 przy każdej mapie
    /// (pomiar z 3.11.2026, Unity 6000.6.2f1).
    /// </para>
    /// </summary>
    public sealed class RenderStatsRecorder : IDisposable
    {
        static readonly string[] DrawPaths = { "Standard", "Standard Instanced", "Standard Indirect", "SRP Batcher", "BRG", "BRG Indirect", "Null Geometry" };

        readonly ProfilerRecorder[] drawCalls = StartPerPath("Draw Calls Count");
        readonly ProfilerRecorder[] drawnInstances = StartPerPath("Instances Count");
        ProfilerRecorder setPassCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        ProfilerRecorder triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        ProfilerRecorder vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        ProfilerRecorder processMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        readonly FrameTiming[] timing = new FrameTiming[1];
        long drawCallSum;
        long instanceSum;
        long setPassSum;
        long triangleSum;
        long vertexSum;
        double frameSum;
        double longestFrame;
        double cpuSum;
        double gpuSum;
        int timedFrames;

        /// <summary>Klatki zebrane od ostatniego Reset.</summary>
        public int Frames { get; private set; }

        public void Reset()
        {
            drawCallSum = instanceSum = setPassSum = triangleSum = vertexSum = 0;
            frameSum = longestFrame = cpuSum = gpuSum = 0;
            timedFrames = 0;
            Frames = 0;
        }

        public void Sample()
        {
            foreach (ProfilerRecorder recorder in drawCalls)
                drawCallSum += recorder.LastValue;
            foreach (ProfilerRecorder recorder in drawnInstances)
                instanceSum += recorder.LastValue;
            setPassSum += setPassCalls.LastValue;
            triangleSum += triangles.LastValue;
            vertexSum += vertices.LastValue;
            double frameMs = Time.unscaledDeltaTime * 1000.0;
            frameSum += frameMs;
            longestFrame = Math.Max(longestFrame, frameMs);
            Frames++;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                cpuSum += timing[0].cpuFrameTime;
                gpuSum += timing[0].gpuFrameTime;
                timedFrames++;
            }
        }

        public RenderStats Result()
        {
            int frames = Mathf.Max(1, Frames);
            return new RenderStats(
                Average(drawCalls, drawCallSum, frames), Average(drawnInstances, instanceSum, frames), Average(setPassCalls, setPassSum, frames),
                Average(triangles, triangleSum, frames), Average(vertices, vertexSum, frames),
                frameSum / frames,
                timedFrames > 0 ? cpuSum / timedFrames : (double?)null,
                timedFrames > 0 && gpuSum > 0 ? gpuSum / timedFrames : (double?)null,
                longestFrame,
                processMemory.Valid ? processMemory.LastValue : (long?)null);
        }

        public void Dispose()
        {
            foreach (ProfilerRecorder recorder in drawCalls)
                recorder.Dispose();
            foreach (ProfilerRecorder recorder in drawnInstances)
                recorder.Dispose();
            setPassCalls.Dispose();
            triangles.Dispose();
            vertices.Dispose();
            processMemory.Dispose();
        }

        static ProfilerRecorder[] StartPerPath(string counter) =>
            Array.ConvertAll(DrawPaths, path => ProfilerRecorder.StartNew(ProfilerCategory.Render, $"{path} {counter}"));

        static long? Average(ProfilerRecorder[] recorders, long sum, int frames) =>
            Array.Exists(recorders, recorder => recorder.Valid) ? (long?)Math.Round((double)sum / frames) : null;

        static long? Average(ProfilerRecorder recorder, long sum, int frames) =>
            recorder.Valid ? (long?)Math.Round((double)sum / frames) : null;
    }
}
