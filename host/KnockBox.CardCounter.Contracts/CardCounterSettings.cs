namespace KnockBox.CardCounter.Contracts;

/// <summary>
/// Host-configurable match rules. Shared between the server (authoritative
/// <c>state.Settings</c>) and the browser (the House Rules drawer renders + edits a
/// copy, then sends it as the <c>update-settings</c> command payload, which the server
/// deserializes straight back into this type). Pure record, no logic.
/// </summary>
public sealed record CardCounterSettings
{
    public int DeckSize { get; init; } = 52;
    public float NumberToOperatorRatio { get; init; } = 4.0f;
    public float AddSubToMulDivRatio { get; init; } = 4.0f;
    public int ActionsDealtPerRound { get; init; } = 3;
    public int ActionHandLimit { get; init; } = 6;
    public int TotalPassesPerPlayer { get; init; } = 3;
    public int MinShoeSize { get; init; } = 12;
    public int MaxShoeSize { get; init; } = 20;
    public int PlayerTurnTimeoutMs { get; init; } = 15000;
    public int BuyInTimeoutMs { get; init; } = 20000;
    public int RoundEndTimeoutMs { get; init; } = 20000;
    public int FeelingLuckyChainTimeoutMs { get; init; } = 12000;
    public int MakeMyLuckTimeoutMs { get; init; } = 12000;
    public int NotMyMoneyTimeoutMs { get; init; } = 12000;
    public int SkimTimeoutMs { get; init; } = 12000;
    public int WaitingForReactionTimeoutMs { get; init; } = 12000;
    public bool EnableActionTimer { get; init; } = true;
    public bool ShowMakeMyMoneyOperator { get; init; } = true;
    public bool FlipWinCondition { get; init; } = false;

    /// <summary>
    /// When true, the host is dealt into the game as an active participant instead of acting
    /// as a shared spectator display. Set by the lobby's deal buttons (not the House Rules drawer).
    /// </summary>
    public bool HostPlays { get; init; } = false;

    /// <summary>
    /// When true, players have no pot. Drawing a number card applies it directly to the
    /// player's balance using their Active Operator. Drawing an operator card replaces the
    /// player's Active Operator. Skim and Turn The Table are not distributed in this mode;
    /// Turn The Table is repurposed to reverse balance digits when played.
    /// </summary>
    public bool ActiveOperatorMode { get; init; } = false;

    // ── Action card deal-weights ─────────────────────────────────────────
    // Higher value → more likely to be dealt. 0 removes the card from the deal pool entirely.

    public int FeelingLuckyWeight { get; init; } = 10;
    public int MakeMyLuckWeight { get; init; } = 10;
    public int SkimWeight { get; init; } = 10;
    public int BurnWeight { get; init; } = 10;
    public int TurnTheTableWeight { get; init; } = 10;
    public int CompdWeight { get; init; } = 10;
    public int NotMyMoneyWeight { get; init; } = 10;
    public int LaunderWeight { get; init; } = 10;
    public int TiltWeight { get; init; } = 1;
    public int HedgeYourBetWeight { get; init; } = 10;
    public int LetItRideWeight { get; init; } = 10;
}
