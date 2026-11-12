// Fragment wygenerowany z pomocą AI (Claude Code).
public const int BytesPerVertex = 24;

[StructLayout(LayoutKind.Sequential)]
struct PackedVertex
{
  public float3 Position;
  public short NormalX;
  public short NormalY;
  public short NormalZ;
  public short NormalW;
  public ushort U;
  public ushort V;
}

static readonly VertexAttributeDescriptor[] VertexLayout =
{
  new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
  new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm16, 4),
  new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm16, 2)
};
