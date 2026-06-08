using System.Collections.Generic;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.State.Games;

namespace KnockBox.HiddenAgenda.Services.Projection;

/// <summary>
/// The per-player projected view of a Hidden Agenda room — the ONLY Hidden
/// Agenda data that crosses the wire to a browser. It is built by
/// <see cref="HiddenAgendaProjector"/> under the state read lock and serialized
/// to the recipient. Secret-bearing fields on <see cref="HiddenAgendaPlayerView"/>
/// are populated only for the recipient's own entry (default-deny); every other
/// player's secret fields are <see langword="null"/>.
/// <para>
/// In Phase 1 this DTO moves into a <c>KnockBox.HiddenAgenda.Contracts</c>
/// assembly shared with the client; for the spike it lives here and is consumed
/// client-side as raw JSON.
/// </para>
/// </summary>
public sealed record HiddenAgendaViewDto(
    GamePhase Phase,
    int CurrentRound,
    bool IsJoinable,
    Guid? CurrentPlayerId,
    // Lobby roster (public): who is in the room. Populated as players join, even
    // before the game starts (GamePlayers is empty until start).
    IReadOnlyList<RosterEntry> Roster,
    // Per-player game view (post-start): the secret-bearing entries, default-deny.
    IReadOnlyList<HiddenAgendaPlayerView> Players);

public sealed record RosterEntry(Guid PlayerId, string DisplayName);

public sealed record HiddenAgendaPlayerView(
    // ── Public to every player ───────────────────────────────────────────────
    Guid PlayerId,
    string DisplayName,
    int CurrentSpaceId,
    int RoundScore,
    int CumulativeScore,
    int TurnsTakenThisRound,
    bool HasSubmittedGuess,
    // ── Recipient-only (null for everyone except the recipient) ──────────────
    IReadOnlyList<SecretTask>? SecretTasks,
    Guid? RivalryTargetPlayerId,
    EventCard? HeldEventCard,
    IReadOnlyDictionary<Guid, List<string>>? GuessSubmission);
