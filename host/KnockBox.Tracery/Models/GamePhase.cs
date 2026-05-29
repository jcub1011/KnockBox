namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// The phases a Tracery match moves through. The engine drives transitions and the
    /// root page (<c>TraceryRoom</c>) switches its rendered view on the current phase.
    /// </summary>
    public enum GamePhase
    {
        /// <summary>Pre-game: players join, host configures settings.</summary>
        Lobby,

        /// <summary>Brief "round N" countdown before the grid appears.</summary>
        RoundIntro,

        /// <summary>The timed round: the shared grid is shown and players trace words.</summary>
        Playing,

        /// <summary>
        /// The single post-round intermission — the host-screen reveal of notable words, words
        /// nobody found, round scoring, and the cumulative-score standings, plus a "next round"
        /// indicator. Auto-advances straight into the next round (or final standings).
        /// </summary>
        Reveal,

        /// <summary>End of match: final standings across all rounds.</summary>
        FinalStandings
    }
}
