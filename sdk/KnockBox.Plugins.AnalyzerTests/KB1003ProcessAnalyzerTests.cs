using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

[TestClass]
public sealed class KB1003ProcessAnalyzerTests
{
    [TestMethod]
    public async Task ProcessStart_ProducesKB1003()
    {
        var source = """
            using System.Diagnostics;
            public class C { public Process? M() => Process.Start("ls"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1003ProcessAnalyzer>(
            source, "KB1003", "System.Diagnostics.Process");
    }

    [TestMethod]
    public async Task NewProcessStartInfo_ProducesKB1003()
    {
        var source = """
            using System.Diagnostics;
            public class C { public ProcessStartInfo M() => new ProcessStartInfo("ls"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1003ProcessAnalyzer>(
            source, "KB1003", "System.Diagnostics.ProcessStartInfo");
    }

    [TestMethod]
    public async Task EnvironmentExit_ProducesKB1003()
    {
        var source = """
            using System;
            public class C { public void M() => Environment.Exit(0); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1003ProcessAnalyzer>(
            source, "KB1003", "System.Environment.Exit");
    }

    [TestMethod]
    public async Task EnvironmentFailFast_ProducesKB1003()
    {
        var source = """
            using System;
            public class C { public void M() => Environment.FailFast("bye"); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1003ProcessAnalyzer>(
            source, "KB1003", "System.Environment.FailFast");
    }

    [TestMethod]
    public async Task StopwatchStart_ProducesNoDiagnostic()
    {
        // Stopwatch lives in System.Diagnostics but isn't related to process
        // launch — this confirms we scope only to Process/ProcessStartInfo.
        var source = """
            using System.Diagnostics;
            public class C { public Stopwatch M() { var s = Stopwatch.StartNew(); return s; } }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1003ProcessAnalyzer>(source);
    }

    [TestMethod]
    public async Task PragmaWarningDisable_SuppressesKB1003()
    {
        var source = """
            using System.Diagnostics;
            public class C {
                public Process? M() {
            #pragma warning disable KB1003
                    return Process.Start("ls");
            #pragma warning restore KB1003
                }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1003ProcessAnalyzer>(source);
    }
}
