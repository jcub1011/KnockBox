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

        /// <summary>The host-screen reveal — notable words, words nobody found, scores.</summary>
        Reveal,

        /// <summary>Post-reveal pause showing this round's standings before advancing.</summary>
        RoundOver,

        /// <summary>End of match: final standings across all rounds.</summary>
        FinalStandings
    }
}
