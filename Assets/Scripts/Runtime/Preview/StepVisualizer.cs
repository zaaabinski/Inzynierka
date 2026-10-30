using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using WFC.Core;

namespace WFC.Runtime
{
    /// <summary>
    /// Podgląd generacji krok po kroku (kamień 5): odtwarzanie z wybraną szybkością, kolor nieustalonej komórki według
    /// liczby możliwości, ostatnia decyzja i miejsce sprzeczności, piętro po piętrze. Ustalone komórki stawia MapBuilder
    /// jak zwykle, a znaczniki rysuje karta graficzna instancjami, bez obiektów w scenie. Zrzut ekranu po każdym
    /// skończonym piętrze pozwala nagrać generację obraz po obrazie. GeneratorController dodaje ten komponent sam
    /// w trybie Play, więc scena Generator nie musi go mieć.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StepVisualizer : MonoBehaviour
    {
        public const float MinStepsPerSecond = 1f;
        public const float MaxStepsPerSecond = 2000f;

        /// <summary>Dolne granice przedziałów liczby możliwości: 2–3, 4–7, 8–15, 16–31, 32–63, 64 i więcej.</summary>
        public static readonly int[] BandStarts = { 2, 4, 8, 16, 32, 64 };

        /// <summary>Kolory przedziałów: od czerwieni (prawie ustalona) do fioletu (prawie wszystko możliwe).</summary>
        public static readonly Color[] BandColors =
        {
            new Color(0.95f, 0.30f, 0.20f), new Color(1.00f, 0.60f, 0.15f), new Color(0.95f, 0.85f, 0.25f),
            new Color(0.40f, 0.80f, 0.35f), new Color(0.25f, 0.70f, 0.95f), new Color(0.50f, 0.40f, 0.95f)
        };

        public static readonly Color DecisionColor = new Color(1f, 0.92f, 0.2f);
        public static readonly Color ContradictionColor = new Color(0.9f, 0.08f, 0.1f);

        const string MaterialResource = "Materials/M_PodgladKrokow";
        const int MaxStepsPerFrame = 500;
        const int MaxPerDraw = 1023;
        const float ContradictionSeconds = 1.5f;

        /// <summary>Znacznik możliwości: kostka o boku 35% komórki w jej środku.</summary>
        const float MarkerSize = 0.35f;

        [SerializeField] GeneratorController generator;
        [Tooltip("Materiał znaczników (URP Unlit z instancjonowaniem). Pusty: Resources/Materials/M_PodgladKrokow.")]
        [SerializeField] Material markerMaterial;
        [Tooltip("Kroki (decyzje) na sekundę przy odtwarzaniu.")]
        [SerializeField, Range(MinStepsPerSecond, MaxStepsPerSecond)] float stepsPerSecond = 30f;
        [SerializeField] bool showPossibilities = true;
        [Tooltip("Znaczniki możliwości tylko na piętrze ostatniej decyzji.")]
        [SerializeField] bool currentFloorOnly = true;
        [Tooltip("Zrzut ekranu po każdym skończonym piętrze i na końcu, do Wyniki/Zrzuty/Kroki_<data>.")]
        [SerializeField] bool screenshotEachFloor;

        readonly Matrix4x4[][] bandMatrices = new Matrix4x4[BandStarts.Length][];
        readonly int[] bandCounts = new int[BandStarts.Length];
        RenderParams[] bandParams;
        RenderParams decisionParams;
        RenderParams contradictionParams;
        readonly Matrix4x4[] single = new Matrix4x4[1];
        Mesh cube;
        bool ready;

        IWfcSolver markedSolver;
        int markedVersion;
        bool markedFloorOnly;
        int markedFloor;
        bool hasDecisionMarker;
        Matrix4x4 decisionMarker;

        IWfcSolver trackedSolver;
        int seenContradictions;
        int contradictionCell = -1;
        float contradictionUntil;
        bool finishShot;
        string shotFolder;
        float accumulator;

        public GeneratorController Generator
        {
            get => generator;
            set => generator = value;
        }

        public bool IsPlaying { get; private set; }

        public float StepsPerSecond
        {
            get => stepsPerSecond;
            set => stepsPerSecond = Mathf.Clamp(value, MinStepsPerSecond, MaxStepsPerSecond);
        }

        public bool ShowPossibilities
        {
            get => showPossibilities;
            set => showPossibilities = value;
        }

        public bool CurrentFloorOnly
        {
            get => currentFloorOnly;
            set => currentFloorOnly = value;
        }

        public bool ScreenshotEachFloor
        {
            get => screenshotEachFloor;
            set => screenshotEachFloor = value;
        }

        /// <summary>Folder zrzutów pięter; pusty: Wyniki/Zrzuty/Kroki_&lt;data&gt;, nowy dla każdej generacji.</summary>
        public string ScreenshotFolder { get; set; }

        /// <summary>Piętro ostatniej decyzji (0 = parter) albo −1 przed pierwszą decyzją.</summary>
        public int CurrentFloor { get; private set; } = -1;

        /// <summary>Liczba znaczników możliwości w ostatniej klatce.</summary>
        public int MarkerCount { get; private set; }

        /// <summary>Przedział liczby możliwości (indeks w BandStarts) albo −1 dla komórki ustalonej.</summary>
        public static int Band(int possibilities)
        {
            if (possibilities < BandStarts[0])
                return -1;
            int band = 0;
            while (band + 1 < BandStarts.Length && possibilities >= BandStarts[band + 1])
                band++;
            return band;
        }

        public void Play()
        {
            IsPlaying = true;
            accumulator = 0f;
        }

        public void Pause() => IsPlaying = false;

        public void TogglePlay()
        {
            if (IsPlaying)
                Pause();
            else
                Play();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame && !UiInput.IsTyping)
                TogglePlay();
            if (!IsPlaying || generator == null)
                return;

            accumulator += Time.unscaledDeltaTime * stepsPerSecond;
            int steps = (int)accumulator;
            if (steps == 0)
                return;
            accumulator -= steps;
            if (steps > MaxStepsPerFrame)
            {
                steps = MaxStepsPerFrame;
                accumulator = 0f;
            }
            try
            {
                generator.Step(steps);
            }
            catch (Exception exception)
            {
                Pause();
                Debug.LogException(exception, this);
                return;
            }
            if (generator.ActiveSolver == null || generator.ActiveSolver.IsFinished)
                Pause();
        }

