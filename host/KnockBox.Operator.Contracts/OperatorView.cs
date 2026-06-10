using KnockBox.Operator.Models;

namespace KnockBox.Operator.Contracts;

/// <summary>
/// Per-player projected view of an Operator lobby, sent server → browser over the hub.
/// The projection is <b>default-deny</b>: the recipient sees their own
/// <see cref="MyHand"/> in full (each card pre-rendered with its play affordances), but
/// every other player's hand is reduced to <see cref="OperatorPlayerView.HandCount"/>.
/// The draw <c>Deck</c> order never crosses the wire (only <see cref="DeckCount"/>).
/// </summary>
public sealed record OperatorView(
    OperatorGamePhase Phase,
    Guid HostId,
    Guid RecipientId,
    // ── Recipient role flags (computed against RecipientId) ───────────────────
    bool IsHost,
    bool IsParticipant,
    bool IsHostObserver,
    bool IsMyTurn,
    bool IsJoinable,
    Guid? CurrentPlayerId,
    IReadOnlyList<RosterEntryView> Roster,
    IReadOnlyList<OperatorPlayerView> Players,
    OperatorSettingsView Settings,
    // The ± starting-score choices (server settings, surfaced so the Setup buttons can label them).
    decimal SetupPositivePoints,
    decimal SetupNegativePoints,
    // CountdownClock target for the current timed phase; null when untimed or timers off.
    DateTimeOffset? PhaseExpiresAtUtc,
    int TurnCount,
    int DeckCount,
    // Discard pile is face-up/public. CardView affordance fields are inert here.
    IReadOnlyList<CardView> DiscardPile,
    IReadOnlyList<ActionLogView> ActionLog,
    // ── Recipient-only ────────────────────────────────────────────────────────
    IReadOnlyList<CardView>? MyHand,
    bool HasPlayedCardThisTurn,
    // Public table event during a Reaction window (attacker + card + targets).
    PendingActionView? PendingAction,
    // Populated only when the recipient is a current reaction target.
    ReactionOptionsView? MyReactionOptions,
    string? LastBlockedActionMessage,
    Guid? BlockedAttackerId,
    Guid? WinnerPlayerId,
    IReadOnlyList<PlayerStandingView> Standings);

/// <summary>A lobby roster entry (host + joined players), display names only.</summary>
public sealed record RosterEntryView(Guid PlayerId, string DisplayName, bool IsHost);

/// <summary>
/// A single card on the wire. Behaviour-bearing server <c>Card</c> objects cannot cross
/// the boundary, so the projector pre-renders the display strings and — for the
/// recipient's own hand — the play affordances computed from the server rules engine
/// (<c>IsPlayable</c>, valid targets, pairable cards). The WASM UI runs only the pure,
/// card-type-based selection combinatorics on top of these; it never re-implements rules.
/// For non-hand cards (discard pile) the affordance fields are inert (false / empty).
/// </summary>
public sealed record CardView(
    Guid Id,
    CardType Type,
    CardOperator Operator,   // None unless Type == Operator
    CardAction Action,       // None unless Type == Action
    decimal? NumberValue,    // null unless Type == Number
    string Icon,
    string TooltipName,
    string TooltipDescription,
    bool IsPlayable,
    IReadOnlyList<Guid> ValidTargetPlayerIds,
    IReadOnlyList<Guid> PairableCardIds);

/// <summary>Public per-player state. Secret hand contents are reduced to <see cref="HandCount"/>.</summary>
public sealed record OperatorPlayerView(
    Guid UserId,
    string DisplayName,
    decimal CurrentPoints,
    CardOperator ActiveOperator,
    int HandCount,
    bool IsAudited,
    bool IsBeingStolenFrom,
    bool IsDivideBroken,
    bool IsCurrentTurn,
    bool IsReactionTarget,
    // Server-assigned live standing (1 = closest to zero / leading), so the spectator
    // leaderboard reproduces the server tiebreak without leaking ScoreTimestamp.
    int LiveRank);

/// <summary>The action awaiting reactions: attacker, the primary card, and the targets.</summary>
public sealed record PendingActionView(
    Guid AttackerId,
    string AttackerName,
    CardView? Card,
    IReadOnlyList<Guid> TargetPlayerIds,
    string Description);

/// <summary>
/// The recipient's available reactions while they are a reaction target: shield card ids
/// to block, and (when the pending action is a Hot Potato) the redirect card + valid new
/// targets.
/// </summary>
public sealed record ReactionOptionsView(
    IReadOnlyList<Guid> ShieldCardIds,
    Guid? HotPotatoRedirectCardId,
    IReadOnlyList<Guid> ValidRedirectTargetIds);

/// <summary>A public activity-log line. <paramref name="Timestamp"/> is server wall-clock UTC, used
/// for the spectator feed's relative "Ns ago" stamp and the history modal's per-entry times.</summary>
public sealed record ActionLogView(string Message, Guid? SourceId, Guid? TargetId, DateTimeOffset Timestamp);

/// <summary>A final-standings row (game over).</summary>
public sealed record PlayerStandingView(
    Guid UserId,
    string DisplayName,
    decimal FinalPoints,
    int Rank,
    bool IsWinner);
