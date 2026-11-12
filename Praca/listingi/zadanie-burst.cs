// Fragment wygenerowany z pomocą AI (Claude Code).
[BurstCompile]
public static unsafe class BurstPropagation
{
  [BurstCompile(CompileSynchronously = true)]
  public static void Run(ref BitsetPropagationData data) => BitsetPropagation.Run(ref data);
  // ...
}

public sealed class BurstSolver : BitsetSolver
{
  public override string Name => "Burst";
  // ...
  protected override void RunPropagation(ref BitsetPropagationData data) => BurstPropagation.Run(ref data);
}
