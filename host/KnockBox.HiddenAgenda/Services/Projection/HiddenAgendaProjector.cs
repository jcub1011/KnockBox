using System.Linq;
using KnockBox.HiddenAgenda.Services.State.Games;

namespace KnockBox.HiddenAgenda.Services.Projection;

/// <summary>
/// Builds the per-recipient, default-deny <see cref="HiddenAgendaViewDto"/>.
/// This is the security boundary the WASM migration depends on: anything this
/// returns is serialized and lands in the recipient's browser. The rule is
/// simple and auditable — secret fields are copied <b>only</b> when the player
/// entry being projected belongs to the recipient; every other player's secrets
/// are emitted as <see langword="null"/>.
/// <para>
/// Call inside <c>AbstractGameState.WithExclusiveRead</c> for a consistent
/// snapshot (the host's <c>GameViewCoordinator</c> does this).
/// </para>
/// </summary>
public static class HiddenAgendaProjector
{
    public static HiddenAgendaViewDto ProjectFor(HiddenAgendaGameState state, Guid recipientId)
    {
        var players = state.GamePlayers.Values
            .OrderBy(p => p.PlayerId)
            .Select(p => ProjectPlayer(p, isRecipient: p.PlayerId == recipientId))
            .ToList();

        // Lobby roster is public (display names only) and lets clients see players
        // arrive before the game starts.
        var roster = state.Players
            .Select(e => new RosterEntry(e.User.Id, e.DisplayName))
            .ToList();

        return new HiddenAgendaViewDto(
            state.Phase,
            state.CurrentRound,
            state.IsJoinable,
            state.TurnManager.CurrentPlayer,
            roster,
            players);
    }

    private static HiddenAgendaPlayerView ProjectPlayer(
        State.Games.Data.HiddenAgendaPlayerState p, bool isRecipient)
        => new(
            PlayerId: p.PlayerId,
            DisplayName: p.DisplayName,
            CurrentSpaceId: p.CurrentSpaceId,
            RoundScore: p.RoundScore,
            CumulativeScore: p.CumulativeScore,
            TurnsTakenThisRound: p.TurnsTakenThisRound,
            HasSubmittedGuess: p.HasSubmittedGuess,
            // Secret-bearing fields: recipient's own entry ONLY.
            SecretTasks: isRecipient ? p.SecretTasks : null,
            RivalryTargetPlayerId: isRecipient ? p.RivalryTargetPlayerId : null,
            HeldEventCard: isRecipient ? p.HeldEventCard : null,
            GuessSubmission: isRecipient ? p.GuessSubmission : null);
}
