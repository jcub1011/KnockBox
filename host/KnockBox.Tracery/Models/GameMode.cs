namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// The play modes a Tracery match can run in. Held by <c>TracerySettings.Mode</c> and frozen
    /// for the match once it starts. The engine branches its per-round behaviour
    /// (<c>EnterPlaying</c>, <c>SubmitTrace</c>, <c>CompleteRound</c>) on this; everything else —
    /// the phase machine, lobby, grid, trie — is shared across both modes.
    /// </summary>
    public enum GameMode
    {
        /// <summary>
        /// The default free-for-all: players race the clock banking any valid word they can trace,
        /// scored with the length, rare-letter, and unique-find layers (GDD §5).
        /// </summary>
        Standard,

        /// <summary>
        /// A shared word-search race: every player is given the same curated list of target words
        /// to find on the board. Only listed words score (flat, by length); the first players to
        /// find them all earn a placement bonus that scales with the player count.
        /// </summary>
        Search
    }
}
