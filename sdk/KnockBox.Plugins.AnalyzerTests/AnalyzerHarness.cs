using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.AnalyzerTests;

/// <summary>
/// Minimal in-process Roslyn harness. Compiles a source snippet against the
/// current test runtime's trusted-platform-assemblies reference set, runs one
/// analyzer, and returns the diagnostics that analyzer emitted. Deliberately
/// simpler than <c>Microsoft.CodeAnalysis.Testing</c> — we just need to know
/// whether a given rule fired on a given snippet.
/// </summary>
internal static class AnalyzerHarness
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // TRUSTED_PLATFORM_ASSEMBLIES is the BCL + framework reference set the
        // current runtime resolves against. Using it gives analyzer test code
        // the same reference surface as the .NET 10 SDK's default compilations
        // without hauling in a separate Basic.Reference.Assemblies dependency.
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    /// <summary>
    /// Compiles <paramref name="source"/> as a library and runs
    /// <typeparamref name="TAnalyzer"/> against it. Returns only the analyzer's
    /// diagnostics — compiler errors/warnings from the snippet are ignored.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync<TAnalyzer>(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTestSnippet",
            syntaxTrees: new[] { syntaxTree },
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // The gated WASM analyzers (KB1005–KB1008) read the KnockBoxPluginKind build
        // property from global analyzer config options; tests pass it through here.
        AnalyzerOptions? analyzerOptions = globalOptions is null
            ? null
            : new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new TestConfigOptionsProvider(globalOptions));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()),
            analyzerOptions);

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics;
    }

    private sealed class TestConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options;
        public TestConfigOptionsProvider(IReadOnlyDictionary<string, string> values)
            => _options = new TestConfigOptions(values);

        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class TestConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;
        public TestConfigOptions(IReadOnlyDictionary<string, string> values) => _values = values;

        public override bool TryGetValue(string key, out string value)
        {
            if (_values.TryGetValue(key, out var v))
            {
                value = v;
                return true;
            }
            value = null!;
            return false;
        }
    }

    /// <summary>
    /// Asserts that running <typeparamref name="TAnalyzer"/> on <paramref name="source"/>
    /// produces exactly one diagnostic with the expected rule id, and that its
    /// message contains every expected substring.
    /// </summary>
    public static Task AssertSingleDiagnosticAsync<TAnalyzer>(
        string source,
        string expectedRuleId,
        params string[] expectedMessageSubstrings)
        where TAnalyzer : DiagnosticAnalyzer, new()
        => AssertSingleDiagnosticAsync<TAnalyzer>(source, null, expectedRuleId, expectedMessageSubstrings);

    public static async Task AssertSingleDiagnosticAsync<TAnalyzer>(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions,
        string expectedRuleId,
        params string[] expectedMessageSubstrings)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var diagnostics = await GetDiagnosticsAsync<TAnalyzer>(source, globalOptions);

        Assert.HasCount(
            1,
            diagnostics,
            $"Expected exactly one diagnostic, got [{diagnostics.Length}]:{Environment.NewLine}" +
            string.Join(Environment.NewLine, diagnostics.Select(d => "  " + d)));

        var diagnostic = diagnostics[0];
        Assert.AreEqual(expectedRuleId, diagnostic.Id, "Unexpected diagnostic id.");

        var message = diagnostic.GetMessage();
        foreach (var substring in expectedMessageSubstrings)
            StringAssert.Contains(message, substring);
    }

    public static async Task AssertNoDiagnosticAsync<TAnalyzer>(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var diagnostics = await GetDiagnosticsAsync<TAnalyzer>(source, globalOptions);
        Assert.IsEmpty(
            diagnostics,
            $"Expected no diagnostics, got [{diagnostics.Length}]:{Environment.NewLine}" +
            string.Join(Environment.NewLine, diagnostics.Select(d => "  " + d)));
    }
}
