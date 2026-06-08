using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

[TestClass]
public sealed class KB1008ClientFilesystemAnalyzerTests
{
    [TestMethod]
    public async Task FileReadAllText_ProducesKB1008()
    {
        var source = """
            using System.IO;
            namespace MyGame.Client { public class C { public string M() => File.ReadAllText("x"); } }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1008ClientFilesystemAnalyzer>(
            source, WasmAnalyzerOptions.Client, "KB1008", "System.IO.File");
    }

    [TestMethod]
    public async Task NewFileStream_ProducesKB1008()
    {
        var source = """
            using System.IO;
            namespace MyGame.Client { public class C { public Stream M() => new FileStream("x", FileMode.Open); } }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1008ClientFilesystemAnalyzer>(
            source, WasmAnalyzerOptions.Client, "KB1008", "System.IO.FileStream");
    }

    [TestMethod]
    public async Task StreamReaderFromPath_ProducesKB1008()
    {
        var source = """
            using System.IO;
            namespace MyGame.Client { public class C { public StreamReader M() => new StreamReader("x"); } }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1008ClientFilesystemAnalyzer>(
            source, WasmAnalyzerOptions.Client, "KB1008", "System.IO.StreamReader");
    }

    [TestMethod]
    public async Task MemoryStream_ProducesNoDiagnostic()
    {
        var source = """
            using System.IO;
            namespace MyGame.Client { public class C { public Stream M() => new MemoryStream(); } }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1008ClientFilesystemAnalyzer>(
            source, WasmAnalyzerOptions.Client);
    }

    [TestMethod]
    public async Task ServerProject_DoesNotFire()
    {
        // Server-side System.IO is KB1001's job, not KB1008's.
        var source = """
            using System.IO;
            namespace MyGame.Client { public class C { public string M() => File.ReadAllText("x"); } }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1008ClientFilesystemAnalyzer>(
            source, WasmAnalyzerOptions.Server);
    }

    [TestMethod]
    public async Task PragmaWarningDisable_SuppressesKB1008()
    {
        var source = """
            using System.IO;
            namespace MyGame.Client
            {
                public class C
                {
                    public string M()
                    {
            #pragma warning disable KB1008
                        return File.ReadAllText("x");
            #pragma warning restore KB1008
                    }
                }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1008ClientFilesystemAnalyzer>(
            source, WasmAnalyzerOptions.Client);
    }
}
