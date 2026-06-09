using System.Text.Json.Serialization;

namespace KnockBox.DiceSimulator.Contracts;

/// <summary>
/// Per-player projected view of a Dice Simulator lobby. Sent server → browser over
/// the hub. Dice Simulator is fully symmetric (no hidden state), so every recipient
/// receives the same data — but it still crosses the projection boundary, which is
/// the security contract every other game depends on.
/// </summary>
public sealed record DiceSimulatorView(
    bool IsJoinable,
    Guid HostId,
    Guid RecipientId,
    IReadOnlyList<RosterEntryView> Roster,
    IReadOnlyList<PlayerStatsView> Leaderboard,
    IReadOnlyList<DiceRollEntry> RollHistory);

/// <summary>A lobby roster entry (host + players), display names only.</summary>
public sealed record RosterEntryView(Guid PlayerId, string DisplayName, bool IsHost);

/// <summary>
/// Immutable per-player leaderboard stats. <see cref="RollCountByDie"/> is keyed by
/// the die's string name (e.g. <c>"D20"</c>) rather than the <see cref="DiceType"/>
/// enum so it round-trips cleanly through System.Text.Json dictionary serialization.
/// </summary>
public sealed record PlayerStatsView(
    Guid PlayerId,
    string PlayerName,
    int TotalRolls,
    int TotalDiceRolled,
    int NatTwentyCount,
    int NatOneCount,
    int HighestResult,
    string? HighestResultExpression,
    int CumulativeTotal,
    IReadOnlyDictionary<string, int> RollCountByDie);

/// <summary>Command names the client sends to the server engine via the hub.</summary>
public static class DiceSimulatorCommands
{
    public const string Start = "start";
    public const string RollDice = "roll-dice";
    public const string ClearHistory = "clear-history";
    public const string KickPlayer = "kick-player";
}

/// <summary>
/// Source-generated JSON context so the contract DTOs survive IL trimming in the
/// WASM client without reflection roots. <c>UseStringEnumConverter</c> matches the
/// server's wire format (the host's <c>GameViewCoordinator</c> writes enums as
/// strings) for both the projected view and the roll-command payload.
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiceSimulatorView))]
[JsonSerializable(typeof(DiceRollAction))]
public partial class DiceSimulatorContractsJsonContext : JsonSerializerContext;
