namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// A player's pending Engine Bay ordering during the Optimization sub-phase. The
    /// submission is recorded but <b>not</b> applied to the live <c>EngineBay</c> until the
    /// sub-phase ends, so a player's in-progress reorder never leaks to opponents (the
    /// GDD's "fog-of-war" guarantee). <see cref="Submitted"/> starts false (the seeded
    /// current order) and flips true when the player commits via <c>SubmitOptimizationCommand</c>.
    /// </summary>
    /// <param name="UserId">The owning player's <c>User.Id</c>.</param>
    /// <param name="ModifierBayIds">The desired left → right ordering of modifier-card ids.</param>
    /// <param name="Submitted">Whether the player has committed this ordering.</param>
    public record OptimizationSubmission(
        Guid UserId,
        IReadOnlyList<string> ModifierBayIds,
        bool Submitted);
}
