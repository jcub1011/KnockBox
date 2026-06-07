using KnockBox.Core.Services.State.PlayLog;
using KnockBox.TaskMaster.Services.State.Games;

namespace KnockBox.TaskMaster.Services.State.Games.PlayLog
{
    /// <summary>
    /// Builds the per-user play-log metadata for a finished Task Master match. Pure and
    /// DI-free so it can be unit-tested directly: it reads the terminal
    /// <see cref="TaskMasterGameState"/> and emits a string→string table the home page
    /// renders verbatim. Task Master tracks no per-player scoring on the state, so the
    /// match-level keys (Players, Duration) are always present; the personal "Result" key
    /// is added only when <paramref name="currentUserId"/> is one of the lobby's players.
    /// </summary>
    internal static class TaskMasterPlayLogMetadata
    {
        public static IReadOnlyDictionary<string, string> Build(TaskMasterGameState state, Guid? currentUserId)
        {
            var players = state.Players;

            var metadata = new Dictionary<string, string>();

            // Match-level keys (always present).
            metadata.Set(StandardMetadata.Players, players.Length.ToString());

            // Personal key — only when the local user actually played this match.
            if (currentUserId is { } userId)
            {
                bool played = false;
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i].User.Id == userId)
                    {
                        played = true;
                        break;
                    }
                }

                if (played)
                    metadata.Set(StandardMetadata.Result, "Completed");
            }

            var elapsed = DateTime.UtcNow - state.CreatedAt;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            // h:mm:ss once we cross an hour, otherwise the tighter mm:ss.
            metadata.Set(StandardMetadata.Duration, elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"mm\:ss"));

            return metadata;
        }
    }
}
