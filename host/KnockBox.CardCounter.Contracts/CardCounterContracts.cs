using System.Text.Json.Serialization;

namespace KnockBox.CardCounter.Contracts;

/// <summary>
/// Per-player projected view of a Card Counter lobby, sent server → browser over the
/// hub. The projection is <b>default-deny</b>: each recipient sees their own
/// <see cref="PlayerView.ActionHand"/> / <see cref="PlayerView.PrivateReveal"/> in full
/// but only an <see cref="PlayerView.ActionHandCount"/> for everyone else, and the
/// server-only deck stacks (main deck / shoe / discard pile) never cross the wire.
/// </summary>
public sealed record CardCounterView(
    GamePhase Phase,
    Guid HostId,
    Guid RecipientId,
    bool HostIsParticipant,
    bool IsJoinable,
    Guid? CurrentPlayerId,
    IReadOnlyList<RosterEntryView> Roster,
    IReadOnlyList<PlayerView> Players,
    int ShoeIndex,
    IReadOnlyDictionary<string, int> ShoeCardCounts,
    bool IsNewShoe,
    int MainDeckCount,
    int CurrentShoeCount,
    IReadOnlyList<DiscardHistoryEntry> DiscardHistory,
    LastPlayedActionInfo? LastPlayedAction,
    PendingReactionInfo? PendingReaction,
    Guid? FeelingLuckyTargetId,
    LastDrawnCardInfo? LastDrawnCard,
    bool IsNotMyMoneySelecting,
    Operator? PendingNotMyMoneyOperator,
    OperatorResultInfo? LastOperatorResult,
    OperatorChangeInfo? LastOperatorChange,
    Guid? HedgeYourBetPlayerId,
    CardCounterSettings Settings,
    DateTimeOffset? PhaseEndsAtUtc,
    int PhaseDurationSeconds,
    DateTime CreatedAtUtc,
    Guid? ForceDrawTopId);

/// <summary>A pre-game lobby roster entry (host + joined players), display names only.</summary>
public sealed record RosterEntryView(Guid PlayerId, string DisplayName, bool IsHost);

/// <summary>
/// Per-player in-game state. The secret fields (<see cref="ActionHand"/>,
/// <see cref="PrivateReveal"/>) are non-null only when this player is the projection
/// recipient; every other recipient sees them as <see langword="null"/> and learns only
/// <see cref="ActionHandCount"/>.
/// </summary>
public sealed record PlayerView(
    Guid PlayerId,
    string DisplayName,
    double Balance,
    IReadOnlyList<int> Pot,
    double PotValue,
    int PassesRemaining,
    int ExtraTurns,
    bool HasSetBuyIn,
    int BuyInRoll,
    Operator? ActiveOperator,
    int ActionHandCount,
    IReadOnlyList<ActionCard>? ActionHand,
    IReadOnlyList<BaseCard>? PrivateReveal);

/// <summary>Command names the client sends to the server engine via the hub.</summary>
public static class CardCounterCommands
{
    public const string Start = "start";                       // host deals (HostPlays carried in payload)
    public const string SetBuyIn = "set-buy-in";
    public const string DrawCard = "draw-card";
    public const string PassTurn = "pass-turn";
    public const string FoldPot = "fold-pot";
    public const string PlayAction = "play-action";
    public const string AcceptPending = "accept-pending";
    public const string SubmitReorder = "submit-reorder";
    public const string Discard = "discard";
    public const string SkimSelect = "skim-select";
    public const string NotMyMoneyTarget = "not-my-money-target";
    public const string NotMyMoneyCancel = "not-my-money-cancel";
    public const string ReturnToLobby = "return-to-lobby";
    public const string UpdateSettings = "update-settings";
    public const string KickPlayer = "kick-player";
}

/// <summary>Payload for <see cref="CardCounterCommands.Start"/>: whether the host plays.</summary>
public sealed record StartPayload(bool HostPlays);

/// <summary>Payload for <see cref="CardCounterCommands.SetBuyIn"/>.</summary>
public sealed record SetBuyInPayload(bool IsNegative);

/// <summary>Payload for <see cref="CardCounterCommands.PlayAction"/>.</summary>
public sealed record PlayActionPayload(int CardIndex, Guid? TargetPlayerId);

/// <summary>Payload for <see cref="CardCounterCommands.SubmitReorder"/>.</summary>
public sealed record ReorderPayload(int[] ReorderedIndices);

/// <summary>Payload for <see cref="CardCounterCommands.Discard"/>.</summary>
public sealed record DiscardPayload(int[] CardIndices);

/// <summary>Payload for <see cref="CardCounterCommands.SkimSelect"/>.</summary>
public sealed record SkimPayload(int SourceDigitIndex, int TargetDigitIndex);

/// <summary>Payload for <see cref="CardCounterCommands.NotMyMoneyTarget"/>.</summary>
public sealed record TargetPayload(Guid TargetPlayerId);

/// <summary>
/// Source-generated JSON context so the contract DTOs survive IL trimming in the WASM
/// client without reflection roots. <c>UseStringEnumConverter</c> matches the server's
/// wire format (the host's <c>GameViewCoordinator</c> writes enums as strings) for the
/// projected view and every command payload. <see cref="CardCounterSettings"/> doubles
/// as the <c>update-settings</c> payload.
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(CardCounterView))]
[JsonSerializable(typeof(CardCounterSettings))]
[JsonSerializable(typeof(BaseCard))]
[JsonSerializable(typeof(StartPayload))]
[JsonSerializable(typeof(SetBuyInPayload))]
[JsonSerializable(typeof(PlayActionPayload))]
[JsonSerializable(typeof(ReorderPayload))]
[JsonSerializable(typeof(DiscardPayload))]
[JsonSerializable(typeof(SkimPayload))]
[JsonSerializable(typeof(TargetPayload))]
public partial class CardCounterContractsJsonContext : JsonSerializerContext;
