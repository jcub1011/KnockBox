using KnockBox.Platform.Plugins;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Covers <see cref="PluginPathGuard"/>. Real-reparse-point escape tests need
/// either elevated privileges (Windows) or platform-specific symlink creation;
/// those live in <c>DefaultPluginStorageTests</c> and are conditionally skipped.
/// The checks here cover the pure lexical behavior exercised by every caller.
/// </summary>
[TestClass]
public sealed class PluginPathGuardTests
{
    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pluginpathguard-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void IsInsideRoot_PathEqualToRoot_ReturnsTrue()
    {
        var root = MakeTempDir();
        try
        {
            Assert.IsTrue(PluginPathGuard.IsInsideRoot(root, root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void IsInsideRoot_NestedChild_ReturnsTrue()
    {
        var root = MakeTempDir();
        try
        {
            var child = Path.Combine(root, "sub", "file.txt");
            Assert.IsTrue(PluginPathGuard.IsInsideRoot(root, child));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void IsInsideRoot_SiblingOfRoot_ReturnsFalse()
    {
        var root = MakeTempDir();
        try
        {
            // Sibling directory — same parent as root but not under it.
            var sibling = Path.Combine(Path.GetDirectoryName(root)!, "unrelated-dir");
            Assert.IsFalse(PluginPathGuard.IsInsideRoot(root, sibling));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void IsInsideRoot_SharedPrefixButNotChild_ReturnsFalse()
    {
        // Guards against the classic "startsWith without separator" bug: the
        // sibling "/tmp/plugin-data-evil" must not be considered inside "/tmp/plugin-data".
        var root = Path.Combine(Path.GetTempPath(), "pluginpathguard-prefix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var trickySibling = root + "-evil";
            Assert.IsFalse(PluginPathGuard.IsInsideRoot(root, trickySibling));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void HasNoReparsePointEscape_PlainPathInsideRoot_ReturnsTrue()
    {
        var root = MakeTempDir();
        try
        {
            var candidate = Path.Combine(root, "data.bin");
            Assert.IsTrue(PluginPathGuard.HasNoReparsePointEscape(root, candidate, out var reason));
            Assert.IsNull(reason);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void HasNoReparsePointEscape_NonExistentSegment_ShortCircuitsAsTrue()
    {
        // OpenWrite targets often don't exist yet; the walk must stop at the
        // first non-existent segment without erroring.
        var root = MakeTempDir();
        try
        {
            var candidate = Path.Combine(root, "never", "ever", "exists", "file.bin");
            Assert.IsTrue(PluginPathGuard.HasNoReparsePointEscape(root, candidate, out _));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void NormalizeDirectory_NonExistent_ReturnsFullPath()
    {
        // Root normalization must survive the "operator pointed us at a path that doesn't exist yet" case.
        var absent = Path.Combine(Path.GetTempPath(), "pluginpathguard-absent-" + Guid.NewGuid().ToString("N"));
        var normalized = PluginPathGuard.NormalizeDirectory(absent);

        Assert.AreEqual(Path.GetFullPath(absent), normalized);
    }
}
