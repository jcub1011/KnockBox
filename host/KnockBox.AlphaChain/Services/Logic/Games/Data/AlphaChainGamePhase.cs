namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// High-level phases the Alpha Chain FSM moves through. <see cref="Setup"/> is a
    /// transient bootstrap phase that immediately hands off to <see cref="Round"/> (or a
    /// <see cref="Tutorial"/>); <see cref="Intermission"/> is the era-boundary card draft;
    /// <see cref="Tutorial"/> is a non-interactive scripted demo (the in-Intermission Tax
    /// tutorial instead rides on an Intermission sub-phase, so this phase covers the
    /// full-screen Shiritori and Engine tutorials).
    /// </summary>
    public enum AlphaChainGamePhase
    {
        Setup,
        Round,
        Intermission,
        Tutorial,
        GameOver
    }
}
