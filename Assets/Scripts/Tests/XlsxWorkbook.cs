using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace WFC.Tests
{
    /// <summary>
    /// Minimalny czytnik arkusza .xlsx (archiwum ZIP z plikami XML) do porównania reguł z zakładkami arkusza.
    /// Zwraca wartości komórek jako tekst: liczby w zapisie z pliku, np. „0.3”.
    /// </summary>
    sealed class XlsxWorkbook
    {
        static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

        readonly Dictionary<string, List<string[]>> sheets = new Dictionary<string, List<string[]>>();

        public IEnumerable<string> SheetNames => sheets.Keys;

        public IReadOnlyList<string[]> Rows(string sheetName)
        {
            if (!sheets.TryGetValue(sheetName, out List<string[]> rows))
                throw new ArgumentException($"Arkusz nie ma zakładki „{sheetName}”. Zakładki: {string.Join(", ", sheets.Keys)}.");
            return rows;
        }

        public static XlsxWorkbook Open(string path)
        {
            var workbook = new XlsxWorkbook();
            using (FileStream stream = File.OpenRead(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var sharedStrings = new List<string>();
                ZipArchiveEntry stringsEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (stringsEntry != null)
                    foreach (XElement item in Load(stringsEntry).Root.Elements(Main + "si"))
                        sharedStrings.Add(string.Concat(item.Descendants(Main + "t").Select(text => text.Value)));

                var targets = Load(zip.GetEntry("xl/_rels/workbook.xml.rels")).Root
                    .Elements(PackageRelationships + "Relationship")
                    .ToDictionary(relation => (string)relation.Attribute("Id"), relation => (string)relation.Attribute("Target"));

                foreach (XElement sheet in Load(zip.GetEntry("xl/workbook.xml")).Root.Element(Main + "sheets").Elements(Main + "sheet"))
                {
                    string target = targets[(string)sheet.Attribute(Relationships + "id")].TrimStart('/');
                    if (!target.StartsWith("xl/", StringComparison.Ordinal))
                        target = "xl/" + target;
                    workbook.sheets[(string)sheet.Attribute("name")] = ReadSheet(Load(zip.GetEntry(target)), sharedStrings);
                }
            }
            return workbook;
        }

        static List<string[]> ReadSheet(XDocument document, List<string> sharedStrings)
        {
            var rows = new List<string[]>();
            foreach (XElement row in document.Descendants(Main + "row"))
            {
                var cells = new SortedDictionary<int, string>();
                foreach (XElement cell in row.Elements(Main + "c"))
                {
                    string type = (string)cell.Attribute("t");
                    string value = (string)cell.Element(Main + "v") ?? string.Empty;
                    if (type == "s")
                        value = sharedStrings[int.Parse(value)];
                    else if (type == "inlineStr")
                        value = string.Concat(cell.Descendants(Main + "t").Select(text => text.Value));
                    cells[ColumnIndex((string)cell.Attribute("r"))] = value;
                }
                var values = new string[cells.Count == 0 ? 0 : cells.Keys.Max() + 1];
                for (int i = 0; i < values.Length; i++)
                    values[i] = cells.TryGetValue(i, out string value) ? value : string.Empty;
                rows.Add(values);
            }
            return rows;
        }

        static int ColumnIndex(string reference)
        {
            int index = 0;
            foreach (char character in reference)
            {
                if (character < 'A' || character > 'Z')
                    break;
                index = index * 26 + (character - 'A' + 1);
            }
            return index - 1;
        }

        static XDocument Load(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
                return XDocument.Load(stream);
        }
    }
}