        void LateUpdate()
        {
            IWfcSolver solver = generator != null ? generator.ActiveSolver : null;
            MarkerCount = 0;
            if (solver == null || !EnsureResources())
                return;
            TrackEvents(solver);

            if (!solver.IsFinished && showPossibilities)
            {
                if (solver != markedSolver || Version(solver) != markedVersion || currentFloorOnly != markedFloorOnly || CurrentFloor != markedFloor)
                    Rebuild(solver);
                for (int band = 0; band < bandCounts.Length; band++)
                {
                    MarkerCount += bandCounts[band];
                    for (int start = 0; start < bandCounts[band]; start += MaxPerDraw)
                        Graphics.RenderMeshInstanced(bandParams[band], cube, 0, bandMatrices[band], Math.Min(MaxPerDraw, bandCounts[band] - start), start);
                }
                if (hasDecisionMarker)
                    Draw(decisionParams, decisionMarker);
            }
            if (contradictionCell >= 0 && Time.unscaledTime < contradictionUntil && contradictionCell < solver.Grid.CellCount)
                Draw(contradictionParams, CellMatrix(solver.Grid, contradictionCell, Vector3.one * 1.02f, 0.5f));
        }

        /// <summary>Sprzeczności (czerwony znacznik na 1,5 s), bieżące piętro i zrzuty ekranu pięter.</summary>
        void TrackEvents(IWfcSolver solver)
        {
            SolverMetrics metrics = solver.Metrics;
            if (solver != trackedSolver)
            {
                trackedSolver = solver;
                seenContradictions = metrics.Contradictions;
                contradictionCell = -1;
                CurrentFloor = -1;
                finishShot = false;
                shotFolder = null;
            }
            if (metrics.Contradictions > seenContradictions)
            {
                seenContradictions = metrics.Contradictions;
                contradictionCell = solver.LastContradictionCell;
                contradictionUntil = Time.unscaledTime + ContradictionSeconds;
            }

            int floor = solver.LastDecisionCell >= 0 ? solver.Grid.Floor(solver.LastDecisionCell) : CurrentFloor;
            if (screenshotEachFloor && floor > CurrentFloor && CurrentFloor >= 0)
                Capture($"pietro_{CurrentFloor:00}.png");
            CurrentFloor = floor;
            if (screenshotEachFloor && solver.IsFinished && !finishShot)
            {
                finishShot = true;
                Capture("koniec.png");
            }
        }

