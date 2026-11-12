// Fragment wygenerowany z pomocą AI (Claude Code).
Mesh.MeshDataArray output = Mesh.AllocateWritableMeshData(written);
// ...
outputMeshes.Clear();
int slot = 0;
foreach (Chunk chunk in dirty)
{
  if (chunk.Instances == 0)
    continue;
  bool wide = chunk.Vertices > ushort.MaxValue;
  Mesh.MeshData data = output[slot];
  data.SetVertexBufferParams(chunk.Vertices, VertexLayout);
  data.SetIndexBufferParams(chunk.Indices, wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
  handles[slot] = new WriteChunkJob
  {
    Output = data,
    WideIndices = wide,
    FirstInstance = chunk.FirstInstance,
    InstanceCount = chunk.Instances,
    FirstBounds = chunk.FirstBounds,
    Instances = instances,
    Geometry = geometry,
    Poses = poses,
    Positions = positions,
    Normals = normals,
    Indices = indices,
    BoundsMin = boundsMin,
    BoundsMax = boundsMax
  }.Schedule();
  outputMeshes.Add(chunk.Mesh);
  slot++;
}
JobHandle.CombineDependencies(handles).Complete();

slot = 0;
foreach (Chunk chunk in dirty)
  if (chunk.Instances > 0)
    SetSubmeshes(chunk, output[slot++], boundsMin, boundsMax);
Mesh.ApplyAndDisposeWritableMeshData(output, outputMeshes, ApplyFlags);
