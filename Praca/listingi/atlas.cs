// Fragment wygenerowany z pomocą AI (Claude Code).
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
