using KnockBox.Core.Services.Logic.RandomGeneration;
using System.Security.Cryptography;

namespace KnockBox.Services.Logic.RandomGeneration
{
    /// <summary>
    /// Default <see cref="IRandomNumberService"/> implementation. Routes calls
    /// to <see cref="System.Random.Shared"/> for <see cref="RandomType.Fast"/>
    /// or <see cref="RandomNumberGenerator"/> for <see cref="RandomType.Secure"/>.
    /// Registered as a singleton by the platform.
    /// </summary>
    public sealed class RandomNumberService : IRandomNumberService
    {
        public int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast)
        {
            return type switch
            {
                RandomType.Secure => RandomNumberGenerator.GetInt32(exclusiveMax),
                _ => Random.Shared.Next(exclusiveMax)
            };
        }

        public int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast)
        {
            return type switch
            {
                RandomType.Secure => RandomNumberGenerator.GetInt32(inclusiveMin, exclusiveMax),
                _ => Random.Shared.Next(inclusiveMin, exclusiveMax)
            };
        }

        public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0, nameof(length));

            return type switch
            {
                RandomType.Secure => RandomNumberGenerator.GetBytes(length),
                _ => GetFastBytes(length)
            };
        }

        private static byte[] GetFastBytes(int length)
        {
            byte[] bytes = new byte[length];
            Random.Shared.NextBytes(bytes);
            return bytes;
        }
    }
}
