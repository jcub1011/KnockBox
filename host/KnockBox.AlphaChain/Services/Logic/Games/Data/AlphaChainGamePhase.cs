namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// High-level phases the Alpha Chain FSM moves through. <see cref="Setup"/> is a
    /// transient bootstrap phase that immediately hands off to <see cref="Round"/>;
    /// <see cref="Intermission"/> is reserved for the era-boundary card draft (M4) and
    /// is never entered in M1.
    /// </summary>
    public enum AlphaChainGamePhase
    {
        Setup,
        Round,
        Intermission,
        GameOver
    }
}
