namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public enum CombatPhase
    {
        WaitingForRolls = 0,
        Active = 1,
    }

    public sealed class CombatState
    {
        public CombatPhase Phase { get; set; }
        public int RoundNumber { get; set; } = 1;
        public int CurrentTurnIndex { get; set; }
        public List<CombatantEntry> TurnOrder { get; set; } = [];
    }
}
