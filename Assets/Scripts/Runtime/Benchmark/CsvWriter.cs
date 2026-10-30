using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WFC.Runtime
{
    /// <summary>
    /// Plik wyników: wiersz nagłówka, potem jeden wiersz na uruchomienie. Wartości rozdzielone przecinkami, liczby
    /// z kropką dziesiętną (bez względu na ustawienia systemu), kodowanie UTF-8. Każdy wiersz jest od razu zapisywany
    /// na dysk, więc przerwana seria zostawia wszystkie dotychczasowe wyniki.
    /// </summary>
    public sealed class CsvWriter : IDisposable
    {
        /// <summary>
        /// 40 kolumn z rozdziału 7 handoffu i 5 dodatkowych: czas stawiania kostek, środowisko uruchomienia oraz różnica
        /// od mapy poprzedniego ziarna w tym samym wariancie (wszystkie komórki, parter, zabudowa; DiversityAnalyzer).
        /// </summary>
        public static readonly string[] Columns =
        {
            "data", "komputer", "procesor", "ram_gb", "wersja_unity", "wersja_programu", "eksperyment", "rozwiazywacz",
            "kolejnosc", "sprzecznosc", "zestaw", "x", "y", "z", "ziarno", "powtorzenie", "udane", "proby", "czas_ms",
            "czas_wybor_ms", "czas_propagacja_ms", "czas_cofanie_ms", "decyzje", "kroki_propagacji", "usuniete_mozliwosci",
            "sprzecznosci", "cofniecia", "restarty", "pamiec_szczytowa_mb", "alokacje_gc_kb", "naruszenia", "wieze",
            "donzony", "mosty", "schody", "wys_wiez_sr", "wys_murow_sr", "wys_donzonow_sr", "trawa_dostepna_proc",
            "dziedzince_zamkniete", "czas_stawiania_ms", "srodowisko", "roznica_mapy_proc", "roznica_parter_proc",
            "roznica_zabudowa_proc"
        };

        readonly StreamWriter writer;

        public CsvWriter(string path)
        {
            Path = System.IO.Path.GetFullPath(path);
            string folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            writer = new StreamWriter(Path, false, new UTF8Encoding(false));
            WriteRow(Columns);
        }

        public string Path { get; }

        public int RowsWritten { get; private set; }

        /// <summary>Wiersz z wartościami w kolejności Columns; liczby zamienia na tekst niezależnie od języka systemu.</summary>
        public void Write(IReadOnlyList<object> values)
        {
            if (values.Count != Columns.Length)
                throw new ArgumentException($"Wiersz ma {values.Count} wartości, a plik {Columns.Length} kolumn.", nameof(values));
            WriteRow(values.Select(Format));
            RowsWritten++;
        }

        public void Dispose() => writer.Dispose();

        void WriteRow(IEnumerable<string> fields)
        {
            writer.WriteLine(string.Join(",", fields.Select(Escape)));
            writer.Flush();
        }

        public static string Format(object value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case double number: return double.IsNaN(number) ? string.Empty : number.ToString("0.###", CultureInfo.InvariantCulture);
                case float number: return float.IsNaN(number) ? string.Empty : number.ToString("0.###", CultureInfo.InvariantCulture);
                case bool flag: return flag ? "1" : "0";
                case DateTime time: return time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        /// <summary>Pole z przecinkiem, cudzysłowem albo końcem wiersza trafia w cudzysłów, a cudzysłów w środku jest podwajany.</summary>
        public static string Escape(string field)
        {
            if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
    }
}
