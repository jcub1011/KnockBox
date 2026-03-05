namespace KnockBox.Services.State.Games.DiceSimulator.Data
{
    public class DiceRollAction
    {
        public DiceType DiceType { get; set; } = DiceType.D20;
        public int DiceCount { get; set; } = 1;
        public int Modifier { get; set; } = 0;
        public RollMode Mode { get; set; } = RollMode.Normal;
    }
}