using KnockBox.Core.Plugins;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// Concrete <see cref="IPluginStorage"/> rooted at an absolute directory. Every
/// caller-supplied <c>relativePath</c> is joined against the root and the
/// resulting full path is verified to remain inside the root (rejects absolute
/// paths, rooted paths, and <c>..</c>-escapes). The root directory is created
/// on first write if it doesn't already exist.
/// </summary>
internal sealed class DefaultPluginStorage(string rootDirectory) : IPluginStorage
{
    private readonly string _root = Path.GetFullPath(rootDirectory);

    public Stream OpenRead(string relativePath)
    {
        var full = Resolve(relativePath);
        return File.OpenRead(full);
    }

    public Stream OpenWrite(string relativePath)
    {
        var full = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return File.Create(full);
    }

    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));

    public void Delete(string relativePath)
    {
        var full = Resolve(relativePath);
        if (File.Exists(full))
            File.Delete(full);
    }

    public IEnumerable<string> EnumerateFiles(string relativeDir, string searchPattern)
    {
        var dir = Resolve(relativeDir);
        if (!Directory.Exists(dir))
            yield break;

        foreach (var full in Directory.EnumerateFiles(dir, searchPattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_root, full).Replace(Path.DirectorySeparatorChar, '/');
            yield return relative;
        }
    }

    /// <summary>
    /// Joins <paramref name="relativePath"/> to the plugin root and rejects any
    /// path that resolves outside it. Absolute paths and rooted paths fail
    /// before the join because <see cref="Path.Combine(string, string)"/> would
    /// silently replace the root.
    /// </summary>
    private string Resolve(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException(
                $"Plugin storage path [{relativePath}] must be relative, not rooted/absolute.",
                nameof(relativePath));
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, relativePath));
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;

        if (!string.Equals(candidate, _root, StringComparison.Ordinal)
            && !candidate.StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Plugin storage path [{relativePath}] resolves outside the plugin's root directory.",
                nameof(relativePath));
        }

        return candidate;
    }
}
