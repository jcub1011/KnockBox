namespace KnockBox.Services.State.Games.DiceSimulator.Data
{
    public class PlayerStats
    {
        public string PlayerName { get; set; } = string.Empty;
        public int TotalRolls { get; set; }
        public int TotalDiceRolled { get; set; }
        public int NatTwentyCount { get; set; }
        public int NatOneCount { get; set; }
        public int HighestResult { get; set; }
        public string? HighestResultExpression { get; set; }
        public int CumulativeTotal { get; set; }
        public Dictionary<DiceType, int> RollCountByDie { get; set; } = new();
    }
}