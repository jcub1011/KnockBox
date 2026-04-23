using System.Text;
using KnockBox.Platform.Plugins;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage for <see cref="DefaultPluginStorage"/>'s path-traversal invariants.
/// Symlink-specific tests live alongside these once Phase 2 hardens
/// <c>Resolve</c> to follow reparse points.
/// </summary>
[TestClass]
public sealed class DefaultPluginStorageTests
{
    private static string MakeRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "DefaultPluginStorageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort */ }
    }

    // ─── Round-trip ─────────────────────────────────────────────────────────

    [TestMethod]
    public void OpenWriteThenOpenRead_RoundTripsPayload()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            using (var write = storage.OpenWrite("hello.txt"))
            {
                var bytes = Encoding.UTF8.GetBytes("world");
                write.Write(bytes, 0, bytes.Length);
            }

            using var read = storage.OpenRead("hello.txt");
            using var sr = new StreamReader(read);
            Assert.AreEqual("world", sr.ReadToEnd());
        }
        finally { SafeDelete(root); }
    }

    [TestMethod]
    public void OpenWrite_NestedRelativePath_CreatesParentDirectories()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            using (var write = storage.OpenWrite("a/b/c.txt"))
                write.WriteByte(0x42);

            Assert.IsTrue(Directory.Exists(Path.Combine(root, "a", "b")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "a", "b", "c.txt")));
        }
        finally { SafeDelete(root); }
    }

    // ─── Rejection: rooted / absolute ───────────────────────────────────────

    [TestMethod]
    public void OpenRead_UnixAbsolutePath_Throws()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentException>(() => storage.OpenRead("/etc/passwd"));
        }
        finally { SafeDelete(root); }
    }

    [TestMethod]
    public void OpenRead_WindowsRootedPath_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only path-rooted shape.");
            return;
        }

        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentException>(() => storage.OpenRead(@"C:\Windows\System32\drivers\etc\hosts"));
        }
        finally { SafeDelete(root); }
    }

    // ─── Rejection: dot-dot escape ──────────────────────────────────────────

    [TestMethod]
    public void OpenRead_DotDotEscape_Throws()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentException>(() => storage.OpenRead("../../secret.txt"));
        }
        finally { SafeDelete(root); }
    }

    [TestMethod]
    public void OpenWrite_DotDotThatStaysInsideRoot_IsAccepted()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            // foo/../bar.txt resolves lexically to <root>/bar.txt — inside root.
            using (var write = storage.OpenWrite("foo/../bar.txt"))
                write.WriteByte(0x10);

            Assert.IsTrue(File.Exists(Path.Combine(root, "bar.txt")));
        }
        finally { SafeDelete(root); }
    }

    // ─── Edge: empty string, null ───────────────────────────────────────────

    [TestMethod]
    public void Resolve_EmptyString_ResolvesToRoot_PinsCurrentBehavior()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            // Empty string currently passes the root-prefix check (equal to root).
            // File.OpenRead on a directory throws UnauthorizedAccessException
            // (Windows) or a related IOException — the point is it did NOT raise
            // ArgumentException, pinning that the lexical resolver considers
            // empty-relative-path inside root.
            try
            {
                using var _ = storage.OpenRead("");
                Assert.Fail("Expected an IO-related exception opening the root as a file.");
            }
            catch (UnauthorizedAccessException) { /* expected on Windows */ }
            catch (IOException) { /* expected on other platforms or a directory-is-not-file */ }
        }
        finally { SafeDelete(root); }
    }

    [TestMethod]
    public void Resolve_NullRelativePath_ThrowsArgumentNullException()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentNullException>(() => storage.OpenRead(null!));
        }
        finally { SafeDelete(root); }
    }

    // ─── EnumerateFiles ─────────────────────────────────────────────────────

    [TestMethod]
    public void EnumerateFiles_ReturnsForwardSlashRelativePathsRecursively()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            using (storage.OpenWrite("top.txt")) { }
            using (storage.OpenWrite("sub/inner.txt")) { }
            using (storage.OpenWrite("sub/deep/leaf.txt")) { }

            var results = storage.EnumerateFiles(relativeDir: "", searchPattern: "*")
                .OrderBy(s => s)
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { "sub/deep/leaf.txt", "sub/inner.txt", "top.txt" },
                results);
        }
        finally { SafeDelete(root); }
    }

    [TestMethod]
    public void EnumerateFiles_NonexistentRelativeDir_YieldsEmpty()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            var results = storage.EnumerateFiles("never/existed", "*").ToArray();

            Assert.AreEqual(0, results.Length);
        }
        finally { SafeDelete(root); }
    }

    // ─── Delete / Exists ────────────────────────────────────────────────────

    [TestMethod]
    public void Delete_NonexistentRelativePath_IsNoOp()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            // Does not throw.
            storage.Delete("nothing-here.txt");
        }
        finally { SafeDelete(root); }
    }

    [TestMethod]
    public void Exists_RelativePathThatEscapesRoot_Throws_PinsCurrentBehavior()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            // Resolve() runs before File.Exists(), so escape-attempts fail fast
            // with ArgumentException rather than returning false. If Phase 2
            // changes this to return false instead, update this test.
            Assert.Throws<ArgumentException>(() => storage.Exists("../../escape.txt"));
        }
        finally { SafeDelete(root); }
    }

    [TestMethod]
    public void Exists_LegitimatePath_ReturnsExpectedResult()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            Assert.IsFalse(storage.Exists("missing.txt"));

            using (storage.OpenWrite("missing.txt")) { }

            Assert.IsTrue(storage.Exists("missing.txt"));
        }
        finally { SafeDelete(root); }
    }
}
