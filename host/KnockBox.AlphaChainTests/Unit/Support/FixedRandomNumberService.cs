using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.AlphaChain.Tests.Unit.Support
{
    /// <summary>
    /// Deterministic <see cref="IRandomNumberService"/> that always returns a fixed index
    /// (clamped into range). Tests that care about the exact banned letter override
    /// <c>AlphaChainGameState.BannedLetter</c> directly; this just makes the Intermission card
    /// deals and the Sniper Ban timeout draw reproducible. (Era 1 is ban-free, so no draw
    /// happens at setup.)
    /// </summary>
    internal sealed class FixedRandomNumberService(int value = 0) : IRandomNumberService
    {
        public int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast)
            => exclusiveMax <= 0 ? 0 : value % exclusiveMax;

        public int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast)
        {
            int range = exclusiveMax - inclusiveMin;
            return range <= 0 ? inclusiveMin : inclusiveMin + (value % range);
        }

        public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast)
            => new byte[length];
    }
}
