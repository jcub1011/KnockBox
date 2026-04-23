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

    // ─── Symlink escape hardening (Phase 2) ─────────────────────────────────
    //
    // These tests create real symlinks on disk, which requires privileged
    // access on Windows (admin terminal or Developer Mode enabled). When link
    // creation fails with the expected denial exceptions, the test is marked
    // Inconclusive rather than failed — CI runners without elevated privileges
    // still get a clean signal without pretending the hardening works.

    private static bool TryCreateSymlink(string linkPath, string targetPath, bool linkTargetIsDirectory)
    {
        try
        {
            if (linkTargetIsDirectory)
                Directory.CreateSymbolicLink(linkPath, targetPath);
            else
                File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
        catch (PlatformNotSupportedException) { return false; }
    }

    [TestMethod]
    public void OpenRead_ThroughDirectorySymlinkEscapingRoot_Throws()
    {
        var workspace = MakeRoot();
        var root = Path.Combine(workspace, "root");
        Directory.CreateDirectory(root);

        var outside = Path.Combine(workspace, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "leak");

        var link = Path.Combine(root, "link");
        try
        {
            if (!TryCreateSymlink(link, outside, linkTargetIsDirectory: true))
            {
                Assert.Inconclusive("Creating symlinks is not permitted on this platform/session.");
                return;
            }

            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentException>(() => storage.OpenRead("link/secret.txt"));
        }
        finally { SafeDelete(workspace); }
    }

    [TestMethod]
    public void OpenRead_FileSymlinkEscapingRoot_Throws()
    {
        var workspace = MakeRoot();
        var root = Path.Combine(workspace, "root");
        Directory.CreateDirectory(root);

        var outside = Path.Combine(workspace, "outside");
        Directory.CreateDirectory(outside);
        var targetFile = Path.Combine(outside, "secret.txt");
        File.WriteAllText(targetFile, "leak");

        var link = Path.Combine(root, "secret.txt");
        try
        {
            if (!TryCreateSymlink(link, targetFile, linkTargetIsDirectory: false))
            {
                Assert.Inconclusive("Creating symlinks is not permitted on this platform/session.");
                return;
            }

            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentException>(() => storage.OpenRead("secret.txt"));
        }
        finally { SafeDelete(workspace); }
    }

    [TestMethod]
    public void OpenWrite_ThroughDirectorySymlinkEscapingRoot_ThrowsEvenWhenTerminalFileDoesNotExist()
    {
        var workspace = MakeRoot();
        var root = Path.Combine(workspace, "root");
        Directory.CreateDirectory(root);

        var outside = Path.Combine(workspace, "outside");
        Directory.CreateDirectory(outside);

        var link = Path.Combine(root, "link");
        try
        {
            if (!TryCreateSymlink(link, outside, linkTargetIsDirectory: true))
            {
                Assert.Inconclusive("Creating symlinks is not permitted on this platform/session.");
                return;
            }

            var storage = new DefaultPluginStorage(root);

            // newfile.txt doesn't exist yet; the escape is via the intermediate
            // symlink, which does exist and MUST be caught before the write.
            Assert.Throws<ArgumentException>(() => storage.OpenWrite("link/newfile.txt"));
            Assert.IsFalse(File.Exists(Path.Combine(outside, "newfile.txt")),
                "Write was not rejected before it hit the symlink target.");
        }
        finally { SafeDelete(workspace); }
    }

    [TestMethod]
    public void OpenRead_SymlinkStayingInsideRoot_IsAccepted()
    {
        var workspace = MakeRoot();
        var root = Path.Combine(workspace, "root");
        Directory.CreateDirectory(root);

        var realDir = Path.Combine(root, "real");
        Directory.CreateDirectory(realDir);
        File.WriteAllText(Path.Combine(realDir, "ok.txt"), "inside");

        var link = Path.Combine(root, "link");
        try
        {
            if (!TryCreateSymlink(link, realDir, linkTargetIsDirectory: true))
            {
                Assert.Inconclusive("Creating symlinks is not permitted on this platform/session.");
                return;
            }

            var storage = new DefaultPluginStorage(root);

            using var stream = storage.OpenRead("link/ok.txt");
            using var sr = new StreamReader(stream);
            Assert.AreEqual("inside", sr.ReadToEnd());
        }
        finally { SafeDelete(workspace); }
    }

    /// <summary>
    /// Creates an NTFS directory junction via <c>mklink /J</c>. Junctions are
    /// reparse points on Windows and do NOT require administrator privileges,
    /// so this exercises the same reparse-point-resolution code path as a
    /// symlink test without relying on elevated rights or Developer Mode.
    /// </summary>
    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit();
            return proc.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch { return false; }
    }

    [TestMethod]
    public void OpenRead_ThroughDirectoryJunctionEscapingRoot_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Junctions are Windows-only.");
            return;
        }

        var workspace = MakeRoot();
        var root = Path.Combine(workspace, "root");
        Directory.CreateDirectory(root);

        var outside = Path.Combine(workspace, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "leak");

        var junction = Path.Combine(root, "junction");
        try
        {
            if (!TryCreateJunction(junction, outside))
            {
                Assert.Inconclusive("Creating a junction via mklink /J failed on this machine.");
                return;
            }

            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentException>(() => storage.OpenRead("junction/secret.txt"));
        }
        finally { SafeDelete(workspace); }
    }

    [TestMethod]
    public void OpenRead_NestedSymlinkChainEscapingRoot_Throws()
    {
        var workspace = MakeRoot();
        var root = Path.Combine(workspace, "root");
        Directory.CreateDirectory(root);

        var outside = Path.Combine(workspace, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "leak.txt"), "leak");

        // link1 -> link2 -> outside
        var link2 = Path.Combine(workspace, "link2");
        var link1 = Path.Combine(root, "link1");
        try
        {
            if (!TryCreateSymlink(link2, outside, linkTargetIsDirectory: true))
            {
                Assert.Inconclusive("Creating symlinks is not permitted on this platform/session.");
                return;
            }
            if (!TryCreateSymlink(link1, link2, linkTargetIsDirectory: true))
            {
                Assert.Inconclusive("Creating symlinks is not permitted on this platform/session.");
                return;
            }

            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentException>(() => storage.OpenRead("link1/leak.txt"));
        }
        finally { SafeDelete(workspace); }
    }
}
