using System.Collections.Immutable;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// The outcome of validating an <see cref="AlphaChainSettings"/> record. Carries every
    /// violation so the lobby UI can surface them all at once rather than one-at-a-time.
    /// <see cref="AlphaChainSettings.Validate"/> is the single source of truth for what counts
    /// as a legal config; both the settings panel (to gate the start buttons) and
    /// <c>StartAsyncCore</c> (to refuse an illegal start) call it.
    /// </summary>
    public sealed record ConfigValidationResult(ImmutableArray<string> Violations)
    {
        /// <summary>A passing result with no violations.</summary>
        public static readonly ConfigValidationResult Valid = new(ImmutableArray<string>.Empty);

        /// <summary>True when there are no violations.</summary>
        public bool IsValid => Violations.IsDefaultOrEmpty;

        /// <summary>A single line joining every violation, for log/error surfaces.</summary>
        public string Summary => IsValid ? string.Empty : string.Join("; ", Violations);
    }
}
