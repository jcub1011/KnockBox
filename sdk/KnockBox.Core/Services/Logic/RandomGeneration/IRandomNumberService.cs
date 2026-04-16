namespace KnockBox.Core.Services.Logic.RandomGeneration
{
    /// <summary>
    /// Selects the RNG backing used by <see cref="IRandomNumberService"/>.
    /// </summary>
    public enum RandomType
    {
        /// <summary>
        /// Cryptographically secure random number generation (uses
        /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/>).
        /// Slower; use for lobby codes, session tokens, or anything a player
        /// shouldn't be able to predict.
        /// </summary>
        Secure,
        /// <summary>
        /// Fast, non-cryptographic RNG (uses <see cref="System.Random.Shared"/>).
        /// Use for gameplay randomness where predictability is acceptable —
        /// card shuffles, dice rolls, trivia shuffles.
        /// </summary>
        Fast
    }

    /// <summary>
    /// Abstraction over the .NET RNG primitives. Injected into engines and
    /// services so tests can mock randomness and so callers can choose between
    /// fast non-cryptographic generation and cryptographically secure
    /// generation without new-ing up a <see cref="System.Random"/> manually.
    /// </summary>
    public interface IRandomNumberService
    {
        /// <summary>
        /// Generates a random integer in the range <c>[0, exclusiveMax)</c>.
        /// </summary>
        /// <param name="exclusiveMax">Exclusive upper bound; must be positive.</param>
        /// <param name="type">Which backing RNG to use.</param>
        int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast);

        /// <summary>
        /// Generates a random integer in the range <c>[inclusiveMin, exclusiveMax)</c>.
        /// </summary>
        /// <param name="inclusiveMin">Inclusive lower bound.</param>
        /// <param name="exclusiveMax">Exclusive upper bound; must be &gt; <paramref name="inclusiveMin"/>.</param>
        /// <param name="type">Which backing RNG to use.</param>
        int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast);

        /// <summary>
        /// Fills a new byte array of the given length with random bytes.
        /// </summary>
        /// <param name="length">Number of bytes to generate; must be positive.</param>
        /// <param name="type">Which backing RNG to use.</param>
        byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast);
    }
}
