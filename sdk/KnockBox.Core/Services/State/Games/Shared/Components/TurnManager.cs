namespace KnockBox.Core.Services.State.Games.Shared.Components;

/// <summary>
/// Helper for games that take strict turns. Stores an ordered list of player
/// ids and a rolling index into that list. Not thread-safe on its own — invoke
/// its methods from inside your state's <c>Execute</c> block so access is
/// serialized with the rest of the room's state.
/// </summary>
public class TurnManager
{
    /// <summary>Player ids in turn order. Usually set once at game start.</summary>
    public List<string> TurnOrder { get; } = [];

    /// <summary>Index into <see cref="TurnOrder"/> pointing at the active player.</summary>
    public int CurrentPlayerIndex { get; private set; }

    /// <summary>
    /// The player id whose turn it currently is, or <c>null</c> if the turn
    /// order has not been set yet.
    /// </summary>
    public string? CurrentPlayer => TurnOrder.Count > 0 ? TurnOrder[CurrentPlayerIndex] : null;

    /// <summary>
    /// Replaces the turn order with <paramref name="playerIds"/> and resets the
    /// current-player index to zero.
    /// </summary>
    public void SetTurnOrder(IEnumerable<string> playerIds)
    {
        TurnOrder.Clear();
        TurnOrder.AddRange(playerIds);
        CurrentPlayerIndex = 0;
    }

    /// <summary>
    /// Advances to the next player in the turn order.
    /// </summary>
    /// <returns>True if the turn order has looped back to the beginning; otherwise, false.</returns>
    public bool NextTurn()
    {
        if (TurnOrder.Count == 0) return false;
        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % TurnOrder.Count;
        return CurrentPlayerIndex == 0;
    }

    /// <summary>
    /// Jumps to a specific position in the turn order. No-op if
    /// <paramref name="index"/> is outside the valid range.
    /// </summary>
    /// <param name="index">Zero-based index into <see cref="TurnOrder"/>.</param>
    public void SetCurrentPlayerIndex(int index)
    {
        if (index >= 0 && index < TurnOrder.Count)
        {
            CurrentPlayerIndex = index;
        }
    }
}
