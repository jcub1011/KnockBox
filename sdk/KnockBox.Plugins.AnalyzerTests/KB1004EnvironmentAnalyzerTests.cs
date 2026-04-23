using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

[TestClass]
public sealed class KB1004EnvironmentAnalyzerTests
{
    [TestMethod]
    public async Task EnvironmentGetEnvironmentVariable_ProducesKB1004()
    {
        var source = """
            using System;
            public class C { public string? M() => Environment.GetEnvironmentVariable("PATH"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1004EnvironmentAnalyzer>(
            source, "KB1004", "System.Environment.GetEnvironmentVariable");
    }

    [TestMethod]
    public async Task EnvironmentExpandEnvironmentVariables_ProducesKB1004()
    {
        var source = """
            using System;
            public class C { public string M() => Environment.ExpandEnvironmentVariables("%PATH%"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1004EnvironmentAnalyzer>(
            source, "KB1004", "System.Environment.ExpandEnvironmentVariables");
    }

    [TestMethod]
    public async Task EnvironmentSetEnvironmentVariable_ProducesKB1004()
    {
        var source = """
            using System;
            public class C { public void M() => Environment.SetEnvironmentVariable("X", "y"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1004EnvironmentAnalyzer>(
            source, "KB1004", "System.Environment.SetEnvironmentVariable");
    }

    [TestMethod]
    public async Task EnvironmentNewLine_ProducesNoDiagnostic()
    {
        // Environment.NewLine and similar benign readers are not banned — only
        // the environment-variable accessors are flagged.
        var source = """
            using System;
            public class C { public string M() => Environment.NewLine; }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1004EnvironmentAnalyzer>(source);
    }

    [TestMethod]
    public async Task EnvironmentProcessorCount_ProducesNoDiagnostic()
    {
        var source = """
            using System;
            public class C { public int M() => Environment.ProcessorCount; }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1004EnvironmentAnalyzer>(source);
    }

    [TestMethod]
    public async Task PragmaWarningDisable_SuppressesKB1004()
    {
        var source = """
            using System;
            public class C {
                public string? M() {
            #pragma warning disable KB1004
                    return Environment.GetEnvironmentVariable("PATH");
            #pragma warning restore KB1004
                }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1004EnvironmentAnalyzer>(source);
    }
}
