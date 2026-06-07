using System.Collections.ObjectModel;

namespace KnockBox.Core.Services.State.PlayLog;

/// <summary>
/// Well-known keys for <see cref="GameLog.Metadata"/>. The point of the enum is
/// that <c>.ToString()</c> on a member is the dictionary key, so adding a new
/// common field across games is just a new member here — no change to the
/// <see cref="GameLog"/> record and so no breaking change.
/// <para>
/// Members MUST be single PascalCase tokens (the displayed key is the member
/// name verbatim). Game-specific or multi-word keys (e.g. <c>"My Score"</c>,
/// <c>"Chain Length"</c>) stay as plain string literals at their call site.
/// </para>
/// </summary>
public enum StandardMetadata
{
    /// <summary>Whether the user hosted or merely joined; value is one of
    /// <see cref="PlayLogRoles"/>. Stamped automatically by the lobby page base.</summary>
    Role,

    /// <summary>Number of players in the session.</summary>
    Players,

    /// <summary>Display name of the winning player.</summary>
    Winner,

    /// <summary>The user's finishing position, e.g. <c>"2 / 5"</c>.</summary>
    Placement,

    /// <summary>How long the session lasted.</summary>
    Duration,

    /// <summary>The user's outcome, e.g. <c>"Won"</c> / <c>"Eliminated"</c>.</summary>
    Result,

    /// <summary>Number of rounds played.</summary>
    Rounds,

    /// <summary>The user's score.</summary>
    Score,
}

/// <summary>
/// Canonical values for the <see cref="StandardMetadata.Role"/> metadata entry,
/// shared between the writer (the lobby page base, which stamps it) and the
/// reader (the home-page panel, which renders the badge).
/// </summary>
public static class PlayLogRoles
{
    /// <summary>The user created and hosted the lobby.</summary>
    public const string Host = "Host";

    /// <summary>The user joined the lobby as a participant.</summary>
    public const string Player = "Player";
}

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
    /// Convenience factory: a game id plus optional metadata. The user's role
    /// and any other standard fields live inside <paramref name="metadata"/>
    /// (see <see cref="StandardMetadata"/>). <see cref="PlayedAt"/> is left
    /// default and stamped by the service on store.
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
