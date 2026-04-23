using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// Shared scaffolding for the sandbox-escape analyzers (KB1001–KB1004). Each
/// concrete analyzer contributes its own diagnostic descriptor plus two
/// lookup sets: fully-qualified type names whose <b>every</b> member is
/// flagged, and fully-qualified <c>Type.Member</c> names flagged
/// individually. The base observes object creations, method invocations, and
/// property/field references — covering both instance construction
/// (<c>new HttpClient()</c>) and static dispatch (<c>Environment.MachineName</c>).
/// </summary>
public abstract class SandboxAnalyzerBase : DiagnosticAnalyzer
{
    /// <summary>The rule this analyzer reports.</summary>
    protected abstract DiagnosticDescriptor Rule { get; }

    /// <summary>
    /// Fully-qualified type names whose every member access is flagged.
    /// Keyed by the type's display name from <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>
    /// after stripping the leading <c>global::</c>.
    /// </summary>
    protected abstract ImmutableHashSet<string> BannedTypes { get; }

    /// <summary>
    /// Fully-qualified <c>Type.Member</c> names flagged individually. Used when
    /// a type has legitimate uses (e.g. <c>Environment.NewLine</c>) mixed with
    /// banned ones (e.g. <c>Environment.GetEnvironmentVariable</c>).
    /// </summary>
    protected virtual ImmutableHashSet<string> BannedMembers => ImmutableHashSet<string>.Empty;

    public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public sealed override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(
            Analyze,
            OperationKind.ObjectCreation,
            OperationKind.Invocation,
            OperationKind.PropertyReference,
            OperationKind.FieldReference);
    }

    private void Analyze(OperationAnalysisContext context)
    {
        ISymbol? targetSymbol = context.Operation switch
        {
            IObjectCreationOperation o => o.Constructor,
            IInvocationOperation i => i.TargetMethod,
            IPropertyReferenceOperation p => p.Property,
            IFieldReferenceOperation f => f.Field,
            _ => null,
        };

        if (targetSymbol is null)
            return;

        var containingType = targetSymbol.ContainingType;
        if (containingType is null)
            return;

        var typeFullName = StripGlobalPrefix(
            containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        string? flaggedName = null;
        if (BannedTypes.Contains(typeFullName))
        {
            flaggedName = typeFullName;
        }
        else if (!BannedMembers.IsEmpty)
        {
            var memberFullName = typeFullName + "." + targetSymbol.Name;
            if (BannedMembers.Contains(memberFullName))
                flaggedName = memberFullName;
        }

        if (flaggedName is not null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(), flaggedName));
        }
    }

    private static string StripGlobalPrefix(string fullyQualified) =>
        fullyQualified.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualified.Substring("global::".Length)
            : fullyQualified;
}
