namespace KnockBox.Core.Extensions.Math
{
    public static class MathExtensions
    {
        /// <summary>
        /// Calculates the integer power of the value.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="exponent"></param>
        /// <returns></returns>
        public static int Pow(this int value, uint exponent)
        {
            int sum = 1;
            checked
            {
                while (exponent != 0)
                {
                    if ((exponent & 1) == 1)
                        sum *= value;
                    value *= value;
                    exponent >>= 1;
                }
            }
            return sum;
        }

        /// <summary>
        /// Calculates the integer power of the value.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="exponent"></param>
        /// <returns></returns>
        public static long Pow(this long value, uint exponent)
        {
            long sum = 1;
            checked
            {
                while (exponent != 0)
                {
                    if ((exponent & 1) == 1)
                        sum *= value;
                    value *= value;
                    exponent >>= 1;
                }
            }
            return sum;
        }
    }
}
