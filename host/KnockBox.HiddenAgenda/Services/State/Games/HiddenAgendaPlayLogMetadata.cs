using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.State.PlayLog;

namespace KnockBox.HiddenAgenda.Services.State.Games;

/// <summary>
/// Builds the per-user play-log metadata for a completed Hidden Agenda match.
/// Pure function over a <see cref="GamePhase.MatchOver"/> state — match-level
/// facts are always emitted; personal facts only when <paramref name="currentUserId"/>
/// is one of the players. All values are strings (the play-log contract).
/// </summary>
internal static class HiddenAgendaPlayLogMetadata
{
    public static IReadOnlyDictionary<string, string> Build(HiddenAgendaGameState state, Guid? currentUserId)
    {
        var ranked = state.GamePlayers.Values
            .OrderByDescending(p => p.CumulativeScore)
            .ToList();

        var metadata = new Dictionary<string, string>();
        metadata.Set(StandardMetadata.Rounds, state.RoundResults.Count.ToString());
        metadata.Set(StandardMetadata.Players, state.GamePlayers.Count.ToString());

        if (state.MatchWinner is { } winnerId
            && state.GamePlayers.TryGetValue(winnerId, out var winner))
        {
            metadata.Set(StandardMetadata.Winner, winner.DisplayName);
        }

        if (currentUserId is { } userId
            && state.GamePlayers.TryGetValue(userId, out var me))
        {
            metadata["My Score"] = me.CumulativeScore.ToString();

            var placement = ranked.FindIndex(p => p.PlayerId == userId) + 1;
            metadata.Set(StandardMetadata.Placement, $"{placement} / {ranked.Count}");

            metadata.Set(StandardMetadata.Result, userId == state.MatchWinner ? "Won" : "Lost");
        }

        return metadata;
    }
}
