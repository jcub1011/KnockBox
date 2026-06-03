using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Plugins;

/// <summary>
/// Per-plugin filesystem abstraction rooted at a host-chosen directory. All
/// paths are interpreted relative to that root; absolute paths, rooted paths,
/// and paths that escape the root (via <c>..</c> or similar) are rejected with
/// <see cref="System.ArgumentException"/> (an authoring/programming error).
/// Runtime I/O failures are reported through <see cref="Result"/> /
/// <see cref="ValueResult{T}"/> rather than thrown, so plugin code can branch
/// without try/catch.
/// </summary>
/// <remarks>
/// This is a contract-level boundary only. Nothing stops plugin code from
/// bypassing <see cref="IPluginStorage"/> and calling <c>System.IO</c> APIs
/// directly — that is an authoring violation, not a runtime-enforced one.
/// </remarks>
public interface IPluginStorage
{
    /// <summary>
    /// Opens the file at <paramref name="relativePath"/> for reading. Returns a
    /// failure result when the file is missing or cannot be read.
    /// </summary>
    ValueResult<Stream> OpenRead(string relativePath);

    /// <summary>
    /// Opens the file at <paramref name="relativePath"/> for writing, creating
    /// or truncating. Creates any missing parent directories within the root.
    /// Returns a failure result when the file cannot be opened.
    /// </summary>
    ValueResult<Stream> OpenWrite(string relativePath);

    /// <summary>
    /// Returns <c>true</c> if a file exists at <paramref name="relativePath"/>.
    /// </summary>
    bool Exists(string relativePath);

    /// <summary>
    /// Deletes the file at <paramref name="relativePath"/>. Succeeds (no-op) if
    /// the file is absent; returns a failure result if the delete fails.
    /// </summary>
    Result Delete(string relativePath);

    /// <summary>
    /// Enumerates files under <paramref name="relativeDir"/> (relative to the
    /// plugin root) matching <paramref name="searchPattern"/>. Returned paths
    /// are relative to the plugin root, using forward slashes. Returns a failure
    /// result if enumeration fails.
    /// </summary>
    ValueResult<IReadOnlyList<string>> EnumerateFiles(string relativeDir, string searchPattern);
}
