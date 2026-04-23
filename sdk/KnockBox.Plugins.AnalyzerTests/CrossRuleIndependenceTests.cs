using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

/// <summary>
/// Guards against a refactor that accidentally unions ban lists across the
/// four sandbox analyzers. Each rule's positive case should produce zero
/// diagnostics when run under any other rule's analyzer.
/// </summary>
[TestClass]
public sealed class CrossRuleIndependenceTests
{
    private const string Kb1001PositiveSource = """
        using System.IO;
        public class C { public string M() => File.ReadAllText("x"); }
        """;

    private const string Kb1002PositiveSource = """
        using System.Net.Http;
        public class C { public HttpClient M() => new HttpClient(); }
        """;

    private const string Kb1003PositiveSource = """
        using System.Diagnostics;
        public class C { public Process? M() => Process.Start("ls"); }
        """;

    private const string Kb1004PositiveSource = """
        using System;
        public class C { public string? M() => Environment.GetEnvironmentVariable("PATH"); }
        """;

    [TestMethod]
    public async Task KB1001_DoesNotFireOnOtherRulesPositiveCases()
    {
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(Kb1002PositiveSource);
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(Kb1003PositiveSource);
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1001FileSystemAccessAnalyzer>(Kb1004PositiveSource);
    }

    [TestMethod]
    public async Task KB1002_DoesNotFireOnOtherRulesPositiveCases()
    {
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1002HttpAnalyzer>(Kb1001PositiveSource);
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1002HttpAnalyzer>(Kb1003PositiveSource);
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1002HttpAnalyzer>(Kb1004PositiveSource);
    }

    [TestMethod]
    public async Task KB1003_DoesNotFireOnOtherRulesPositiveCases()
    {
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1003ProcessAnalyzer>(Kb1001PositiveSource);
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1003ProcessAnalyzer>(Kb1002PositiveSource);
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1003ProcessAnalyzer>(Kb1004PositiveSource);
    }

    [TestMethod]
    public async Task KB1004_DoesNotFireOnOtherRulesPositiveCases()
    {
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1004EnvironmentAnalyzer>(Kb1001PositiveSource);
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1004EnvironmentAnalyzer>(Kb1002PositiveSource);
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1004EnvironmentAnalyzer>(Kb1003PositiveSource);
    }
}
