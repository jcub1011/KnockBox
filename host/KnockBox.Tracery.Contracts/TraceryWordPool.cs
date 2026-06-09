namespace KnockBox.Tracery.Contracts
{
    /// <summary>
    /// The dictionary pools a host can pick for board generation and answer validation.
    /// Mirrors <c>KnockBox.WordService.Contracts.WordPoolMode</c> by name so the server can
    /// map between them, but is declared here so the wire <see cref="TracerySettingsView"/>
    /// carries no dependency on the WordService contracts assembly (a Contracts project must
    /// reference zero KnockBox.* assemblies).
    /// </summary>
    public enum TraceryWordPool
    {
        /// <summary>The NYT Wordle answer list (5-letter only).</summary>
        NytStandard,

        /// <summary>The curated common-word list.</summary>
        ReducedDictionary,

        /// <summary>The full dictionary (union of NYT + the large word list).</summary>
        FullDictionary
    }
}
