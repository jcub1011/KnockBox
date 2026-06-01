namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Host-only debug command that deals a random starter set of cards to every player so
    /// the scoring pipeline and card UI can be exercised in isolation, before Intermission
    /// card-draft lands in M4. <b>Removed (or gated behind a debug flag) before release.</b>
    /// </summary>
    /// <param name="ActorUserId">The id of the issuing player (must be the host).</param>
    /// <param name="ModifierCount">How many random modifiers to deal to each player.</param>
    /// <param name="ActionCount">How many random actions to deal to each player.</param>
    public record GrantCardsDebugCommand(string ActorUserId, int ModifierCount = 2, int ActionCount = 1)
        : AlphaChainCommand(ActorUserId);
}