        void Capture(string fileName)
        {
            shotFolder ??= ScreenshotFolder ?? Path.Combine(ScreenshotTaker.ScreenshotFolder, $"Kroki_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(shotFolder);
            ScreenCapture.CaptureScreenshot(Path.Combine(shotFolder, fileName));
        }

        void Rebuild(IWfcSolver solver)
        {
            markedSolver = solver;
            markedVersion = Version(solver);
            markedFloorOnly = currentFloorOnly;
            markedFloor = CurrentFloor;
            Array.Clear(bandCounts, 0, bandCounts.Length);

            GridShape grid = solver.Grid;
            int fromFloor = currentFloorOnly ? Mathf.Max(0, CurrentFloor) : 0;
            int toFloor = currentFloorOnly ? fromFloor : grid.Floors - 1;
            Vector3 markerScale = Vector3.one * MarkerSize;
            for (int cell = fromFloor * grid.CellsPerFloor; cell < (toFloor + 1) * grid.CellsPerFloor; cell++)
            {
                int band = Band(solver.GetPossibilityCount(cell));
                if (band < 0)
                    continue;
                if (bandCounts[band] == bandMatrices[band].Length)
                    Array.Resize(ref bandMatrices[band], bandCounts[band] * 2);
                bandMatrices[band][bandCounts[band]++] = CellMatrix(grid, cell, markerScale, 0.5f);
            }

            // Ostatnia decyzja: żółta płytka na wierzchu komórki, widoczna nad postawionym modelem.
            hasDecisionMarker = solver.LastDecisionCell >= 0;
            if (hasDecisionMarker)
                decisionMarker = CellMatrix(grid, solver.LastDecisionCell, new Vector3(1.04f, 0.05f, 1.04f), 1.02f);

            Bounds bounds = MapBounds(grid);
            for (int band = 0; band < bandParams.Length; band++)
                bandParams[band].worldBounds = bounds;
            decisionParams.worldBounds = bounds;
            contradictionParams.worldBounds = bounds;
        }

        /// <summary>Kostka w komórce: skala jako ułamek boku komórki, wysokość środka jako ułamek wysokości komórki.</summary>
        Matrix4x4 CellMatrix(GridShape grid, int cell, Vector3 scale, float heightFraction)
        {
            var position = grid.Coordinates(cell);
            Vector3 center = UnityGrid.CellPosition(position.x, position.y, position.z) + Vector3.up * (UnityGrid.CellSize * heightFraction);
            return MapToWorld * Matrix4x4.TRS(center, Quaternion.identity, scale * UnityGrid.CellSize);
        }

        Matrix4x4 MapToWorld => generator != null && generator.MapBuilder != null ? generator.MapBuilder.MapRoot.localToWorldMatrix : Matrix4x4.identity;

        Bounds MapBounds(GridShape grid)
        {
            Vector3 size = new Vector3(grid.Width, grid.Floors, grid.Depth) * UnityGrid.CellSize;
            return new Bounds(MapToWorld.MultiplyPoint3x4(size / 2f), size + Vector3.one * (2f * UnityGrid.CellSize));
        }

        void Draw(RenderParams renderParams, Matrix4x4 matrix)
        {
            single[0] = matrix;
            Graphics.RenderMeshInstanced(renderParams, cube, 0, single, 1);
        }

        static int Version(IWfcSolver solver)
        {
            SolverMetrics metrics = solver.Metrics;
            return unchecked(((metrics.Attempts * 397) ^ metrics.Decisions) * 397 ^ (metrics.Backtracks * 31 + metrics.Contradictions));
        }

        bool EnsureResources()
        {
            if (ready)
                return true;
            Material material = markerMaterial != null ? markerMaterial : Resources.Load<Material>(MaterialResource);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    Debug.LogWarning("[StepVisualizer] Brak materiału Resources/Materials/M_PodgladKrokow i shadera URP Unlit; znaczniki wyłączone.", this);
                    enabled = false;
                    return false;
                }
                material = new Material(shader) { enableInstancing = true };
            }
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            bandParams = new RenderParams[BandStarts.Length];
            for (int band = 0; band < bandParams.Length; band++)
            {
                bandMatrices[band] = new Matrix4x4[256];
                bandParams[band] = Params(material, BandColors[band]);
            }
            decisionParams = Params(material, DecisionColor);
            contradictionParams = Params(material, ContradictionColor);
            ready = true;
            return true;
        }

        static RenderParams Params(Material material, Color color)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            return new RenderParams(material)
            {
                matProps = properties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
        }
    }
}
