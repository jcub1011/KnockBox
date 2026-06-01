namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Base for all player-issued commands processed by the Alpha Chain FSM. Every
    /// command carries the id of the player that issued it so states can validate
    /// permissions (active-player restrictions, host-only commands, etc.).
    /// </summary>
    public abstract record AlphaChainCommand(string ActorUserId);

    /// <summary>Rotates the active player to the next seat in turn order.</summary>
    public record AdvanceTurnCommand(string ActorUserId) : AlphaChainCommand(ActorUserId);
}
