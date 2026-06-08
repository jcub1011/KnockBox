using KnockBox.Core.Services.State.PlayLog;

namespace KnockBox.LinkedList.Services.State.Games.PlayLog
{
    /// <summary>
    /// Builds the per-user play-log metadata for a finished Linked List match. Pure and
    /// DI-free so it can be unit-tested directly: it reads the terminal
    /// <see cref="LinkedListGameState"/> and emits a string→string table the home page
    /// renders verbatim. Linked List is a cooperative word-chain game, so the team-level
    /// keys (chain length, destination, rounds, players) are always present; personal keys
    /// (the local player's accepted pairs, rejections received, and any superlatives they
    /// earned) are added only when <paramref name="currentUserId"/> is one of the players.
    /// </summary>
    internal static class LinkedListPlayLogMetadata
    {
        public static IReadOnlyDictionary<string, string> Build(LinkedListGameState state, Guid? currentUserId)
        {
            var metadata = new Dictionary<string, string>();

            // Team-level keys (always present). The single-chain accessors throw when no
            // group exists, so read the chain length defensively through PrimaryGroup.
            metadata["Chain Length"] = (state.PrimaryGroup?.Chain.Count ?? 0).ToString();
            metadata["Destination Reached"] = state.DestinationReached ? "Yes" : "No";
            metadata.Set(StandardMetadata.Rounds, state.RoundNumber.ToString());
            metadata.Set(StandardMetadata.Players, state.GamePlayers.Count.ToString());

            // Personal keys — only when the local user actually played this match.
            if (currentUserId is { } userId && state.GamePlayers.TryGetValue(userId, out var me))
            {
                metadata["My Accepted Pairs"] = me.AcceptedPairs.ToString();
                metadata["Rejections Received"] = me.RejectionsReceived.ToString();

                var myTitles = state.Superlatives
                    .Where(s => s.PlayerId == userId)
                    .Select(s => s.Title)
                    .ToList();

                if (myTitles.Count > 0)
                    metadata["Superlatives"] = string.Join(", ", myTitles);
            }

            return metadata;
        }
    }
}
