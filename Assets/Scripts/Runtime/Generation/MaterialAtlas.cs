using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WFC.Runtime
{
    /// <summary>
    /// Atlas materiałów zestawu. Modele nie mają tekstur, a materiały różnią się tylko kolorem, więc atlas to paleta:
    /// mała tekstura z jednym tekselem na materiał. Wszystkie części scalonej mapy rysuje jeden materiał, a kolor wybiera
    /// współrzędna UV zapisana w wierzchołku (CombinedMap). Materiał z inną gładkością, metalicznością, emisją albo teksturą
    /// wyglądałby po scaleniu inaczej, dlatego Differences wypisuje takie przypadki.
    /// </summary>
    public sealed class MaterialAtlas : IDisposable
    {
        const string BaseColor = "_BaseColor";
        const string BaseMap = "_BaseMap";
        const string Smoothness = "_Smoothness";
        const string Metallic = "_Metallic";
        const string EmissionKeyword = "_EMISSION";

        readonly Dictionary<Material, Vector2> uvs = new Dictionary<Material, Vector2>();

        MaterialAtlas(Texture2D texture, Material material)
        {
            Texture = texture;
            Material = material;
        }

        /// <summary>Paleta: format sRGB, bez mipmap, filtr punktowy, więc kolor nie miesza się z sąsiednim tekselem.</summary>
        public Texture2D Texture { get; }

        /// <summary>Jedyny materiał scalonej mapy: kopia pierwszego materiału zestawu z paletą jako teksturą bazową.</summary>
        public Material Material { get; }

        /// <summary>Materiały w atlasie, według nazwy.</summary>
        public IReadOnlyCollection<Material> Materials => uvs.Keys;

        /// <summary>Różnice materiałów, których paleta nie odda; pusta lista oznacza wygląd taki sam jak przed scaleniem.</summary>
        public List<string> Differences { get; } = new List<string>();

        public static MaterialAtlas Build(IEnumerable<Material> materials, HideFlags hideFlags = HideFlags.None)
        {
            List<Material> sorted = materials.Distinct().OrderBy(material => material.name, StringComparer.Ordinal).ToList();
            if (sorted.Count == 0)
                throw new ArgumentException("Atlas potrzebuje co najmniej jednego materiału.", nameof(materials));

            int side = Mathf.CeilToInt(Mathf.Sqrt(sorted.Count));
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false, false)
            {
                name = "T_AtlasMaterialow",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = hideFlags
            };
            var pixels = new Color32[side * side];
            Array.Fill(pixels, new Color32(255, 255, 255, 255));

            var combined = new Material(sorted[0])
            {
                name = "M_AtlasMaterialow",
                enableInstancing = true,
                hideFlags = hideFlags
            };
            combined.SetTexture(BaseMap, texture);
            combined.SetColor(BaseColor, Color.white);

            var atlas = new MaterialAtlas(texture, combined);
            for (int i = 0; i < sorted.Count; i++)
            {
                Material material = sorted[i];
                int x = i % side;
                int y = i / side;
                pixels[y * side + x] = material.GetColor(BaseColor);
                atlas.uvs[material] = new Vector2((x + 0.5f) / side, (y + 0.5f) / side);
                atlas.Check(material, sorted[0]);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return atlas;
        }

        /// <summary>Współrzędna UV środka teksela materiału.</summary>
        public Vector2 UvOf(Material material) => uvs[material];

        /// <summary>Kolor palety w punkcie UV.</summary>
        public Color ColorAt(Vector2 uv) => Texture.GetPixel(Mathf.FloorToInt(uv.x * Texture.width), Mathf.FloorToInt(uv.y * Texture.height));

        public void Dispose()
        {
            DestroyObject(Material);
            DestroyObject(Texture);
        }

        void Check(Material material, Material reference)
        {
            if (material.shader != reference.shader)
            {
                Differences.Add($"{material.name}: inny shader ({material.shader.name}) niż {reference.name}.");
                return;
            }
            if (material.GetTexture(BaseMap) != null)
                Differences.Add($"{material.name}: ma własną teksturę, a paleta bierze tylko kolor.");
            if (!Mathf.Approximately(material.GetFloat(Smoothness), reference.GetFloat(Smoothness)))
                Differences.Add($"{material.name}: gładkość {material.GetFloat(Smoothness)} zamiast {reference.GetFloat(Smoothness)}.");
            if (!Mathf.Approximately(material.GetFloat(Metallic), reference.GetFloat(Metallic)))
                Differences.Add($"{material.name}: metaliczność {material.GetFloat(Metallic)} zamiast {reference.GetFloat(Metallic)}.");
            if (material.IsKeywordEnabled(EmissionKeyword) != reference.IsKeywordEnabled(EmissionKeyword))
                Differences.Add($"{material.name}: inna emisja niż {reference.name}.");
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
