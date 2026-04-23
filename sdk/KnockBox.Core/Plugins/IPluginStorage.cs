namespace KnockBox.Core.Plugins;

/// <summary>
/// Per-plugin filesystem abstraction rooted at a host-chosen directory. All
/// paths are interpreted relative to that root; absolute paths, rooted paths,
/// and paths that escape the root (via <c>..</c> or similar) are rejected with
/// <see cref="ArgumentException"/>.
/// </summary>
/// <remarks>
/// This is a contract-level boundary only. Nothing stops plugin code from
/// bypassing <see cref="IPluginStorage"/> and calling <c>System.IO</c> APIs
/// directly — that is an authoring violation, not a runtime-enforced one.
/// </remarks>
public interface IPluginStorage
{
    /// <summary>
    /// Opens the file at <paramref name="relativePath"/> for reading. Throws
    /// <see cref="FileNotFoundException"/> if the file does not exist.
    /// </summary>
    Stream OpenRead(string relativePath);

    /// <summary>
    /// Opens the file at <paramref name="relativePath"/> for writing, creating
    /// or truncating. Creates any missing parent directories within the root.
    /// </summary>
    Stream OpenWrite(string relativePath);

    /// <summary>
    /// Returns <c>true</c> if a file exists at <paramref name="relativePath"/>.
    /// </summary>
    bool Exists(string relativePath);

    /// <summary>
    /// Deletes the file at <paramref name="relativePath"/>. No-op if absent.
    /// </summary>
    void Delete(string relativePath);

    /// <summary>
    /// Enumerates files under <paramref name="relativeDir"/> (relative to the
    /// plugin root) matching <paramref name="searchPattern"/>. Returned paths
    /// are relative to the plugin root, using forward slashes.
    /// </summary>
    IEnumerable<string> EnumerateFiles(string relativeDir, string searchPattern);
}
