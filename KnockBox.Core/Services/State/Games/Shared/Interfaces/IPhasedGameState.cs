namespace KnockBox.Core.Services.State.Games.Shared.Interfaces;

public interface IPhasedGameState<TPhase> where TPhase : struct, Enum
{
    TPhase Phase { get; }
    void SetPhase(TPhase phase);
}
