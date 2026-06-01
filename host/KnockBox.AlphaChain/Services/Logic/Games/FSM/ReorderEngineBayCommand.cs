namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Re-orders a player's Engine Bay. <paramref name="CardIds"/> is the desired left → right
    /// ordering, expressed as a permutation of the ids currently in the bay. Only valid
    /// between rounds in the final design; M3 also allows it during the round to ease testing
    /// (M4 locks it to Intermission).
    /// </summary>
    /// <param name="ActorUserId">The id of the player re-ordering their bay.</param>
    /// <param name="CardIds">The new ordering of modifier-card ids.</param>
    public record ReorderEngineBayCommand(string ActorUserId, IReadOnlyList<string> CardIds)
        : AlphaChainCommand(ActorUserId);
}
