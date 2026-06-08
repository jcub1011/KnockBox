using System;
using Microsoft.CodeAnalysis;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// Shared symbol-name helpers for the WASM client/server boundary analyzers
/// (KB1005–KB1008). Centralizes fully-qualified name formatting and the
/// "server-only type" / "*.Contracts type" classification so the four rules agree.
/// </summary>
internal static class AnalyzerTypeNames
{
    /// <summary>
    /// Fully-qualified, no special-type aliasing (so <c>System.String</c> stays
    /// <c>System.String</c>, not <c>string</c>) and no generic arguments (so a
    /// constructed generic's name collapses to its open-generic full name).
    /// </summary>
    private static readonly SymbolDisplayFormat FullyQualified = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string FullName(ISymbol symbol)
    {
        var name = symbol.ToDisplayString(FullyQualified);
        return name.StartsWith("global::", StringComparison.Ordinal)
            ? name.Substring("global::".Length)
            : name;
    }

    private static string NamespaceOf(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? string.Empty : ns.ToDisplayString();
    }

    /// <summary>
    /// True when <paramref name="type"/> is a server-only KnockBox type a WASM
    /// <c>.Client</c> assembly must not reference: anything under
    /// <c>KnockBox.Core.*</c> (except the WASM-safe <c>KnockBox.Core.Client.*</c>
    /// tree) or <c>KnockBox.Platform.*</c>, plus the explicitly-server WordService
    /// oracle (which lives in a <c>.Contracts</c> assembly but must stay server-side).
    /// </summary>
    public static bool IsServerOnlyType(ITypeSymbol type)
    {
        if (FullName(type) == "KnockBox.WordService.Contracts.IWordListService")
            return true;

        var ns = NamespaceOf(type);

        if (ns == "KnockBox.Core" || ns.StartsWith("KnockBox.Core.", StringComparison.Ordinal))
        {
            // The WASM-safe client SDK is the one allowed KnockBox.Core.* subtree.
            if (ns == "KnockBox.Core.Client" || ns.StartsWith("KnockBox.Core.Client.", StringComparison.Ordinal))
                return false;
            return true;
        }

        return ns == "KnockBox.Platform" || ns.StartsWith("KnockBox.Platform.", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="type"/> looks like a shared-contract DTO: it lives
    /// in an assembly whose simple name ends in <c>.Contracts</c>, or (a fallback
    /// for same-assembly test fixtures) in a namespace with a <c>Contracts</c>
    /// segment.
    /// </summary>
    public static bool IsContractsType(ITypeSymbol type)
    {
        var assembly = type.ContainingAssembly?.Name ?? string.Empty;
        if (assembly.EndsWith(".Contracts", StringComparison.Ordinal))
            return true;

        foreach (var segment in NamespaceOf(type).Split('.'))
        {
            if (string.Equals(segment, "Contracts", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
