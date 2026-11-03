using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WFC.Core;
using WFC.Runtime;

namespace WFC.Editor
{
    /// <summary>
    /// Narzędzie edytora (menu WFC → Generator zamków): parametry generacji, generacja rozłożona na kawałki z paskiem
    /// postępu i anulowaniem, podgląd scalonej mapy w scenie, statystyki przed scaleniem i po nim, zapis jako prefab.
    /// <para>
    /// Podgląd nie zapisuje się ze sceną (HideFlags.DontSave) i znika przy zamknięciu okna, przed trybem Play i przed
    /// przeładowaniem kodu; razem z nim CombinedMap niszczy swoje siatki, materiał i paletę, więc nic nie zostaje w pamięci.
    /// Trwałą kopię daje MapBaker.
    /// </para>
    /// </summary>
    public sealed class GeneratorWindow : EditorWindow
    {
        public const string PreviewRootName = "[Podgląd generatora]";

        const double FrameBudgetMs = 16;

        [SerializeField] GeneratorToolSettings settings = new GeneratorToolSettings();

        EditorGeneration generation;
        EditorGeneration shown;
        GameObject previewRoot;
        CombinedMap map;
        int3 framedSize;
        string report = string.Empty;

        UnsignedIntegerField seedField;
        ProgressBar progressBar;
        HelpBox problemBox;
        Label reportLabel;
        Button generateButton;
        Button nextSeedButton;
        Button cancelButton;
        Button clearButton;
        Button saveButton;
        Button placeButton;

        public GeneratorToolSettings Settings => settings;

        public bool IsGenerating => generation != null;

        /// <summary>Pokazana mapa albo null.</summary>
        public CombinedMap Map => map;

        public GameObject PreviewRoot => previewRoot;

        public string Report => report;

        /// <summary>Folder zapisu prefabów, zmieniany w testach.</summary>
        public string BakeFolder { get; set; } = WfcPaths.GeneratedCastles;

        [MenuItem("WFC/Generator zamków", priority = 10)]
        static void Open()
        {
            var window = GetWindow<GeneratorWindow>("Generator zamków");
            window.minSize = new Vector2(360f, 480f);
        }

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneView.duringSceneGui += DrawGridOutline;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.duringSceneGui -= DrawGridOutline;
            ClearPreview();
        }

        void CreateGUI() => BuildInterface(rootVisualElement);

        /// <summary>Buduje interfejs; testy wywołują to bez otwierania okna.</summary>
        public void BuildInterface(VisualElement root)
        {
            if (settings.Tileset == null)
                settings.Tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset("Pelny"));

            var scroll = new ScrollView();
            scroll.contentContainer.style.paddingLeft = 8f;
            scroll.contentContainer.style.paddingRight = 8f;
            scroll.contentContainer.style.paddingTop = 4f;
            scroll.contentContainer.style.paddingBottom = 8f;
            root.Add(scroll);

            scroll.Add(MakeHeader("Zestaw i siatka"));
            var tilesetField = new ObjectField("Zestaw") { name = "zestaw", objectType = typeof(TilesetDefinition), allowSceneObjects = false, value = settings.Tileset };
            tilesetField.RegisterValueChangedCallback(change =>
            {
                settings.Tileset = (TilesetDefinition)change.newValue;
                Refresh();
            });
            scroll.Add(tilesetField);
            scroll.Add(MakePositive("wymiar-e", "E (x)", settings.East, value => settings.East = value));
            scroll.Add(MakePositive("wymiar-n", "N (z)", settings.North, value => settings.North = value));
            scroll.Add(MakePositive("pietra", "Piętra", settings.Floors, value => settings.Floors = value));

            scroll.Add(MakeHeader("Algorytm"));
            scroll.Add(MakeChoice("rozwiazywacz", "Rozwiązywacz", settings.Solver, value => settings.Solver = value));
            scroll.Add(MakeChoice("heurystyka", "Heurystyka", settings.Selector, value => settings.Selector = value));
            scroll.Add(MakeChoice("sprzecznosc", "Sprzeczność", settings.Contradiction, value => settings.Contradiction = value));
            seedField = new UnsignedIntegerField("Ziarno") { name = "ziarno", value = settings.Seed, isDelayed = true };
            seedField.RegisterValueChangedCallback(change => settings.Seed = change.newValue);
            scroll.Add(seedField);
            scroll.Add(MakePositive("limit-prob", "Limit prób", settings.MaxAttempts, value => settings.MaxAttempts = value));

