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
    public void Resolve_EmptyString_Throws()
    {
        var root = MakeRoot();
        try
        {
            var storage = new DefaultPluginStorage(root);

            Assert.Throws<ArgumentException>(() => storage.OpenRead(""));
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

            Assert.IsEmpty(results);
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

    [TestMethod]
    public void Ctor_WhenRootIsSymlink_NormalizesToFinalTarget()
    {
        // Pins _root normalization: if an operator hands in a symlinked path
        // (e.g. macOS /var → /private/var, Docker bind mounts), the storage
        // service resolves it to the final target so downstream in-root checks
        // are against the canonical form. Writes through the symlinked path
        // must land in the final-target directory.
        var workspace = MakeRoot();
        var realRoot = Path.Combine(workspace, "real");
        Directory.CreateDirectory(realRoot);

        var linkedRoot = Path.Combine(workspace, "linked");
        try
        {
            if (!TryCreateSymlink(linkedRoot, realRoot, linkTargetIsDirectory: true))
            {
                Assert.Inconclusive("Creating symlinks is not permitted on this platform/session.");
                return;
            }

            var storage = new DefaultPluginStorage(linkedRoot);

            using (var w = storage.OpenWrite("hello.txt"))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes("normalized");
                w.Write(bytes, 0, bytes.Length);
            }

            // File must exist at the real target, not through the symlink alias.
            Assert.IsTrue(File.Exists(Path.Combine(realRoot, "hello.txt")));

            using var r = storage.OpenRead("hello.txt");
            using var sr = new StreamReader(r);
            Assert.AreEqual("normalized", sr.ReadToEnd());
        }
        finally { SafeDelete(workspace); }
    }

    [TestMethod]
    public void OpenRead_SymlinkChainEndingInsideRoot_IsAccepted()
    {
        // Pins "only the FINAL target matters" behavior from
        // ResolveLinkTarget(returnFinalTarget: true). A link that transits
        // outside and comes back in is accepted — the resolver collapses the
        // chain to its endpoint, which IS inside _root.
        var workspace = MakeRoot();
        var root = Path.Combine(workspace, "root");
        Directory.CreateDirectory(root);
        var realInside = Path.Combine(root, "real");
        Directory.CreateDirectory(realInside);
        File.WriteAllText(Path.Combine(realInside, "target.txt"), "reachable");

        // link1 (inside root) -> link2 (outside root) -> realInside (inside root)
        var link2 = Path.Combine(workspace, "link2");
        var link1 = Path.Combine(root, "link1");
        try
        {
            if (!TryCreateSymlink(link2, realInside, linkTargetIsDirectory: true))
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

            using var stream = storage.OpenRead("link1/target.txt");
            using var sr = new StreamReader(stream);
            Assert.AreEqual("reachable", sr.ReadToEnd());
        }
        finally { SafeDelete(workspace); }
    }
}
