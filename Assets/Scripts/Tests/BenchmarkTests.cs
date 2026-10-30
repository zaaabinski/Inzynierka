using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WFC.Core;
using WFC.Runtime;
using Object = UnityEngine.Object;

namespace WFC.Tests
{
    /// <summary>Badania: plik CSV, warianty eksperymentu, seria uruchomień, argumenty i okno ustawień.</summary>
    public sealed class BenchmarkTests
    {
        const string HandoffColumns =
            "data,komputer,procesor,ram_gb,wersja_unity,wersja_programu,eksperyment,rozwiazywacz,kolejnosc,sprzecznosc,zestaw,x,y,z,ziarno,powtorzenie,udane,proby,czas_ms,czas_wybor_ms,czas_propagacja_ms,czas_cofanie_ms,decyzje,kroki_propagacji,usuniete_mozliwosci,sprzecznosci,cofniecia,restarty,pamiec_szczytowa_mb,alokacje_gc_kb,naruszenia,wieze,donzony,mosty,schody,wys_wiez_sr,wys_murow_sr,wys_donzonow_sr,trawa_dostepna_proc,dziedzince_zamkniete";

        string folder;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void CreateFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "wfc_testy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            foreach (Object item in created)
                if (item != null)
                    Object.DestroyImmediate(item);
            created.Clear();
            CommandLine.Arguments = null;
        }

        static TilesetDefinition Tileset(string setName)
        {
            var tileset = AssetDatabase.LoadAssetAtPath<TilesetDefinition>(WfcPaths.Tileset(setName));
            Assert.IsNotNull(tileset, $"Brak zestawu {setName}. Uruchom WFC → Importuj reguły i modele.");
            return tileset;
        }

        BenchmarkConfig SmallConfig()
        {
            var config = ScriptableObject.CreateInstance<BenchmarkConfig>();
            created.Add(config);
            config.ExperimentName = "Test";
            config.Tilesets.Add(Tileset("Prosty"));
            config.GridSizes.Clear();
            config.GridSizes.Add(new Vector3Int(6, 4, 6));
            config.FirstSeed = 1;
            config.SeedCount = 3;
            config.Repetitions = 2;
            config.WarmupRuns = 1;
            return config;
        }

        static string[] ParseLine(string line)
        {
            // Wiersze testowe nie zawierają cudzysłowów poza nazwą procesora albo komputera.
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = !quoted;
                else if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }

        [Test]
        public void Csv_45Kolumn_Pierwsze40JakWRozdziale7()
        {
            Assert.AreEqual(45, CsvWriter.Columns.Length);
            Assert.AreEqual(HandoffColumns, string.Join(",", CsvWriter.Columns.Take(40)));
            CollectionAssert.AreEqual(new[] { "czas_stawiania_ms", "srodowisko", "roznica_mapy_proc", "roznica_parter_proc", "roznica_zabudowa_proc" },
                CsvWriter.Columns.Skip(40).ToArray());
        }

