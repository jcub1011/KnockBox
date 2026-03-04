namespace KnockBox.Services.Logic.Games.CardCounter.FSM
{
    /// <summary>Base for all player-issued commands processed by the FSM engine.</summary>
    public abstract record CardCounterCommand(string PlayerId);

    /// <summary>Active player draws the top card from the current shoe.</summary>
    public record DrawCardCommand(string PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Active player passes their draw for this turn (costs one pass).</summary>
    public record PassTurnCommand(string PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Active player folds (clears) their pot (costs one pass; turn continues).</summary>
    public record FoldPotCommand(string PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Player commits their buy-in choice (positive or negative balance).</summary>
    public record SetBuyInCommand(string PlayerId, bool IsNegative) : CardCounterCommand(PlayerId);

    /// <summary>Player plays an action card from their hand by index, optionally targeting another player.</summary>
    public record PlayActionCardCommand(string PlayerId, int CardIndex, string? TargetPlayerId = null)
        : CardCounterCommand(PlayerId);

    /// <summary>Player submits their chosen card order after a Make My Luck reveal.</summary>
    public record SubmitReorderCommand(string PlayerId, int[] ReorderedIndices) : CardCounterCommand(PlayerId);

    /// <summary>Targeted player accepts a pending blockable action without playing Comp'd.</summary>
    public record AcceptPendingCommand(string PlayerId) : CardCounterCommand(PlayerId);
}
