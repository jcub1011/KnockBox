using System.Security.Cryptography;

namespace KnockBox.Services.State.Games.DiceSimulator.Data
{
    public class CustomizableDice()
    {
        public readonly int Sides;
        public int CurrentValue { get; set; }

        /// <summary>
        /// Rolls a new value.
        /// </summary>
        public void Roll()
        {
            CurrentValue = RandomNumberGenerator.GetInt32(Sides) + 1;
        }
    }
}
