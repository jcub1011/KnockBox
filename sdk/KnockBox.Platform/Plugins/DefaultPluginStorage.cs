using KnockBox.Core.Plugins;
using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// Concrete <see cref="IPluginStorage"/> rooted at an absolute directory. Every
/// caller-supplied <c>relativePath</c> is joined against the root and the
/// resulting full path is verified to remain inside the root (rejects absolute
/// paths, rooted paths, <c>..</c>-escapes, and reparse-point segments whose
/// final target lives outside the root). The root directory is created on
/// first write if it doesn't already exist.
/// </summary>
internal sealed class DefaultPluginStorage(string rootDirectory) : IPluginStorage
{
    private readonly string _root = PluginPathGuard.NormalizeDirectory(rootDirectory);

    public ValueResult<Stream> OpenRead(string relativePath)
    {
        var full = Resolve(relativePath);
        try
        {
            return ValueResult<Stream>.FromValue(File.OpenRead(full));
        }
        catch (Exception ex)
        {
            return ValueResult<Stream>.FromError(
                "Unable to open plugin file for reading.", $"Error opening [{relativePath}] for read: {ex}");
        }
    }

    public ValueResult<Stream> OpenWrite(string relativePath)
    {
        var full = Resolve(relativePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            return ValueResult<Stream>.FromValue(File.Create(full));
        }
        catch (Exception ex)
        {
            return ValueResult<Stream>.FromError(
                "Unable to open plugin file for writing.", $"Error opening [{relativePath}] for write: {ex}");
        }
    }

    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));

    public Result Delete(string relativePath)
    {
        var full = Resolve(relativePath);
        try
        {
            if (File.Exists(full))
                File.Delete(full);
            return Result.Success;
        }
        catch (Exception ex)
        {
            return Result.FromError("Unable to delete plugin file.", $"Error deleting [{relativePath}]: {ex}");
        }
    }

    public ValueResult<IReadOnlyList<string>> EnumerateFiles(string relativeDir, string searchPattern)
    {
        // Empty means "from root" — skip Resolve (which rejects empty) and
        // start the walk at _root directly.
        var dir = string.IsNullOrEmpty(relativeDir) ? _root : Resolve(relativeDir);
        try
        {
            if (!Directory.Exists(dir))
                return ValueResult<IReadOnlyList<string>>.FromValue([]);

            // Materialize inside the try so a mid-iteration I/O error surfaces as a
            // failure result rather than throwing out of the caller's foreach.
            var results = new List<string>();
            foreach (var full in Directory.EnumerateFiles(dir, searchPattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(_root, full).Replace(Path.DirectorySeparatorChar, '/');
                results.Add(relative);
            }
            return ValueResult<IReadOnlyList<string>>.FromValue(results);
        }
        catch (Exception ex)
        {
            return ValueResult<IReadOnlyList<string>>.FromError(
                "Unable to enumerate plugin files.", $"Error enumerating [{relativeDir}]: {ex}");
        }
    }

    /// <summary>
    /// Joins <paramref name="relativePath"/> to the plugin root and rejects any
    /// path that resolves outside it. Absolute paths and rooted paths fail
    /// before the join because <see cref="Path.Combine(string, string)"/> would
    /// silently replace the root. Any existing path segment that is a reparse
    /// point (symlink or NTFS junction) has its final target checked against
    /// the root; reparse points whose target escapes the root are rejected.
    /// </summary>
    /// <remarks>
    /// Validation is non-atomic: a hostile plugin can race the filesystem
    /// between this check and the subsequent open. That TOCTOU window is
    /// accepted — this is a contract boundary, not a security boundary.
    /// Hardlinks are also out of scope: a hardlink is not a reparse point and
    /// cannot be distinguished from a regular directory entry by path
    /// inspection, so a plugin with write access could in principle create one
    /// pointing to an outside inode. Preventing that would require
    /// inode-level checks that aren't worth the cost given the trust model.
    /// <c>_root</c> itself is normalized through
    /// <c>DirectoryInfo.ResolveLinkTarget</c> in the constructor, so operators
    /// who hand in a symlinked mount path (e.g. macOS <c>/var</c>) get the
    /// expected behavior without a manual resolve step.
    /// </remarks>
    private string Resolve(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        if (string.IsNullOrEmpty(relativePath))
        {
            throw new ArgumentException(
                "Plugin storage relative path must not be empty.",
                nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException(
                $"Plugin storage path [{relativePath}] must be relative, not rooted/absolute.",
                nameof(relativePath));
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, relativePath));

        if (!PluginPathGuard.IsInsideRoot(_root, candidate))
        {
            throw new ArgumentException(
                $"Plugin storage path [{relativePath}] resolves outside the plugin's root directory.",
                nameof(relativePath));
        }

        if (!PluginPathGuard.HasNoReparsePointEscape(_root, candidate, out var reason))
        {
            throw new ArgumentException(
                $"Plugin storage path [{relativePath}]: {reason}",
                nameof(relativePath));
        }

        return candidate;
    }
}
