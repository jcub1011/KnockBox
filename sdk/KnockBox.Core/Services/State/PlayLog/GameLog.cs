using System.Collections.ObjectModel;

namespace KnockBox.Core.Services.State.PlayLog;

/// <summary>
/// A single entry in a user's <see cref="IPlayLogService">play log</see>: a
/// record that they played a given game, plus arbitrary game-supplied metadata
/// (leaderboard position, duration played, score, …). Persisted to the
/// browser so it survives across sessions.
/// </summary>
public sealed record GameLog
{
    /// <summary>
    /// Stable identifier of the game that was played — the plugin's
    /// <c>RouteIdentifier</c> (e.g. <c>"card-counter"</c>). Lets the log be
    /// grouped and filtered by game without depending on the (mutable) display
    /// name.
    /// </summary>
    public required string GameIdentifier { get; init; }

    /// <summary>
    /// Arbitrary, game-defined metadata about the play session. Values are
    /// strings so the log round-trips losslessly through JSON; games format
    /// their own values (e.g. <c>"3"</c> for placement, <c>"00:12:45"</c> for a
    /// duration). Empty when the game records no metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// When the game was played (UTC). Stamped by
    /// <see cref="IPlayLogService.StoreLogAsync"/> at store time — callers
    /// construct a <see cref="GameLog"/> and leave this at its default; any
    /// value they set is overwritten.
    /// </summary>
    public DateTimeOffset PlayedAt { get; init; }

    /// <summary>
    /// Convenience factory for the common case: a game id plus optional
    /// metadata. <see cref="PlayedAt"/> is left default and stamped by the
    /// service on store.
    /// </summary>
    public static GameLog Create(
        string gameIdentifier,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new()
        {
            GameIdentifier = gameIdentifier,
            Metadata = metadata ?? ReadOnlyDictionary<string, string>.Empty,
        };
}
