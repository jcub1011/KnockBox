using KnockBox.Core.Services.State.PlayLog;
using KnockBox.DiceSimulator.Services.State.Games;

namespace KnockBox.DiceSimulator.Services.State.Games.PlayLog
{
    /// <summary>
    /// Builds the per-user play-log metadata for a Dice Simulator session. Dice Simulator is an
    /// open-ended sandbox tool with no terminal phase, so this is invoked on leave from the session's
    /// accumulated <see cref="DiceSimulatorGameState.RollHistory"/>. Pure and DI-free so it can be
    /// unit-tested directly: it reads the roll history plus per-player roster and emits a string→string
    /// table the home page renders verbatim. Session-level keys (Total Rolls, Players, Duration) are
    /// always present; the personal "My Rolls" key is added only when <paramref name="currentUserId"/>
    /// is known.
    /// </summary>
    internal static class DiceSimulatorPlayLogMetadata
    {
        public static IReadOnlyDictionary<string, string> Build(DiceSimulatorGameState state, Guid? currentUserId)
        {
            var rolls = state.RollHistory;

            var metadata = new Dictionary<string, string>
            {
                ["Total Rolls"] = rolls.Count.ToString(),
            };
            metadata.Set(StandardMetadata.Players, state.PlayerStats.Count.ToString());

            // Personal key — only when the local user is known.
            if (currentUserId is { } userId)
            {
                int myRolls = 0;
                for (int i = 0; i < rolls.Count; i++)
                {
                    if (rolls[i].PlayerId == userId)
                        myRolls++;
                }

                metadata["My Rolls"] = myRolls.ToString();
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
