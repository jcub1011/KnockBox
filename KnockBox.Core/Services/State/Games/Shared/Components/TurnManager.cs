namespace KnockBox.Core.Services.State.Games.Shared.Components;

public class TurnManager
{
    public List<string> TurnOrder { get; } = [];
    public int CurrentPlayerIndex { get; private set; }

    public string? CurrentPlayer => TurnOrder.Count > 0 ? TurnOrder[CurrentPlayerIndex] : null;

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
    /// Sets the current player index.
    /// </summary>
    /// <param name="index"></param>
    public void SetCurrentPlayerIndex(int index)
    {
        if (index >= 0 && index < TurnOrder.Count)
        {
            CurrentPlayerIndex = index;
        }
    }
}
