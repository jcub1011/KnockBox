namespace KnockBox.Core.Services.State.PlayLog;

/// <summary>
/// Ergonomic helpers for reading <see cref="GameLog.Metadata"/> without the
/// <c>TryGetValue</c> ceremony at every call site.
/// </summary>
public static class GameLogExtensions
{
    /// <summary>
    /// Returns the metadata value stored under <paramref name="key"/>, or
    /// <c>null</c> if the game recorded no such key.
    /// </summary>
    public static string? GetMetadata(this GameLog log, string key)
        => log.Metadata.TryGetValue(key, out var value) ? value : null;
}