            scroll.Add(MakeHeader("Scalanie"));
            var atlasField = new Toggle("Atlas materiałów") { name = "atlas", value = settings.UseAtlas, tooltip = "Jeden materiał z paletą kolorów, czyli jedno wywołanie rysowania na fragment. Bez atlasu: podsiatka na materiał." };
            atlasField.RegisterValueChangedCallback(change =>
            {
                settings.UseAtlas = change.newValue;
                RebuildShownMap();
            });
            scroll.Add(atlasField);
            IntegerField chunkField = MakePositive("fragment", "Bok fragmentu", settings.ChunkSize, value =>
            {
                settings.ChunkSize = value;
                RebuildShownMap();
            });
            chunkField.tooltip = "Bok fragmentu mapy w kolumnach siatki. Każdy fragment to osobna siatka, którą kamera może pominąć.";
            scroll.Add(chunkField);

            problemBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning) { name = "problem" };
            scroll.Add(problemBox);

            var mainRow = MakeRow();
            generateButton = MakeButton(mainRow, "generuj", "Generuj", Generate);
            nextSeedButton = MakeButton(mainRow, "nastepne-ziarno", "Następne ziarno", NextSeed);
            cancelButton = MakeButton(mainRow, "anuluj", "Anuluj", Cancel);
            scroll.Add(mainRow);

            progressBar = new ProgressBar { name = "postep", lowValue = 0f, highValue = 1f };
            scroll.Add(progressBar);

            var resultRow = MakeRow();
            clearButton = MakeButton(resultRow, "wyczysc", "Wyczyść", ClearPreview);
            saveButton = MakeButton(resultRow, "zapisz", "Zapisz jako prefab", () => SaveAsPrefab());
            placeButton = MakeButton(resultRow, "zapisz-wstaw", "Zapisz i wstaw do sceny", () => SaveAsPrefab(true));
            scroll.Add(resultRow);

            scroll.Add(MakeHeader("Wynik"));
            reportLabel = new Label { name = "raport", enableRichText = false };
            reportLabel.selection.isSelectable = true;
            reportLabel.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(reportLabel);

            Refresh();
        }

        /// <summary>Zaczyna nową generację; Tick wykonuje ją kawałkami przy każdej aktualizacji edytora.</summary>
        public void Generate()
        {
            string problem = SettingsProblem();
            if (problem != null)
            {
                report = problem;
                Refresh();
                return;
            }
            generation = null;
            try
            {
                generation = new EditorGeneration(settings);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                report = $"Nie udało się zacząć generacji: {exception.Message}";
            }
            Refresh();
        }

        public void NextSeed()
        {
            settings.Seed++;
            seedField?.SetValueWithoutNotify(settings.Seed);
            Generate();
        }

        /// <summary>Przerywa generację; pokazana mapa zostaje bez zmian.</summary>
        public void Cancel()
        {
            if (generation == null)
                return;
            generation = null;
            report = "Generacja przerwana.";
            Refresh();
        }

        /// <summary>Wykonuje kawałek generacji, a po jej końcu pokazuje mapę.</summary>
        public void Tick(double budgetMs)
        {
            if (generation == null)
                return;
            try
            {
                generation.Advance(budgetMs);
                if (generation.IsFinished)
                {
                    shown = generation;
                    generation = null;
                    ShowResult();
                }
            }
            catch (Exception exception)
            {
                generation = null;
                Debug.LogException(exception, this);
                report = $"Błąd generacji: {exception.Message}";
            }
            Refresh();
        }

        /// <summary>Scala pokazaną mapę od nowa z bieżącym atlasem i bokiem fragmentu, bez nowej generacji.</summary>
        public void RebuildShownMap()
        {
            if (shown != null)
                ShowResult();
            Refresh();
        }

        /// <summary>Usuwa podgląd i zwalnia jego siatki, materiał i paletę.</summary>
        public void ClearPreview()
        {
            generation = null;
            shown = null;
            map?.Dispose();
            map = null;
            if (previewRoot != null)
                DestroyImmediate(previewRoot);
            previewRoot = null;
            report = string.Empty;
            Refresh();
        }

        /// <summary>Zapisuje pokazaną mapę jako zasoby; na życzenie wstawia prefab do sceny (z Cofnij) w miejsce podglądu.</summary>
        /// <returns>Zasób prefabu albo null, gdy nie ma mapy do zapisania.</returns>
        public GameObject SaveAsPrefab(bool placeInScene = false)
        {
            if (map == null || map.RendererCount == 0)
                return null;
            GenerationResult result = shown.Result;
            GameObject prefab;
            try
            {
                prefab = MapBaker.Bake(map, BakeFolder, $"Zamek_{shown.Tileset.name}_{result.Size.x}x{result.Size.z}x{result.Size.y}_ziarno{result.Seed}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                report += $"\nZapis się nie udał: {exception.Message}";
                Refresh();
                return null;
            }

            string saved = $"\nZapisano: {AssetDatabase.GetAssetPath(prefab)}, siatki na dysku {MapBaker.MeshesFileSize(prefab) / (1024.0 * 1024.0):0.0} MB.";
            if (placeInScene)
            {
                string shownReport = report;
                ClearPreview();
                report = shownReport;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(instance, "Wstaw zamek");
                Selection.activeGameObject = instance;
                saved += " Wstawiono do sceny.";
            }
            else
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
            report += saved;
            Refresh();
            return prefab;
        }

        void OnEditorUpdate() => Tick(FrameBudgetMs);

        void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                ClearPreview();
            else
                Refresh();
        }

        string SettingsProblem()
        {
            if (settings.Tileset == null)
                return "Wybierz zestaw kafelków.";
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Okno generatora działa poza trybem Play. W trybie Play użyj okna ustawień aplikacji.";
            if (!WfcFactory.IsSupported(settings.Solver, settings.Contradiction))
                return $"Rozwiązywacz „{WfcFactory.Label(settings.Solver)}” nie obsługuje reakcji „{WfcFactory.Label(settings.Contradiction)}”. Wybierz inny rozwiązywacz albo start od nowa.";
            return null;
        }

        /// <summary>Pokazuje wynik generacji; zmiana atlasu albo fragmentu przebudowuje samo scalanie, bez nowej generacji.</summary>
        void ShowResult()
        {
            if (previewRoot == null)
                previewRoot = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            if (map == null || map.UsesAtlas != settings.UseAtlas || map.ChunkSize != settings.ChunkSize)
            {
                map?.Dispose();
                map = new CombinedMap(previewRoot.transform, settings.ChunkSize, settings.UseAtlas, true, HideFlags.DontSave);
            }
            GenerationResult result = shown.Result;
            map.Build(shown.Tileset, shown.Rules, result.Grid, result.States, result.Seed);
            report = $"{shown.Describe(map.LastBuildMs)}\n{map.Describe()}";
            FrameSceneView(result.Size);
        }

        void FrameSceneView(int3 size)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || math.all(size == framedSize))
                return;
            framedSize = size;
            Vector3 extent = new Vector3(size.x, size.y, size.z) * UnityGrid.CellSize;
            view.Frame(new Bounds(extent / 2f, extent), false);
        }

        /// <summary>Obrys siatki z bieżących parametrów, widoczny jeszcze przed generacją.</summary>
        void DrawGridOutline(SceneView view)
        {
            UnityEditor.Handles.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Vector3 size = new Vector3(settings.East, settings.Floors, settings.North) * UnityGrid.CellSize;
            UnityEditor.Handles.DrawWireCube(size / 2f, size);
        }

        void Refresh()
        {
            if (reportLabel == null)
                return;
            string problem = SettingsProblem();
            problemBox.text = problem ?? string.Empty;
            problemBox.style.display = problem != null ? DisplayStyle.Flex : DisplayStyle.None;
            generateButton.SetEnabled(problem == null && !IsGenerating);
            nextSeedButton.SetEnabled(problem == null && !IsGenerating);
            cancelButton.SetEnabled(IsGenerating);
            clearButton.SetEnabled(map != null || IsGenerating);
            saveButton.SetEnabled(map != null && map.RendererCount > 0 && !IsGenerating);
            placeButton.SetEnabled(map != null && map.RendererCount > 0 && !IsGenerating);
            progressBar.style.display = IsGenerating ? DisplayStyle.Flex : DisplayStyle.None;
            if (IsGenerating)
            {
                int decided = generation.DecidedCells;
                progressBar.value = (float)decided / generation.CellCount;
                progressBar.title = $"Próba {generation.Attempt}: ustalone komórki {decided} z {generation.CellCount}";
            }
            reportLabel.text = report;
        }

        static Label MakeHeader(string text)
        {
            var header = new Label(text);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 8f;
            header.style.marginBottom = 2f;
            return header;
        }

        static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 6f;
            return row;
        }

        static Button MakeButton(VisualElement row, string name, string text, Action action)
        {
            var button = new Button(action) { name = name, text = text };
            button.style.flexGrow = 1f;
            button.style.flexBasis = 0f;
            row.Add(button);
            return button;
        }

        IntegerField MakePositive(string name, string label, int value, Action<int> apply)
        {
            var field = new IntegerField(label) { name = name, value = value, isDelayed = true };
            field.RegisterValueChangedCallback(change =>
            {
                int positive = Mathf.Max(1, change.newValue);
                field.SetValueWithoutNotify(positive);
                apply(positive);
                Refresh();
            });
            return field;
        }

        EnumField MakeChoice<T>(string name, string label, T value, Action<T> apply) where T : Enum
        {
            var field = new EnumField(label, value) { name = name };
            field.RegisterValueChangedCallback(change =>
            {
                apply((T)change.newValue);
                Refresh();
            });
            return field;
        }
    }
}
