namespace KnockBox.CardCounter.Services.Logic.Formatting
{
    public static class NumberFormatExtensions
    {
        public static string FormatBalance(this double balance)
        {
            if (Math.Abs(balance) < 1000000.0)
            {
                return balance >= 0 ? $"+{balance:N0}" : $"{balance:N0}";
            }
            else
            {
                return balance >= 0 ? $"+{balance:E4}" : $"{balance:E4}";
            }
        }    
    }
}
