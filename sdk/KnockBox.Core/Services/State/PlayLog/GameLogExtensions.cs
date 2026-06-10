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

    /// <summary>
    /// Returns the metadata value stored under a <see cref="StandardMetadata"/>
    /// key, or <c>null</c> if the game recorded no such key.
    /// </summary>
    public static string? GetMetadata(this GameLog log, StandardMetadata key)
        => log.GetMetadata(key.ToString());

    /// <summary>
    /// Sets a <see cref="StandardMetadata"/> entry on a metadata dictionary
    /// while it is being built, keying it by the enum member name.
    /// </summary>
    public static void Set(this IDictionary<string, string> metadata, StandardMetadata key, string value)
        => metadata[key.ToString()] = value;
}
