namespace KnockBox.Core.Extensions.RandomGeneration
{
    public static class RandomExtensions
    {
        public static byte[] GetBytes(this Random random, int length)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0, nameof(length));
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }
    }
}
