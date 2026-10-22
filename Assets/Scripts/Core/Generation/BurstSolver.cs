namespace WFC.Core
{
    /// <summary>
    /// BitsetSolver z propagacją skompilowaną przez Burst (BurstPropagation): te same tablice i ten sam kod, inny
    /// kompilator. Wybór komórki, losowanie i entropia zostają w C#, bo propagacja to około 99% czasu generacji.
    /// </summary>
    public sealed class BurstSolver : BitsetSolver
    {
        public override string Name => "Burst";

        /// <summary>Czy propagacja idzie przez kod skompilowany przez Burst; fałsz przy wyłączonym Burst albo poza Unity.</summary>
        public static bool IsBurstCompiled
        {
            get
            {
                int managed = 1;
                BurstPropagation.Probe(ref managed);
                return managed == 0;
            }
        }

        protected override void RunPropagation(ref BitsetPropagationData data) => BurstPropagation.Run(ref data);
    }
}
