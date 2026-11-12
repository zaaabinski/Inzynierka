// Fragment wygenerowany z pomocą AI (Claude Code).
public struct DeterministicRandom
{
  Random random;

  public DeterministicRandom(uint seed)
  {
    random = Random.CreateFromIndex(seed);
  }

  public static DeterministicRandom ForAttempt(uint seed, int attempt) =>
    new DeterministicRandom(math.hash(new uint2(seed, (uint)attempt)));

  public double NextDouble() => random.NextDouble();

  public int NextInt(int max) => random.NextInt(max);

  public uint State => random.state;
}
