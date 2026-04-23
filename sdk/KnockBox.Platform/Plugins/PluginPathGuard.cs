namespace KnockBox.Platform.Plugins;

/// <summary>
/// Lexical + reparse-point containment checks shared between per-plugin storage
/// (<c>DefaultPluginStorage</c>) and the static-file mount in
/// <c>MapPluginStaticAssets</c>. Every check here is advisory: it is inherently
/// non-atomic and racey against filesystem changes, so treat it as a contract
/// boundary, not a security boundary.
/// </summary>
internal static class PluginPathGuard
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="absolutePath"/> is either equal
    /// to <paramref name="root"/> or nested strictly below it on disk. Both
    /// arguments must already be full paths.
    /// </summary>
    public static bool IsInsideRoot(string root, string absolutePath)
    {
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return string.Equals(absolutePath, root, StringComparison.Ordinal)
            || absolutePath.StartsWith(rootWithSep, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks every existing path segment from <paramref name="root"/> down to
    /// <paramref name="candidate"/>, returning <c>false</c> if any segment is a
    /// reparse point (symlink / NTFS junction) whose final target lives outside
    /// <paramref name="root"/>. Non-existent segments terminate the walk
    /// without failing — a file that doesn't exist yet cannot be a reparse
    /// point. On success writes <c>null</c> to <paramref name="reason"/>.
    /// </summary>
    /// <remarks>
    /// Precondition: <paramref name="candidate"/> must already be lexically
    /// inside <paramref name="root"/>. Callers should verify that with
    /// <see cref="IsInsideRoot"/> first.
    /// </remarks>
    public static bool HasNoReparsePointEscape(string root, string candidate, out string? reason)
    {
        var relativeFromRoot = Path.GetRelativePath(root, candidate);
        var segments = relativeFromRoot.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var currentPath = root;
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
                reason = null;
                return true;
            }
            catch (DirectoryNotFoundException)
            {
                reason = null;
                return true;
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
                reason = $"reparse-point segment [{currentPath}] target could not be resolved: {ex.Message}";
                return false;
            }

            if (resolved is null)
            {
                reason = $"reparse-point segment [{currentPath}] target could not be resolved.";
                return false;
            }

            if (!IsInsideRoot(root, resolved.FullName))
            {
                reason = $"reparse-point segment [{currentPath}] resolves to [{resolved.FullName}], which is outside the root.";
                return false;
            }
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Resolves <paramref name="directory"/> to its final on-disk target:
    /// full path first, then — if the directory itself is a reparse point —
    /// follows it to the final target. Returns the original full path if the
    /// directory doesn't exist or the link can't be resolved. Operators on
    /// macOS (<c>/var</c> → <c>/private/var</c>), Docker bind mounts, and
    /// similar setups get the expected behavior without a manual resolve.
    /// </summary>
    public static string NormalizeDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
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
}
