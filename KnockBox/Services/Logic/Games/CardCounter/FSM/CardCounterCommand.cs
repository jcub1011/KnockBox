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

    /// <summary>Player discards action cards from their hand when over the hand limit.</summary>
    public record DiscardActionCardsCommand(string PlayerId, int[] CardIndices) : CardCounterCommand(PlayerId);

    /// <summary>
    /// Player selects which digit indices to swap during a Skim action.
    /// <paramref name="SourceDigitIndex"/> is the index in the player's own pot;
    /// <paramref name="TargetDigitIndex"/> is the index in the opponent's pot.
    /// </summary>
    public record SkimSelectCommand(string PlayerId, int SourceDigitIndex, int TargetDigitIndex) : CardCounterCommand(PlayerId);

    /// <summary>Player dismisses the last-drawn-card overlay.</summary>
    public record DismissDrawnCardCommand(string PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>
    /// Active player selects the target for a Not My Money redirect after drawing an operator.
    /// </summary>
    public record NotMyMoneySelectTargetCommand(string PlayerId, string TargetPlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Active player cancels a pending Not My Money redirect.</summary>
    public record NotMyMoneyCancelCommand(string PlayerId) : CardCounterCommand(PlayerId);
}
