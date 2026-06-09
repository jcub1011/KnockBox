using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.DiceSimulator.Contracts;
using KnockBox.DiceSimulator.Services.State.Games;
using KnockBox.DiceSimulator.Services.State.Games.Data;

namespace KnockBox.DiceSimulator.Services.Projection;

/// <summary>
/// Builds the per-recipient <see cref="DiceSimulatorView"/>. Dice Simulator has no
/// hidden state, so the projection is symmetric — every recipient sees the full roll
/// log and leaderboard — but it still goes through the default-deny projection base,
/// which is the serialized wire boundary every other game's security depends on.
/// <para>
/// Called inside <c>AbstractGameState.WithExclusiveRead</c> by the host's
/// <c>GameViewCoordinator</c>, so it observes a consistent snapshot.
/// </para>
/// </summary>
public sealed class DiceSimulatorStateProjector
    : AbstractStateProjector<DiceSimulatorGameState, DiceSimulatorView>
{
    public override DiceSimulatorView ProjectFor(DiceSimulatorGameState state, Guid recipientId)
    {
        var roster = state.RosterIncludingHost
            .Select(e => new RosterEntryView(e.User.Id, e.DisplayName, e.User.Id == state.Host.Id))
            .ToList();

        var leaderboard = state.PlayerStats
            .OrderByDescending(kvp => kvp.Value.TotalRolls)
            .Select(kvp => ToView(kvp.Key, kvp.Value))
            .ToList();

        return new DiceSimulatorView(
            state.IsJoinable,
            state.Host.Id,
            recipientId,
            roster,
            leaderboard,
            state.RollHistory);
    }

    private static PlayerStatsView ToView(Guid playerId, PlayerStats stats)
        => new(
            playerId,
            stats.PlayerName,
            stats.TotalRolls,
            stats.TotalDiceRolled,
            stats.NatTwentyCount,
            stats.NatOneCount,
            stats.HighestResult,
            stats.HighestResultExpression,
            stats.CumulativeTotal,
            stats.RollCountByDie.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value));
}
