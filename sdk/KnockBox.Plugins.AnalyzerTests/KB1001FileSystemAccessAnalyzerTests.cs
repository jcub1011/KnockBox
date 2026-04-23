using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

[TestClass]
public sealed class KB1001FileSystemAccessAnalyzerTests
{
    // ─── Flagged APIs ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task FileReadAllText_ProducesKB1001()
    {
        var source = """
            using System.IO;
            public class C { public string M() => File.ReadAllText("x"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(
            source, "KB1001", "System.IO.File");
    }

    [TestMethod]
    public async Task NewFileStream_ProducesKB1001()
    {
        var source = """
            using System.IO;
            public class C { public Stream M() => new FileStream("x", FileMode.Open); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(
            source, "KB1001", "System.IO.FileStream");
    }

    [TestMethod]
    public async Task NewDirectoryInfo_ProducesKB1001()
    {
        var source = """
            using System.IO;
            public class C { public DirectoryInfo M() => new DirectoryInfo("x"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(
            source, "KB1001", "System.IO.DirectoryInfo");
    }

    [TestMethod]
    public async Task DirectoryEnumerateFiles_ProducesKB1001()
    {
        var source = """
            using System.IO;
            using System.Collections.Generic;
            public class C { public IEnumerable<string> M() => Directory.EnumerateFiles("x"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(
            source, "KB1001", "System.IO.Directory");
    }

    // ─── Exempt constructs ──────────────────────────────────────────────────

    [TestMethod]
    public async Task NewMemoryStream_ProducesNoDiagnostic()
    {
        var source = """
            using System.IO;
            public class C { public Stream M() => new MemoryStream(); }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(source);
    }

    [TestMethod]
    public async Task StreamReaderOverExistingStream_ProducesNoDiagnostic()
    {
        // The (Stream)-accepting ctor is the common "wrap the stream returned
        // by IPluginStorage" pattern and is deliberately exempt.
        var source = """
            using System.IO;
            public class C { public string M(Stream s) { using var r = new StreamReader(s); return r.ReadToEnd(); } }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(source);
    }

    [TestMethod]
    public async Task StreamWriterOverExistingStream_ProducesNoDiagnostic()
    {
        var source = """
            using System.IO;
            public class C { public void M(Stream s) { using var w = new StreamWriter(s); w.WriteLine("x"); } }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(source);
    }

    [TestMethod]
    public async Task PathCombine_ProducesNoDiagnostic()
    {
        // Path.* is pure string manipulation; not in the ban list.
        var source = """
            using System.IO;
            public class C { public string M() => Path.Combine("a", "b"); }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(source);
    }

    // ─── StreamReader/StreamWriter path-ctors ───────────────────────────────

    [TestMethod]
    public async Task NewStreamReaderOverPath_ProducesKB1001()
    {
        // The (string)-accepting ctor opens a file by path, same as File.Open*.
        var source = """
            using System.IO;
            public class C { public string M() { using var r = new StreamReader("x"); return r.ReadToEnd(); } }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(
            source, "KB1001", "System.IO.StreamReader");
    }

    [TestMethod]
    public async Task NewStreamWriterOverPath_ProducesKB1001()
    {
        var source = """
            using System.IO;
            public class C { public void M() { using var w = new StreamWriter("x"); w.WriteLine("y"); } }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(
            source, "KB1001", "System.IO.StreamWriter");
    }

    // ─── Suppression ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PragmaWarningDisable_SuppressesKB1001()
    {
        var source = """
            using System.IO;
            public class C {
                public string M() {
            #pragma warning disable KB1001
                    return File.ReadAllText("x");
            #pragma warning restore KB1001
                }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(source);
    }
}
