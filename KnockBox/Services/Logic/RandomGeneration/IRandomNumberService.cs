namespace KnockBox.Services.Logic.RandomGeneration
{
    public enum RandomType
    {
        /// <summary>
        /// Cryptographically secure random number generation.
        /// </summary>
        Secure,
        /// <summary>
        /// Cryptographically insecure random number generation.
        /// </summary>
        Fast
    }

    public interface IRandomNumberService
    {
        /// <summary>
        /// Generates a random number within the range [0, exclusiveMax).
        /// </summary>
        /// <param name="exclusiveMax"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast);

        /// <summary>
        /// Generates a random number within the range [inclusiveMin, exclusiveMax).
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast);
    }
}