        [Test]
        public void Csv_LiczbyZKropkaNiezaleznieOdJezykaSystemu_PolaZPrzecinkiemWCudzyslowie()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL");
                Assert.AreEqual("1234.568", CsvWriter.Format(1234.5678));
                Assert.AreEqual("0.3", CsvWriter.Format(0.3));
                Assert.AreEqual("1", CsvWriter.Format(true));
                Assert.AreEqual("", CsvWriter.Format(null));
                Assert.AreEqual("4000000000", CsvWriter.Format(4000000000u));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
            Assert.AreEqual("\"Intel(R), 8 rdzeni\"", CsvWriter.Escape("Intel(R), 8 rdzeni"));
            Assert.AreEqual("\"a\"\"b\"", CsvWriter.Escape("a\"b"));
            Assert.AreEqual("zwykly", CsvWriter.Escape("zwykly"));
        }

        [Test]
        public void Eksperyment_WariantyToWszystkieKombinacje()
        {
            BenchmarkConfig config = SmallConfig();
            config.Tilesets.Add(Tileset("Pelny"));
            config.GridSizes.Add(new Vector3Int(10, 8, 10));
            config.Selectors.Add(SelectorKind.Scanline);

            Assert.AreEqual(8, config.Variants().Count, "2 zestawy × 2 rozmiary × 2 heurystyki");
            Assert.AreEqual(8 * 3 * 2, config.MeasuredRuns, "warianty × ziarna × powtórzenia");
            Assert.AreEqual(8 * (3 * 2 + 1), config.TotalRuns, "z rozgrzewką 1 na wariant");
        }

        [Test]
        public void Seria_RozgrzewkaPozaPlikiem_PowtorzeniaDajaTeSameMapy()
        {
            string path = Path.Combine(folder, "seria.csv");
            var shown = new List<bool>();
            int calls = 0;
            using (var session = new BenchmarkSession(SmallConfig(), path, MachineInfo.Current()))
            {
                while (!session.IsFinished)
                {
                    session.RunNext(run =>
                    {
                        shown.Add(run.Warmup);
                        return run.Warmup ? (double?)null : 12.5;
                    });
                    calls++;
                }
                Assert.AreEqual(6, session.CompletedMeasuredRuns);
                Assert.AreEqual(0, session.Violations);
            }

            Assert.AreEqual(7, calls, "1 rozgrzewka + 3 ziarna × 2 powtórzenia");
            CollectionAssert.AreEqual(new[] { true, false, false, false, false, false, false }, shown);
            string[] lines = File.ReadAllLines(path);
            Assert.AreEqual(7, lines.Length, "nagłówek + 6 wierszy");
            Assert.AreEqual(string.Join(",", CsvWriter.Columns), lines[0]);

            List<string[]> rows = lines.Skip(1).Select(ParseLine).ToList();
            int Column(string name) => Array.IndexOf(CsvWriter.Columns, name);
            Assert.IsTrue(rows.All(row => row.Length == CsvWriter.Columns.Length));
            CollectionAssert.AreEqual(new[] { "1", "1", "2", "2", "3", "3" }, rows.Select(row => row[Column("ziarno")]).ToArray());
            CollectionAssert.AreEqual(new[] { "1", "2", "1", "2", "1", "2" }, rows.Select(row => row[Column("powtorzenie")]).ToArray());
            Assert.IsTrue(rows.All(row => row[Column("udane")] == "1" && row[Column("naruszenia")] == "0"));
            Assert.IsTrue(rows.All(row => row[Column("x")] == "6" && row[Column("y")] == "6" && row[Column("z")] == "4"), "x = E, y = N, z = piętra");
            Assert.IsTrue(rows.All(row => row[Column("czas_stawiania_ms")] == "12.5"));
            Assert.IsTrue(rows.All(row => row[Column("eksperyment")] == "Test" && row[Column("zestaw")] == "Prosty" && row[Column("srodowisko")] == "Edytor"));
            Assert.IsTrue(rows.All(row => double.Parse(row[Column("pamiec_szczytowa_mb")], CultureInfo.InvariantCulture) > 0));
            Assert.IsTrue(rows.All(row => double.Parse(row[Column("alokacje_gc_kb")], CultureInfo.InvariantCulture) > 0), "alokacje rozwiązywacza");
            for (int i = 0; i < rows.Count; i += 2)
            {
                Assert.AreEqual(rows[i][Column("decyzje")], rows[i + 1][Column("decyzje")], "to samo ziarno = ta sama mapa");
                Assert.AreEqual(rows[i][Column("usuniete_mozliwosci")], rows[i + 1][Column("usuniete_mozliwosci")]);
            }

            // Różnica od poprzedniego ziarna: pierwsze ziarno bez porównania, powtórzenia porównują się z ziarnem wcześniejszym.
            string[] differences = rows.Select(row => row[Column("roznica_mapy_proc")]).ToArray();
            Assert.AreEqual("", differences[0]);
            Assert.AreEqual("", differences[1]);
            for (int i = 2; i < rows.Count; i++)
            {
                double all = double.Parse(differences[i], CultureInfo.InvariantCulture);
                Assert.Greater(all, 0, $"wiersz {i}: ziarno {rows[i][Column("ziarno")]} inne niż poprzednie");
                Assert.LessOrEqual(all, 100);
                Assert.IsNotEmpty(rows[i][Column("roznica_zabudowa_proc")]);
            }
            Assert.AreEqual(differences[2], differences[3], "oba powtórzenia ziarna 2 porównane z ziarnem 1");
        }

        [Test]
        public void WierszPolecen_EksperymentIZmianyZArgumentow()
        {
            CommandLine.Arguments = new[] { "Zamek.exe", "-batchmode", "-nographics", "-benchmark", "-config", "e2", "-seeds", "10", "-warmup", "0", "-out", "wyniki.csv" };
            Assert.IsTrue(CommandLine.IsBenchmark);
            Assert.AreEqual("wyniki.csv", CommandLine.Get("-out"));
            Assert.IsNull(CommandLine.Get("-brak"));

            var runner = new GameObject("Runner testowy").AddComponent<BenchmarkRunner>();
            created.Add(runner.gameObject);
            var preset = AssetDatabase.LoadAssetAtPath<BenchmarkConfig>(WfcPaths.Experiment("E2"));
            Assert.IsNotNull(preset, "Brak eksperymentu E2. Uruchom WFC → Utwórz eksperymenty E1–E6.");
            runner.Experiments.Add(preset);

            BenchmarkConfig config = runner.ConfigFromCommandLine();
            created.Add(config);
            Assert.AreEqual("E2", config.ExperimentName);
            Assert.AreEqual(10, config.SeedCount);
            Assert.AreEqual(0, config.WarmupRuns);
            Assert.AreEqual(100, preset.SeedCount, "zasób eksperymentu się nie zmienia");
        }

        [Test]
        public void Naive_PamiecStrukturRosnieZRozmiaremSiatki()
        {
            CompiledRules rules = TestData.Compile("Pelny");
            long Memory(int3 size)
            {
                var solver = new NaiveSolver();
                solver.Initialize(rules, size, 1, new SolverSettings());
                solver.Run();
                return solver.PeakMemoryBytes;
            }
            long small = Memory(new int3(10, 8, 10));
            long large = Memory(new int3(20, 13, 20));
            // Listy możliwości: 800 komórek × (56 + 4 × 170) bajtów ≈ 0,56 MB.
            Assert.Greater(small, 800L * (56 + 4 * 170));
            Assert.Greater(large, 4 * small, "5200 komórek zamiast 800");
        }

        [Test]
        public void Okno_ZawieraObaTrybyIPrzyciski()
        {
            var panel = new GameObject("Panel testowy").AddComponent<GeneratorPanel>();
            created.Add(panel.gameObject);
            var root = new VisualElement();
            panel.Build(root);

            foreach (string name in new[] { "zakladka-jedna-mapa", "zakladka-badanie", "przycisk-generuj", "przycisk-krok", "przycisk-odtwarzaj", "przycisk-start", "przycisk-pauza", "przycisk-stop" })
                Assert.IsNotNull(root.Q<Button>(name), name);
            Assert.IsNotNull(root.Q<Slider>("szybkosc"));
            foreach (string name in new[] { "pokaz-mozliwosci", "biezace-pietro", "zrzut-pietra" })
                Assert.IsNotNull(root.Q<Toggle>(name), name);
            StringAssert.Contains("ostatnia decyzja", root.Q<Label>("legenda").text);
            foreach (string name in new[] { "heurystyka", "rozwiazywacz", "sprzecznosc", "zestaw", "eksperyment" })
                Assert.IsNotNull(root.Q<DropdownField>(name), name);
            Assert.AreEqual(5, root.Q<DropdownField>("heurystyka").choices.Count, "5 heurystyk");
            Assert.AreEqual(3, root.Q<DropdownField>("sprzecznosc").choices.Count, "3 reakcje na sprzeczność");
            Assert.IsNotNull(root.Q<ProgressBar>("postep"));
            Assert.AreEqual(DisplayStyle.Flex, root.Q("sekcja-jedna-mapa").style.display.value);
            Assert.AreEqual(DisplayStyle.None, root.Q("sekcja-badanie").style.display.value);
        }
    }
}
