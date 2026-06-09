namespace KnockBox.DiceSimulator.Contracts
{
    /// <summary>
    /// The parameters of a roll request. Doubles as the client's two-way-bound roll
    /// form model and the JSON payload of the <see cref="DiceSimulatorCommands.RollDice"/>
    /// hub command.
    /// </summary>
    public class DiceRollAction
    {
        public DiceType DiceType { get; set; } = DiceType.D20;
        public int DiceCount { get; set; } = 1;
        public int Modifier { get; set; } = 0;
        public RollMode Mode { get; set; } = RollMode.Normal;
    }
}
