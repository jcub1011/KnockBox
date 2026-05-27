using System.Collections.Immutable;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public enum CombatPhase
    {
        WaitingForRolls = 0,
        Active = 1,
    }

    public sealed record CombatState
    {
        public CombatPhase Phase { get; init; }
        public int RoundNumber { get; init; } = 1;
        public int CurrentTurnIndex { get; init; }
        public ImmutableArray<CombatantEntry> TurnOrder { get; init; } = ImmutableArray<CombatantEntry>.Empty;
    }
}
