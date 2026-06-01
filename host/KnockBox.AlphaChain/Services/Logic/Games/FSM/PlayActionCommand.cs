namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Plays an action card from the actor's hand. Pivot/Amnesty queue an effect for the
    /// actor's next submission (no target); Time Thief targets an opponent via
    /// <paramref name="TargetUserId"/>.
    /// </summary>
    /// <param name="ActorUserId">The id of the player playing the card.</param>
    /// <param name="CardId">The stable id of the action card being played.</param>
    /// <param name="TargetUserId">The opponent targeted (Time Thief), or null for self-targeted actions.</param>
    public record PlayActionCommand(string ActorUserId, string CardId, string? TargetUserId)
        : AlphaChainCommand(ActorUserId);
}
