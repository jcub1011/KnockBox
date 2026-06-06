namespace KnockBox.CardCounter.Services.Logic.Games.FSM
{
    /// <summary>Base for all player-issued commands processed by the FSM engine.</summary>
    public abstract record CardCounterCommand(Guid PlayerId);

    /// <summary>Active player draws the top card from the current shoe.</summary>
    public record DrawCardCommand(Guid PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Active player passes their draw for this turn (costs one pass).</summary>
    public record PassTurnCommand(Guid PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Active player folds (clears) their pot (costs one pass; turn continues).</summary>
    public record FoldPotCommand(Guid PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Player commits their buy-in choice (positive or negative balance).</summary>
    public record SetBuyInCommand(Guid PlayerId, bool IsNegative) : CardCounterCommand(PlayerId);

    /// <summary>Player plays an action card from their hand by index, optionally targeting another player.</summary>
    public record PlayActionCardCommand(Guid PlayerId, int CardIndex, Guid? TargetPlayerId = null)
        : CardCounterCommand(PlayerId);

    /// <summary>Player submits their chosen card order after a Make My Luck reveal.</summary>
    public record SubmitReorderCommand(Guid PlayerId, int[] ReorderedIndices) : CardCounterCommand(PlayerId);

    /// <summary>Targeted player accepts a pending blockable action without playing Comp'd.</summary>
    public record AcceptPendingCommand(Guid PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Player discards action cards from their hand when over the hand limit.</summary>
    public record DiscardActionCardsCommand(Guid PlayerId, int[] CardIndices) : CardCounterCommand(PlayerId);

    /// <summary>
    /// Player selects which digit indices to swap during a Skim action.
    /// <paramref name="SourceDigitIndex"/> is the index in the player's own pot;
    /// <paramref name="TargetDigitIndex"/> is the index in the opponent's pot.
    /// </summary>
    public record SkimSelectCommand(Guid PlayerId, int SourceDigitIndex, int TargetDigitIndex) : CardCounterCommand(PlayerId);

    /// <summary>Player dismisses the last-drawn-card overlay.</summary>
    public record DismissDrawnCardCommand(Guid PlayerId) : CardCounterCommand(PlayerId);

    /// <summary>
    /// Active player selects the target for a Not My Money redirect after drawing an operator.
    /// </summary>
    public record NotMyMoneySelectTargetCommand(Guid PlayerId, Guid TargetPlayerId) : CardCounterCommand(PlayerId);

    /// <summary>Active player cancels a pending Not My Money redirect.</summary>
    public record NotMyMoneyCancelCommand(Guid PlayerId) : CardCounterCommand(PlayerId);
}
