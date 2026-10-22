using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace WFC.Runtime
{
    /// <summary>
    /// Okno ustawień w aplikacji (UI Toolkit). Dwa tryby: „Jedna mapa” (generowanie i oglądanie) oraz „Badanie”
    /// (seria uruchomień z powtórkami, mapy widoczne w oknie, wyniki w pliku CSV). Klawisz H chowa okno.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GeneratorPanel : MonoBehaviour
    {
        [SerializeField] GeneratorController generator;
        [SerializeField] BenchmarkRunner runner;
        [Tooltip("Zestawy kafelków do wyboru w oknie.")]
        [SerializeField] List<TilesetDefinition> tilesets = new List<TilesetDefinition>();

        const string CustomExperiment = "Własne (z sekcji Ustawienia)";
        static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f);
        static readonly Color Muted = new Color(0.65f, 0.69f, 0.75f);
        static readonly Color Background = new Color(0.07f, 0.09f, 0.12f, 0.9f);
        static readonly Color Accent = new Color(0.22f, 0.45f, 0.78f);
        static readonly Color TabIdle = new Color(0.2f, 0.23f, 0.28f);

        static readonly SolverKind[] Solvers = (SolverKind[])Enum.GetValues(typeof(SolverKind));
        static readonly SelectorKind[] Selectors = (SelectorKind[])Enum.GetValues(typeof(SelectorKind));
        static readonly ContradictionKind[] Contradictions = (ContradictionKind[])Enum.GetValues(typeof(ContradictionKind));

        VisualElement root;
        VisualElement panel;
        VisualElement singleSection;
        VisualElement seriesSection;
        Button singleTab;
        Button seriesTab;
        DropdownField tilesetField;
        DropdownField solverField;
        DropdownField selectorField;
        DropdownField contradictionField;
        IntegerField sizeEastField;
        IntegerField sizeNorthField;
        IntegerField floorsField;
        IntegerField maxAttemptsField;
        IntegerField seedField;
        Label reportLabel;
        DropdownField experimentField;
        IntegerField firstSeedField;
        IntegerField seedCountField;
        IntegerField repetitionsField;
        IntegerField warmupField;
        IntegerField showEveryField;
        Label summaryLabel;
        Button startButton;
        Button pauseButton;
        Button stopButton;
        ProgressBar progressBar;
        Label statusLabel;
        Label outputLabel;
        readonly List<Button> singleButtons = new List<Button>();
        bool wasRunning;

        public GeneratorController Generator
        {
            get => generator;
            set => generator = value;
        }

        public BenchmarkRunner Runner
        {
            get => runner;
            set => runner = value;
        }

        public List<TilesetDefinition> Tilesets => tilesets;

        void OnEnable() => TryBuild();

        // Jeśli UIDocument utworzył swój panel dopiero po OnEnable tego komponentu.
        void Start()
        {
            if (root == null || root.panel == null)
                TryBuild();
        }

        void TryBuild()
        {
            var document = GetComponent<UIDocument>();
            if (document.rootVisualElement != null)
                Build(document.rootVisualElement);
        }

        void OnDisable()
        {
            UiInput.PointerOverUi = false;
            UiInput.IsTyping = false;
        }

        /// <summary>Buduje okno w podanym elemencie (publiczne dla testów).</summary>
        public void Build(VisualElement rootElement)
        {
            root = rootElement;
            root.Clear();

            panel = new VisualElement { name = "panel" };
            panel.style.position = Position.Absolute;
            panel.style.left = 12;
            panel.style.top = 12;
            panel.style.width = 400;
            panel.style.maxHeight = Length.Percent(96);
            panel.style.backgroundColor = Background;
            SetRadius(panel, 6);
            SetPadding(panel, 10);
            root.Add(panel);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "przewijanie" };
            panel.Add(scroll);

            scroll.Add(MakeTitle("Generator zamków WFC", 18));
            var tabs = MakeRow();
            singleTab = MakeButton("zakladka-jedna-mapa", "Jedna mapa", () => SetMode(false));
            seriesTab = MakeButton("zakladka-badanie", "Badanie", () => SetMode(true));
            singleTab.style.color = TextColor;
            seriesTab.style.color = TextColor;
            tabs.Add(singleTab);
            tabs.Add(seriesTab);
            scroll.Add(tabs);

            scroll.Add(MakeTitle("Ustawienia", 14));
            tilesetField = MakeDropdown("zestaw", "Zestaw", tilesets.Select(t => t != null ? t.name : "(brak)").ToList(), Math.Max(0, tilesets.IndexOf(generator != null ? generator.Tileset : null)));
            solverField = MakeDropdown("rozwiazywacz", "Rozwiązywacz", Solvers.Select(s => WfcFactory.Label(s)).ToList(), Array.IndexOf(Solvers, generator != null ? generator.Solver : SolverKind.Bitset));
            selectorField = MakeDropdown("heurystyka", "Heurystyka", Selectors.Select(s => WfcFactory.Label(s)).ToList(), Array.IndexOf(Selectors, generator != null ? generator.Selector : SelectorKind.LevelFirst));
            contradictionField = MakeDropdown("sprzecznosc", "Sprzeczność", Contradictions.Select(c => WfcFactory.Label(c)).ToList(), Array.IndexOf(Contradictions, generator != null ? generator.Contradiction : ContradictionKind.Restart));
            scroll.Add(tilesetField);
            scroll.Add(solverField);
            scroll.Add(selectorField);
            scroll.Add(contradictionField);
            var size = generator != null ? generator.GridSize : new Unity.Mathematics.int3(20, 13, 20);
            var sizeRow = MakeRow();
            sizeEastField = MakeInteger("rozmiar-e", "E (x)", size.x, 56);
            sizeNorthField = MakeInteger("rozmiar-n", "N (z)", size.z, 56);
            floorsField = MakeInteger("pietra", "Piętra", size.y, 56);
            sizeRow.Add(sizeEastField);
            sizeRow.Add(sizeNorthField);
            sizeRow.Add(floorsField);
            scroll.Add(sizeRow);
            maxAttemptsField = MakeInteger("limit-prob", "Limit prób", generator != null ? generator.MaxAttempts : 1000);
            scroll.Add(maxAttemptsField);

            singleSection = new VisualElement { name = "sekcja-jedna-mapa" };
            singleSection.Add(MakeTitle("Jedna mapa", 14));
            seedField = MakeInteger("ziarno", "Ziarno", generator != null ? (int)generator.Seed : 1);
            singleSection.Add(seedField);
            var actions = MakeRow();
            singleButtons.Clear();
            actions.Add(SingleButton("przycisk-generuj", "Generuj", () => { Apply(); generator.Generate(); }));
            actions.Add(SingleButton("przycisk-krok", "Krok", () => { Apply(); generator.Step(); }));
            actions.Add(SingleButton("przycisk-do-konca", "Do końca", () => { Apply(); generator.RunToEnd(); }));
            singleSection.Add(actions);
            var actions2 = MakeRow();
            actions2.Add(SingleButton("przycisk-nastepne", "Następne ziarno", () => { Apply(); generator.NextSeed(); }));
            actions2.Add(SingleButton("przycisk-wyczysc", "Wyczyść", () => generator.Clear()));
            singleSection.Add(actions2);
            reportLabel = MakeInfo("raport");
            singleSection.Add(reportLabel);
            scroll.Add(singleSection);

            seriesSection = new VisualElement { name = "sekcja-badanie" };
            seriesSection.Add(MakeTitle("Badanie", 14));
            var experimentChoices = new List<string> { CustomExperiment };
            if (runner != null)
                experimentChoices.AddRange(runner.Experiments.Where(e => e != null).Select(ExperimentLabel));
            experimentField = MakeDropdown("eksperyment", "Eksperyment", experimentChoices, 0);
            experimentField.RegisterValueChangedCallback(_ => OnExperimentChanged());
            seriesSection.Add(experimentField);
            firstSeedField = MakeInteger("pierwsze-ziarno", "Pierwsze ziarno", 1, 160);
            seedCountField = MakeInteger("liczba-ziaren", "Liczba ziaren", 100, 160);
            repetitionsField = MakeInteger("powtorzenia", "Powtórzenia ziarna", 1, 160);
            warmupField = MakeInteger("rozgrzewka", "Rozgrzewka na wariant", 5, 160);
            showEveryField = MakeInteger("pokazuj-co", "Pokazuj co N-tą mapę", 1, 160);
            seriesSection.Add(firstSeedField);
            seriesSection.Add(seedCountField);
            seriesSection.Add(repetitionsField);
            seriesSection.Add(warmupField);
            seriesSection.Add(showEveryField);
            summaryLabel = MakeInfo("podsumowanie");
            seriesSection.Add(summaryLabel);
            var seriesActions = MakeRow();
            startButton = MakeButton("przycisk-start", "Start", StartSeries);
            pauseButton = MakeButton("przycisk-pauza", "Pauza", () => runner?.TogglePause());
            stopButton = MakeButton("przycisk-stop", "Stop", () => runner?.Stop());
            seriesActions.Add(startButton);
            seriesActions.Add(pauseButton);
            seriesActions.Add(stopButton);
            seriesSection.Add(seriesActions);
            progressBar = new ProgressBar { name = "postep", lowValue = 0, highValue = 100, title = "0 z 0" };
            seriesSection.Add(progressBar);
            statusLabel = MakeInfo("status");
            seriesSection.Add(statusLabel);
            outputLabel = MakeInfo("plik-wynikow");
            seriesSection.Add(outputLabel);
            seriesSection.Add(MakeButton("przycisk-folder", "Otwórz folder wyników", OpenResultsFolder));
            scroll.Add(seriesSection);

            var help = MakeInfo("pomoc");
            help.text = "Prawy przycisk myszy: obrót wokół zamku. Kółko: przybliżenie. Środkowy przycisk: przesuwanie widoku. " +
                        "G: następne ziarno. F12: zrzut ekranu. H: schowaj okno.";
            help.style.color = Muted;
            scroll.Add(help);

            UpdateSummary();
            SetMode(runner != null && runner.IsRunning);
        }

        void Update()
        {
            if (root == null || root.panel == null)
                return;
            UpdateUiInput();
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame && !UiInput.IsTyping)
                panel.style.display = panel.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh();
        }

        void UpdateUiInput()
        {
            Mouse mouse = Mouse.current;
            bool over = false;
            if (mouse != null && panel.resolvedStyle.display != DisplayStyle.None)
            {
                Vector2 screen = mouse.position.ReadValue();
                Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
                VisualElement picked = root.panel.Pick(point);
                over = picked != null && (picked == panel || panel.Contains(picked));
            }
            UiInput.PointerOverUi = over;

            var focused = root.panel.focusController?.focusedElement as VisualElement;
            bool typing = false;
            for (VisualElement element = focused; element != null && !typing; element = element.parent)
                typing = element is IntegerField || element is TextField;
            UiInput.IsTyping = typing;
        }

        void Refresh()
        {
            bool running = runner != null && runner.IsRunning;
            // Seria uruchomiona z wiersza poleceń albo z innego miejsca: pokaż ją i jej postęp.
            if (running && !wasRunning)
            {
                SetMode(true);
                ShowConfig(runner.CurrentConfig);
            }
            wasRunning = running;
            foreach (Button button in singleButtons)
                button.SetEnabled(!running && generator != null);
            startButton.SetEnabled(!running && runner != null);
            pauseButton.SetEnabled(running);
            stopButton.SetEnabled(running);
            pauseButton.text = runner != null && runner.IsPaused ? "Wznów" : "Pauza";

            if (generator != null && reportLabel.text != generator.LastReport)
                reportLabel.text = generator.LastReport;
            if (runner == null)
                return;
            if (runner.TotalRuns > 0)
            {
                progressBar.value = 100f * runner.CompletedRuns / runner.TotalRuns;
                progressBar.title = $"Uruchomienie {runner.CompletedRuns} z {runner.TotalRuns}";
            }
            if (statusLabel.text != runner.Status)
                statusLabel.text = runner.Status;
            string output = runner.OutputPath != null ? $"Plik wyników: {runner.OutputPath}" : $"Folder wyników: {BenchmarkSession.ResultsFolder}";
            if (outputLabel.text != output)
                outputLabel.text = output;
        }

        void SetMode(bool series)
        {
            singleSection.style.display = series ? DisplayStyle.None : DisplayStyle.Flex;
            seriesSection.style.display = series ? DisplayStyle.Flex : DisplayStyle.None;
            singleTab.style.backgroundColor = series ? TabIdle : Accent;
            seriesTab.style.backgroundColor = series ? Accent : TabIdle;
        }

        /// <summary>Przenosi ustawienia z okna do GeneratorController.</summary>
        void Apply()
        {
            if (generator == null)
                return;
            if (tilesetField.index >= 0 && tilesetField.index < tilesets.Count)
                generator.Tileset = tilesets[tilesetField.index];
            generator.GridSize = new Unity.Mathematics.int3(Mathf.Max(1, sizeEastField.value), Mathf.Max(1, floorsField.value), Mathf.Max(1, sizeNorthField.value));
            generator.Solver = Solvers[Mathf.Max(0, solverField.index)];
            generator.Selector = Selectors[Mathf.Max(0, selectorField.index)];
            generator.Contradiction = Contradictions[Mathf.Max(0, contradictionField.index)];
            generator.MaxAttempts = maxAttemptsField.value;
            generator.Seed = (uint)Mathf.Max(0, seedField.value);
        }

        void StartSeries()
        {
            if (runner == null)
                return;
            BenchmarkConfig config = SelectedPreset() is BenchmarkConfig preset ? preset.Clone() : CustomConfig();
            config.FirstSeed = (uint)Mathf.Max(0, firstSeedField.value);
            config.SeedCount = seedCountField.value;
            config.Repetitions = repetitionsField.value;
            config.WarmupRuns = warmupField.value;
            config.ShowEveryNth = showEveryField.value;
            try
            {
                runner.StartSeries(config);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                statusLabel.text = exception.Message;
            }
        }

        /// <summary>Eksperyment z jednym wariantem: ustawienia z sekcji Ustawienia.</summary>
        BenchmarkConfig CustomConfig()
        {
            Apply();
            var config = ScriptableObject.CreateInstance<BenchmarkConfig>();
            config.name = "Wlasne";
            config.ExperimentName = "Wlasne";
            config.Solvers.Clear();
            config.Solvers.Add(generator != null ? generator.Solver : SolverKind.Bitset);
            config.Selectors.Clear();
            config.Selectors.Add(generator != null ? generator.Selector : SelectorKind.LevelFirst);
            config.ContradictionHandlers.Clear();
            config.ContradictionHandlers.Add(generator != null ? generator.Contradiction : ContradictionKind.Restart);
            config.Tilesets.Clear();
            if (generator != null && generator.Tileset != null)
                config.Tilesets.Add(generator.Tileset);
            config.GridSizes.Clear();
            config.GridSizes.Add(new Vector3Int(sizeEastField.value, floorsField.value, sizeNorthField.value));
            config.MaxAttempts = maxAttemptsField.value;
            return config;
        }

        BenchmarkConfig SelectedPreset()
        {
            int index = experimentField.index - 1;
            List<BenchmarkConfig> presets = runner != null ? runner.Experiments.Where(e => e != null).ToList() : new List<BenchmarkConfig>();
            return index >= 0 && index < presets.Count ? presets[index] : null;
        }

        /// <summary>Pokazuje w oknie ustawienia serii (np. uruchomionej z wiersza poleceń).</summary>
        void ShowConfig(BenchmarkConfig config)
        {
            if (config == null)
                return;
            List<BenchmarkConfig> presets = runner.Experiments.Where(e => e != null).ToList();
            int preset = presets.FindIndex(e => e.ExperimentName == config.ExperimentName);
            experimentField.SetValueWithoutNotify(experimentField.choices[preset >= 0 ? preset + 1 : 0]);
            firstSeedField.SetValueWithoutNotify((int)config.FirstSeed);
            seedCountField.SetValueWithoutNotify(config.SeedCount);
            repetitionsField.SetValueWithoutNotify(config.Repetitions);
            warmupField.SetValueWithoutNotify(config.WarmupRuns);
            showEveryField.SetValueWithoutNotify(config.ShowEveryNth);
            summaryLabel.text = config.Description + "\n" + config.Summary();
        }

        void OnExperimentChanged()
        {
            BenchmarkConfig preset = SelectedPreset();
            if (preset != null)
            {
                firstSeedField.SetValueWithoutNotify((int)preset.FirstSeed);
                seedCountField.SetValueWithoutNotify(preset.SeedCount);
                repetitionsField.SetValueWithoutNotify(preset.Repetitions);
                warmupField.SetValueWithoutNotify(preset.WarmupRuns);
                showEveryField.SetValueWithoutNotify(preset.ShowEveryNth);
            }
            UpdateSummary();
        }

        void UpdateSummary()
        {
            BenchmarkConfig preset = SelectedPreset();
            if (preset == null)
            {
                summaryLabel.text = "Jeden wariant z sekcji Ustawienia. Każde ziarno to jedna mapa; powtórzenia mierzą czas tej samej mapy jeszcze raz.";
                return;
            }
            string variants = string.Join("\n", preset.Variants().Select(v => "• " + v));
            summaryLabel.text = $"{preset.Description}\n{preset.Summary()}\n{variants}";
        }

        static void OpenResultsFolder()
        {
            Directory.CreateDirectory(BenchmarkSession.ResultsFolder);
            Application.OpenURL("file:///" + BenchmarkSession.ResultsFolder.Replace('\\', '/'));
        }

        static string ExperimentLabel(BenchmarkConfig experiment)
        {
            string description = experiment.Description ?? string.Empty;
            int end = description.IndexOfAny(new[] { '.', '\n' });
            return end > 0 ? $"{experiment.ExperimentName}: {description.Substring(0, end)}" : experiment.ExperimentName;
        }

        Button SingleButton(string elementName, string text, Action action)
        {
            Button button = MakeButton(elementName, text, () =>
            {
                if (generator == null)
                    return;
                try
                {
                    action();
                    seedField.SetValueWithoutNotify((int)generator.Seed);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    reportLabel.text = exception.Message;
                }
            });
            singleButtons.Add(button);
            return button;
        }

        static Label MakeTitle(string text, int size)
        {
            var label = new Label(text);
            label.style.color = TextColor;
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            return label;
        }

        static Label MakeInfo(string elementName)
        {
            var label = new Label { name = elementName };
            label.style.color = TextColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 4;
            label.style.marginBottom = 4;
            return label;
        }

        static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            return row;
        }

        static Button MakeButton(string elementName, string text, Action action)
        {
            var button = new Button(action) { name = elementName, text = text };
            button.style.flexGrow = 1;
            return button;
        }

        static DropdownField MakeDropdown(string elementName, string label, List<string> choices, int index)
        {
            var field = new DropdownField(label, choices, choices.Count == 0 ? -1 : Mathf.Clamp(index, 0, choices.Count - 1)) { name = elementName };
            field.labelElement.style.color = TextColor;
            field.labelElement.style.minWidth = 96;
            return field;
        }

        static IntegerField MakeInteger(string elementName, string label, int value, int labelWidth = 96)
        {
            var field = new IntegerField(label) { name = elementName, value = value };
            field.labelElement.style.color = TextColor;
            field.labelElement.style.minWidth = labelWidth;
            field.labelElement.style.width = labelWidth;
            field.style.flexGrow = 1;
            return field;
        }

        static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        static void SetPadding(VisualElement element, float padding)
        {
            element.style.paddingLeft = padding;
            element.style.paddingRight = padding;
            element.style.paddingTop = padding;
            element.style.paddingBottom = padding;
        }
    }
}
