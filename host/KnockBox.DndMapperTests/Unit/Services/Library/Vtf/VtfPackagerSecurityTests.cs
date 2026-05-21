using System.IO.Compression;
using System.Text;
using System.Text.Json;
using KnockBox.DndMapper.Services.Library.Vtf;

namespace KnockBox.DndMapperTests.Unit.Services.Library.Vtf
{
    /// <summary>
    /// Negative-case guards for <see cref="VtfPackager.Unpack"/>: zip-slip
    /// rejection (the spec §2 Security Mandate) and version-skew rejection.
    /// These tests intentionally bypass <c>Pack</c> to hand-craft malicious or
    /// malformed archives — Pack is well-behaved by construction; the risk is
    /// in Unpack accepting untrusted input.
    /// </summary>
    [TestClass]
    public class VtfPackagerSecurityTests
    {
        // ── Zip-slip path rejection ───────────────────────────────────────────

        [TestMethod]
        public void Unpack_EntryWithDoubleDot_Throws()
        {
            using var ms = BuildArchiveWithEntry("../escape.json", "{}");
            ms.Position = 0;
            Assert.ThrowsExactly<InvalidDataException>(() => VtfPackager.Unpack(ms));
        }

        [TestMethod]
        public void Unpack_EntryWithNestedDoubleDot_Throws()
        {
            using var ms = BuildArchiveWithEntry("scenes/../../../etc/passwd", "{}");
            ms.Position = 0;
            Assert.ThrowsExactly<InvalidDataException>(() => VtfPackager.Unpack(ms));
        }

        [TestMethod]
        public void Unpack_EntryWithBackslash_Throws()
        {
            // Spec §2 mandates forward slashes only. A backslash in an entry
            // name is a Windows-shaped traversal attempt.
            using var ms = BuildArchiveWithEntry("scenes\\map.json", "{}");
            ms.Position = 0;
            Assert.ThrowsExactly<InvalidDataException>(() => VtfPackager.Unpack(ms));
        }

        [TestMethod]
        public void Unpack_EntryWithAbsoluteUnixPath_Throws()
        {
            using var ms = BuildArchiveWithEntry("/etc/shadow", "{}");
            ms.Position = 0;
            Assert.ThrowsExactly<InvalidDataException>(() => VtfPackager.Unpack(ms));
        }

        [TestMethod]
        public void Unpack_EntryWithWindowsDriveLetter_Throws()
        {
            using var ms = BuildArchiveWithEntry("C:/Windows/evil.exe", "{}");
            ms.Position = 0;
            Assert.ThrowsExactly<InvalidDataException>(() => VtfPackager.Unpack(ms));
        }

        // ── Direct sanitizer probe ────────────────────────────────────────────

        [TestMethod]
        public void IsSafeRelativePath_RejectsKnownBadInputs()
        {
            Assert.IsFalse(VtfPackager.IsSafeRelativePath(""));
            Assert.IsFalse(VtfPackager.IsSafeRelativePath("   "));
            Assert.IsFalse(VtfPackager.IsSafeRelativePath("/abs/path"));
            Assert.IsFalse(VtfPackager.IsSafeRelativePath("C:/win"));
            Assert.IsFalse(VtfPackager.IsSafeRelativePath("a\\b"));
            Assert.IsFalse(VtfPackager.IsSafeRelativePath("../escape"));
            Assert.IsFalse(VtfPackager.IsSafeRelativePath("a/../b"));
            Assert.IsFalse(VtfPackager.IsSafeRelativePath("./a/b"));
            Assert.IsFalse(VtfPackager.IsSafeRelativePath("a/./b"));
        }

        [TestMethod]
        public void IsSafeRelativePath_AcceptsExpectedShapes()
        {
            Assert.IsTrue(VtfPackager.IsSafeRelativePath("manifest.json"));
            Assert.IsTrue(VtfPackager.IsSafeRelativePath("scenes/abc-123.json"));
            Assert.IsTrue(VtfPackager.IsSafeRelativePath("assets/images/" + Guid.NewGuid().ToString("D") + ".png"));
            Assert.IsTrue(VtfPackager.IsSafeRelativePath("extensions/knockbox_dnd_mapper.json"));
        }

        // ── Manifest version rejection ────────────────────────────────────────

        [TestMethod]
        public void Unpack_MissingManifest_Throws()
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                // global_state but no manifest.
                AddJsonEntry(zip, "global_state.json", "{}");
            }
            ms.Position = 0;
            Assert.ThrowsExactly<InvalidDataException>(() => VtfPackager.Unpack(ms));
        }

        [TestMethod]
        public void Unpack_UnrecognizedVtfVersion_Throws()
        {
            using var ms = BuildArchiveWithManifestVersion("not-a-version");
            ms.Position = 0;
            Assert.ThrowsExactly<InvalidDataException>(() => VtfPackager.Unpack(ms));
        }

        [TestMethod]
        public void Unpack_MajorVersionTooNew_Throws()
        {
            using var ms = BuildArchiveWithManifestVersion("2.0.0");
            ms.Position = 0;
            Assert.ThrowsExactly<InvalidDataException>(() => VtfPackager.Unpack(ms));
        }

        [TestMethod]
        public void Unpack_MinorVersionInSameMajor_Succeeds()
        {
            // 1.99.0 is within the supported major; should parse without
            // throwing — forward-compatible within v1.x.
            using var ms = BuildArchiveWithManifestVersion("1.99.0");
            ms.Position = 0;
            var result = VtfPackager.Unpack(ms);
            Assert.IsNotNull(result);
            Assert.AreEqual("Imported slot", result.SlotTitle); // default fallback when title is blank
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static MemoryStream BuildArchiveWithEntry(string entryName, string entryContent)
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Always include a valid manifest so the failure is unambiguously
                // about the malicious entry, not the missing manifest.
                AddJsonEntry(zip, "manifest.json", MinimalManifest("1.0.0"));
                var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(entryContent);
                s.Write(bytes, 0, bytes.Length);
            }
            return ms;
        }

        private static MemoryStream BuildArchiveWithManifestVersion(string version)
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddJsonEntry(zip, "manifest.json", MinimalManifest(version));
                AddJsonEntry(zip, "global_state.json", "{}");
            }
            return ms;
        }

        private static void AddJsonEntry(ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var s = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }

        private static string MinimalManifest(string version) =>
            JsonSerializer.Serialize(new
            {
                vtfVersion = version,
                campaign = new { id = Guid.NewGuid().ToString("D"), title = "", lastModified = DateTime.UtcNow },
                system = new { core = "dnd5e" },
                dependencies = Array.Empty<object>(),
            });
    }
}
