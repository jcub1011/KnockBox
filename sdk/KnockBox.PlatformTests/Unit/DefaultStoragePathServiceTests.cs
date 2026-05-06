using KnockBox.Platform.Storage;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage for <see cref="DefaultStoragePathService"/>'s data-root resolution.
/// The class is the single chokepoint for every persisted path the host
/// writes (admin settings, rolling logs, per-plugin storage, third-party
/// plugin drop), so the env-var override has to land each subtree somewhere
/// reachable while keeping first-party plugins anchored to the install dir.
///
/// Resolution is exercised through the pure <c>ResolveDataRoot(string?)</c>
/// helper — calling <c>Environment.GetEnvironmentVariable</c> in tests would
/// race with <see cref="ExecutionScope.MethodLevel"/> parallelism.
/// </summary>
[TestClass]
public sealed class DefaultStoragePathServiceTests
{
    [TestMethod]
    public void ResolveDataRoot_NullEnv_FallsBackToBaseDirectoryData()
    {
        var resolved = DefaultStoragePathService.ResolveDataRoot(null);
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "data"), resolved);
    }

    [TestMethod]
    public void ResolveDataRoot_EmptyEnv_FallsBackToBaseDirectoryData()
    {
        var resolved = DefaultStoragePathService.ResolveDataRoot(string.Empty);
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "data"), resolved);
    }

    [TestMethod]
    public void ResolveDataRoot_WhitespaceEnv_FallsBackToBaseDirectoryData()
    {
        var resolved = DefaultStoragePathService.ResolveDataRoot("   ");
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "data"), resolved);
    }

    [TestMethod]
    public void ResolveDataRoot_AbsoluteEnv_UsedAsRoot()
    {
        var target = Path.Combine(Path.GetTempPath(), "kb-test-" + Guid.NewGuid().ToString("N"));
        var resolved = DefaultStoragePathService.ResolveDataRoot(target);
        Assert.AreEqual(Path.GetFullPath(target), resolved);
    }

    [TestMethod]
    public void ResolveDataRoot_TrailingSeparators_Stripped()
    {
        var target = Path.Combine(Path.GetTempPath(), "kb-test-" + Guid.NewGuid().ToString("N"));
        var withTrailing = target + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar;

        var resolved = DefaultStoragePathService.ResolveDataRoot(withTrailing);

        Assert.AreEqual(Path.GetFullPath(target), resolved);
    }

    [TestMethod]
    public void ResolveDataRoot_RelativeEnv_NormalisedToAbsolute()
    {
        var resolved = DefaultStoragePathService.ResolveDataRoot("./relative-data");
        Assert.IsTrue(Path.IsPathRooted(resolved),
            "Relative overrides must be normalised before storage so a working-directory change mid-process does not silently relocate files.");
    }

    [TestMethod]
    public void GetFirstPartyPluginsDirectory_AlwaysAtBaseDirectory()
    {
        // First-party plugins ship inside the deployed artifact; relocating
        // them under the data root would leave the host unable to find any
        // games until an operator copied them. The constructor reads the
        // live env var, but the first-party path must ignore it regardless.
        var service = new DefaultStoragePathService();
        Assert.AreEqual(
            Path.Combine(AppContext.BaseDirectory, "games"),
            service.GetFirstPartyPluginsDirectory());
    }
}
