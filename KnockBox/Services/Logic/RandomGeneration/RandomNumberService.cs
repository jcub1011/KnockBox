using System.Security.Cryptography;

namespace KnockBox.Services.Logic.RandomGeneration
{
    public class RandomNumberService : IRandomNumberService
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
    }
}
