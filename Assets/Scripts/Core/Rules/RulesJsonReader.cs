using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WFC.Core
{
    /// <summary>
    /// Czyta plik reguł wfc_rules_zamek.json do RulesFile. Brak wymaganego pola kończy się FormatException
    /// z nazwą pola i miejscem w pliku.
    /// </summary>
    public static class RulesJsonReader
    {
        public static RulesFile Parse(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonReaderException exception)
            {
                throw new FormatException($"Plik reguł nie jest poprawnym JSON: {exception.Message}", exception);
            }

            var file = new RulesFile
            {
                Version = (string)root["wersja"],
                Description = (string)root["opis"]
            };

            JObject grid = RequiredObject(root, "siatka", "plik");
            file.Grid = new GridSettings
            {
                CellsEast = Required(grid, "x", "siatka").Value<int>(),
                CellsNorth = Required(grid, "y", "siatka").Value<int>(),
                Floors = Required(grid, "z", "siatka").Value<int>(),
                CellSize = Required(grid, "rozmiarKostkiM", "siatka").Value<double>()
            };

            JObject edge = RequiredObject(root, "krawedzMapy", "plik");
            file.MapEdge = new MapEdgeRules
            {
                SideAtGround = RequiredString(edge, "bokNaParterze", "krawedzMapy"),
                SideAbove = RequiredString(edge, "bokWyzej", "krawedzMapy"),
                GridBottom = RequiredString(edge, "dnoSiatki", "krawedzMapy"),
                GridTopAllowed = RequiredArray(edge, "goraSiatkiDozwolone", "krawedzMapy").ToObject<List<string>>()
            };

            foreach (JObject exception in RequiredArray(root, "wyjatkiPionowe", "plik"))
                file.VerticalExceptions.Add(new VerticalException(
                    RequiredString(exception, "podSpodem", "wyjatkiPionowe"),
                    RequiredString(exception, "nadSpodem", "wyjatkiPionowe")));

            foreach (JObject tile in RequiredArray(root, "kafelki", "plik"))
                file.Tiles.Add(ReadTile(tile));

            foreach (JObject label in RequiredArray(root, "etykietyBoczne", "plik"))
            {
                string name = RequiredString(label, "nazwa", "etykietyBoczne");
                file.SideLabels.Add(new SideLabelInfo
                {
                    Name = name,
                    Kind = ParseKind(RequiredString(label, "rodzaj", $"etykieta {name}"), name),
                    Description = (string)label["opis"]
                });
            }

            foreach (JObject label in RequiredArray(root, "etykietyPionowe", "plik"))
            {
                string name = RequiredString(label, "nazwa", "etykietyPionowe");
                file.VerticalLabels.Add(new VerticalLabelInfo
                {
                    Name = name,
                    SameRotation = Required(label, "tenSamObrot", $"etykieta {name}").Value<bool>(),
                    Description = (string)label["opis"]
                });
            }

            foreach (JObject pair in RequiredArray(root, "paryZgodne", "plik"))
                file.CompatiblePairs.Add(ReadPair(pair, "paryZgodne"));

            if (root["paryDodatkowe"] is JArray extraPairs)
                foreach (JObject pair in extraPairs)
                    file.ExtraPairs.Add(ReadPair(pair, "paryDodatkowe"));

            foreach (JProperty set in RequiredObject(root, "zestawy", "plik").Properties())
            {
                var list = new TileList { Name = set.Name };
                foreach (JToken tileName in (JArray)set.Value)
                    list.TileNames.Add((string)tileName);
                file.Sets.Add(list);
            }

            JObject control = RequiredObject(root, "kontrola", "plik");
            file.Control = new ControlSection
            {
                Tiles = Required(control, "kafelki", "kontrola").Value<int>(),
                States = Required(control, "stany", "kontrola").Value<int>(),
                SideLabels = Required(control, "etykietyBoczne", "kontrola").Value<int>(),
                VerticalLabels = Required(control, "etykietyPionowe", "kontrola").Value<int>(),
                CompatiblePairs = Required(control, "paryZgodne", "kontrola").Value<int>()
            };
            if (control["zestawy"] is JObject controlSets)
                foreach (JProperty set in controlSets.Properties())
                {
                    var counts = (JObject)set.Value;
                    file.Control.Sets[set.Name] = new ControlCounts(
                        Required(counts, "kafelki", $"kontrola.zestawy.{set.Name}").Value<int>(),
                        Required(counts, "stany", $"kontrola.zestawy.{set.Name}").Value<int>());
                }

            return file;
        }

        static TileData ReadTile(JObject json)
        {
            string name = RequiredString(json, "nazwa", "kafelki");
            string where = $"kafelek {name}";
            var tile = new TileData
            {
                Name = name,
                Model = (string)json["model"] ?? string.Empty,
                ModelAvailable = (bool?)json["modelDostepny"] ?? true,
                Group = (string)json["grupa"],
                Weight = Required(json, "waga", where).Value<double>(),
                Symmetric = (bool?)json["symetryczny"] ?? false,
                Rotations = Required(json, "obroty", where).Value<int>(),
                RandomModelRotation = (bool?)json["losowyObrotModelu"] ?? false,
                Description = (string)json["opis"]
            };
            foreach (Direction direction in DirectionExtensions.All)
                tile.SetLabel(direction, RequiredString(json, direction.RulesKey(), where));
            return tile;
        }

        static LabelPair ReadPair(JObject json, string where) =>
            new LabelPair(RequiredString(json, "a", where), RequiredString(json, "b", where));

        static SideLabelKind ParseKind(string kind, string label)
        {
            switch (kind)
            {
                case "do_siebie": return SideLabelKind.Self;
                case "para_odbita": return SideLabelKind.MirroredPair;
                case "z_listy": return SideLabelKind.FromList;
                default: throw new FormatException($"Etykieta „{label}” ma nieznany rodzaj „{kind}” (dozwolone: do_siebie, para_odbita, z_listy).");
            }
        }

        static JToken Required(JObject json, string key, string where)
        {
            JToken token = json[key];
            if (token == null || token.Type == JTokenType.Null)
                throw new FormatException($"Brak pola „{key}” ({where}).");
            return token;
        }

        static string RequiredString(JObject json, string key, string where) => (string)Required(json, key, where);

        static JObject RequiredObject(JObject json, string key, string where) =>
            Required(json, key, where) as JObject ?? throw new FormatException($"Pole „{key}” ({where}) powinno być obiektem.");

        static JArray RequiredArray(JObject json, string key, string where) =>
            Required(json, key, where) as JArray ?? throw new FormatException($"Pole „{key}” ({where}) powinno być listą.");
    }
}
