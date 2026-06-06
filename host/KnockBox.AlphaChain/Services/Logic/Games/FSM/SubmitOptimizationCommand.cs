namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Commits a player's Engine Bay ordering during the Intermission's Optimization
    /// sub-phase. <paramref name="ModifierBayIds"/> is the desired left → right ordering,
    /// drawn from the cards the player currently holds (existing bay + cards just dealt).
    /// The order is recorded against <c>OptimizationSubmissions</c> and applied to the live
    /// bay only when the sub-phase ends, so it never leaks to opponents mid-timer.
    /// </summary>
    /// <param name="ActorUserId">The id of the player committing their ordering.</param>
    /// <param name="ModifierBayIds">The new ordering of modifier-card ids.</param>
    public record SubmitOptimizationCommand(Guid ActorUserId, IReadOnlyList<string> ModifierBayIds)
        : AlphaChainCommand(ActorUserId);
}
