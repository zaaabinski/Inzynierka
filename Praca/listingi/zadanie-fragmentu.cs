// Fragment wygenerowany z pomocą AI (Claude Code).
[BurstCompile(CompileSynchronously = true)]
struct WriteChunkJob : IJob
{
  public Mesh.MeshData Output;
  // ...
  public void Execute()
  {
    NativeArray<PackedVertex> vertices = Output.GetVertexData<PackedVertex>();
    NativeArray<ushort> narrow = WideIndices ? default : Output.GetIndexData<ushort>();
    NativeArray<uint> wide = WideIndices ? Output.GetIndexData<uint>() : default;

    for (int i = FirstInstance; i < FirstInstance + InstanceCount; i++)
    {
      PartInstance instance = Instances[i];
      PartGeometry geometry = Geometry[instance.Geometry];
      PartPose pose = Poses[instance.Pose];
      float3 offset = pose.Offset + instance.Cell;
      float3 min = BoundsMin[FirstBounds + instance.Submesh];
      float3 max = BoundsMax[FirstBounds + instance.Submesh];

      for (int v = 0; v < geometry.VertexCount; v++)
      {
        float3 position = math.mul(pose.Rotation, Positions[geometry.VertexStart + v]) + offset;
        float3 normal = math.normalizesafe(math.mul(pose.NormalMatrix, Normals[geometry.VertexStart + v]));
        min = math.min(min, position);
        max = math.max(max, position);
        vertices[instance.VertexStart + v] = new PackedVertex
        {
          Position = position,
          NormalX = (short)math.round(normal.x * short.MaxValue),
          NormalY = (short)math.round(normal.y * short.MaxValue),
          NormalZ = (short)math.round(normal.z * short.MaxValue),
          U = instance.U,
          V = instance.V
        };
      }
      // ...
    }
  }
}
