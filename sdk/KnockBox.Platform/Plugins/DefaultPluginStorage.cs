using KnockBox.Core.Plugins;

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
    private readonly string _root = NormalizeRoot(rootDirectory);

    /// <summary>
    /// Resolves the root to its final on-disk target: runs <see cref="Path.GetFullPath(string)"/>,
    /// then — if the directory itself is a reparse point — follows it to its
    /// ultimate target. This keeps the lexical containment check in
    /// <c>IsInsideRoot</c> meaningful on macOS (<c>/var</c> → <c>/private/var</c>),
    /// Docker bind mounts, and other setups where an operator hands in a
    /// symlinked path. Broken links or resolve failures fall back to the
    /// lexical form.
    /// </summary>
    private static string NormalizeRoot(string rootDirectory)
    {
        var full = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(full))
            return full;

        try
        {
            var info = new DirectoryInfo(full);
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                return full;

            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            return resolved?.FullName ?? full;
        }
        catch (IOException)
        {
            return full;
        }
    }

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
        // Empty means "from root" — skip Resolve (which rejects empty) and
        // start the walk at _root directly.
        var dir = string.IsNullOrEmpty(relativeDir) ? _root : Resolve(relativeDir);
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

        if (!IsInsideRoot(candidate))
        {
            throw new ArgumentException(
                $"Plugin storage path [{relativePath}] resolves outside the plugin's root directory.",
                nameof(relativePath));
        }

        EnsureNoReparsePointEscape(candidate, relativePath);

        return candidate;
    }

    /// <summary>
    /// Walks the lexical path from the root down to <paramref name="candidate"/>,
    /// checking each existing segment. Any segment that is a reparse point
    /// whose final target lives outside <c>_root</c> causes an
    /// <see cref="ArgumentException"/>. Non-existent segments terminate the
    /// walk — a file that doesn't exist yet (common for <c>OpenWrite</c>)
    /// cannot be a reparse point.
    /// </summary>
    private void EnsureNoReparsePointEscape(string candidate, string relativePath)
    {
        System.Diagnostics.Debug.Assert(
            IsInsideRoot(candidate),
            "EnsureNoReparsePointEscape precondition: candidate must already be lexically inside _root.");

        var relativeFromRoot = Path.GetRelativePath(_root, candidate);
        var segments = relativeFromRoot.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var currentPath = _root;
        foreach (var segment in segments)
        {
            currentPath = Path.Combine(currentPath, segment);

            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(currentPath);
            }
            catch (FileNotFoundException)
            {
                // Segment doesn't exist yet — nothing further to check.
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            if ((attrs & FileAttributes.ReparsePoint) == 0)
                continue;

            FileSystemInfo? resolved;
            try
            {
                resolved = (attrs & FileAttributes.Directory) != 0
                    ? new DirectoryInfo(currentPath).ResolveLinkTarget(returnFinalTarget: true)
                    : new FileInfo(currentPath).ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (IOException ex)
            {
                // Some runtime/platform combinations throw instead of returning
                // null for broken or unresolvable link chains; normalize to
                // ArgumentException so callers see one rejection shape.
                throw new ArgumentException(
                    $"Plugin storage path [{relativePath}] traverses reparse-point segment [{currentPath}] whose target could not be resolved: {ex.Message}",
                    nameof(relativePath));
            }

            if (resolved is null)
            {
                throw new ArgumentException(
                    $"Plugin storage path [{relativePath}] traverses reparse-point segment [{currentPath}] whose target could not be resolved.",
                    nameof(relativePath));
            }

            if (!IsInsideRoot(resolved.FullName))
            {
                throw new ArgumentException(
                    $"Plugin storage path [{relativePath}] traverses reparse-point segment [{currentPath}] whose final target [{resolved.FullName}] is outside the plugin's root directory.",
                    nameof(relativePath));
            }
        }
    }

    private bool IsInsideRoot(string absolutePath)
    {
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        return string.Equals(absolutePath, _root, StringComparison.Ordinal)
            || absolutePath.StartsWith(rootWithSep, StringComparison.Ordinal);
    }
}
