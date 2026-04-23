using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// Shared scaffolding for the sandbox-escape analyzers (KB1001–KB1004). Each
/// concrete analyzer contributes its own diagnostic descriptor plus up to
/// three lookup sets: fully-qualified type names whose <b>every</b> member is
/// flagged, fully-qualified <c>Type.Member</c> names flagged individually,
/// and a per-type first-constructor-parameter set used when a type has one
/// legitimate ctor overload (e.g. <c>StreamReader(Stream)</c>) and one
/// sandbox-escaping overload (<c>StreamReader(string)</c>). The base observes
/// object creations, method invocations, and property/field references —
/// covering both instance construction (<c>new HttpClient()</c>) and static
/// dispatch (<c>Environment.MachineName</c>).
/// </summary>
public abstract class SandboxAnalyzerBase : DiagnosticAnalyzer
{
    /// <summary>The rule this analyzer reports.</summary>
    protected abstract DiagnosticDescriptor Rule { get; }

    /// <summary>
    /// Fully-qualified type names whose every member access is flagged.
    /// Keyed by the type's display name from <see cref="FullyQualifiedNoSpecialTypes"/>
    /// after stripping the leading <c>global::</c>.
    /// </summary>
    protected abstract ImmutableHashSet<string> BannedTypes { get; }

    /// <summary>
    /// Fully-qualified <c>Type.Member</c> names flagged individually. Used when
    /// a type has legitimate uses (e.g. <c>Environment.NewLine</c>) mixed with
    /// banned ones (e.g. <c>Environment.GetEnvironmentVariable</c>).
    /// </summary>
    protected virtual ImmutableHashSet<string> BannedMembers => ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Type full name → set of first-parameter type full names that trigger
    /// the rule when seen in an <see cref="IObjectCreationOperation"/>. Only
    /// consulted when <see cref="BannedTypes"/> does not already match.
    /// Zero-arg ctors are matched via the sentinel key
    /// <see cref="EmptyFirstParameterSentinel"/>.
    /// </summary>
    protected virtual ImmutableDictionary<string, ImmutableHashSet<string>> BannedCtorFirstParameters =>
        ImmutableDictionary<string, ImmutableHashSet<string>>.Empty;

    /// <summary>
    /// Sentinel <c>first-parameter</c> value matching a zero-argument ctor, so
    /// <see cref="BannedCtorFirstParameters"/> can express "ban the parameterless ctor".
    /// </summary>
    protected const string EmptyFirstParameterSentinel = "<none>";

    /// <summary>
    /// <see cref="FullyQualifiedNoSpecialTypes"/> minus
    /// <see cref="SymbolDisplayMiscellaneousOptions.UseSpecialTypes"/>. The
    /// default format aliases <c>System.String</c> to <c>string</c> etc., which
    /// would silently break lookups keyed by the BCL type full name.
    /// </summary>
    private static readonly SymbolDisplayFormat FullyQualifiedNoSpecialTypes = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public sealed override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // Analyze needs the instance's Rule/BannedTypes/etc, so it captures `this`.
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
            containingType.ToDisplayString(FullyQualifiedNoSpecialTypes));

        string? flaggedName = null;

        if (BannedTypes.Contains(typeFullName))
        {
            flaggedName = typeFullName;
        }
        else if (context.Operation is IObjectCreationOperation ctorOp
                 && BannedCtorFirstParameters.TryGetValue(typeFullName, out var bannedFirstParams)
                 && MatchesBannedCtorFirstParameter(ctorOp, bannedFirstParams))
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

    private static bool MatchesBannedCtorFirstParameter(
        IObjectCreationOperation ctorOp,
        ImmutableHashSet<string> bannedFirstParams)
    {
        var ctor = ctorOp.Constructor;
        if (ctor is null)
            return false;

        if (ctor.Parameters.Length == 0)
            return bannedFirstParams.Contains(EmptyFirstParameterSentinel);

        var firstParamTypeName = StripGlobalPrefix(
            ctor.Parameters[0].Type.ToDisplayString(FullyQualifiedNoSpecialTypes));
        return bannedFirstParams.Contains(firstParamTypeName);
    }

    private static string StripGlobalPrefix(string fullyQualified) =>
        fullyQualified.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualified.Substring("global::".Length)
            : fullyQualified;
}
