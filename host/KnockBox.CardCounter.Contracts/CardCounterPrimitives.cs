using System.Text.Json.Serialization;

namespace KnockBox.CardCounter.Contracts;

// Pure data types shared by the server engine/state and the browser UI. Moved here
// from the server project (KnockBox.CardCounter.Services.State.Games) so both runtimes
// bind identical CLR types across the projection/command wire.

#region Enums

public enum GamePhase
{
    BuyIn,
    Playing,
    GameOver
}

/// <summary>Distinguishes the two card types tracked in the shoe-card counts.</summary>
public enum CardType
{
    Number,
    Operator
}

public enum Operator
{
    Add,
    Subtract,
    Multiply,
    Divide
}

public enum ActionType
{
    FeelingLucky,
    MakeMyLuck,
    Skim,
    Burn,
    TurnTheTable,
    Compd,
    NotMyMoney,
    Launder,
    Tilt,
    HedgeYourBet,
    LetItRide
}

#endregion

#region Cards

// A BaseCard reference (e.g. a player's PrivateReveal, or LastDrawnCardInfo.Card)
// must round-trip its concrete type across the wire, so the hierarchy carries a
// JSON type discriminator honored by both the server's reflection serializer and
// the client's source-gen context.
[JsonDerivedType(typeof(NumberCard), "number")]
[JsonDerivedType(typeof(OperatorCard), "operator")]
[JsonDerivedType(typeof(ActionCard), "action")]
public abstract record BaseCard;

/// <summary>A number card (digit 0–9) drawn into the player's pot.</summary>
public record NumberCard(int Value) : BaseCard;

/// <summary>An operator card that applies arithmetic to the player's pot and balance.</summary>
public record OperatorCard(Operator Op) : BaseCard;

/// <summary>An action card drawn from the separate action deck.</summary>
public record ActionCard(ActionType Action) : BaseCard;

#endregion

#region Supporting Types

/// <summary>Information about the most recently played action card, shown to all players.</summary>
public record LastPlayedActionInfo(
    Guid PlayerId,
    string PlayerName,
    ActionType Action,
    Guid? TargetId,
    string? TargetName);

/// <summary>Information about a pending blockable reaction (Skim, TurnTheTable, Launder).</summary>
public record PendingReactionInfo(
    Guid SourceId,
    string SourceName,
    Guid TargetId,
    ActionCard PlayedCard,
    int? SourceDigitIndex = null,
    int? TargetDigitIndex = null,
    Operator? NotMyMoneyOperator = null);

/// <summary>
/// Information about the most recently drawn shoe card, shown to all players as an overlay.
/// </summary>
public record LastDrawnCardInfo(
    Guid DrawerId,
    string DrawerName,
    BaseCard Card,
    Guid? RedirectTargetId = null,
    string? RedirectTargetName = null);

/// <summary>A single entry in the visible discard pile history.</summary>
public record DiscardHistoryEntry(
    string Description,
    string Symbol,
    string? PlayerName,
    bool IsActionCard);

/// <summary>
/// Records the result of an operator card being applied to a player's balance.
/// Used to show the affected player an overlay with before/after balance.
/// </summary>
public record OperatorResultInfo(
    Guid PlayerId,
    string PlayerName,
    Operator Op,
    double BalanceBefore,
    double BalanceAfter);

/// <summary>
/// Records a change to a player's active operator in Active Operator Mode.
/// Used to show the affected player a toast with the previous and new operator.
/// </summary>
public record OperatorChangeInfo(
    Guid PlayerId,
    string PlayerName,
    Operator? PreviousOperator,
    Operator NewOperator);

#endregion
