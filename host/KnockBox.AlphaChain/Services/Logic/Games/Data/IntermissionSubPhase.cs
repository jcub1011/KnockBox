namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// The ordered sub-phases the Intermission walks through between eras. The
    /// <c>AlphaChainGameState.IntermissionPhase</c> field tracks the current step; the
    /// Intermission state's <c>Tick</c> drives the progression deterministically:
    /// <see cref="Deal"/> → <see cref="Expansion"/> → <see cref="Optimization"/> →
    /// <see cref="SniperBan"/> → <see cref="Complete"/> (which hands back to the round loop).
    /// </summary>
    public enum IntermissionSubPhase
    {
        /// <summary>Each active player is privately dealt new modifier + action cards.</summary>
        Deal,

        /// <summary>Every active player's Engine Bay gains one modifier slot.</summary>
        Expansion,

        /// <summary>Players privately reorder their Engine Bay under a countdown.</summary>
        Optimization,

        /// <summary>The lowest-scoring active player picks the next era's banned letter.</summary>
        SniperBan,

        /// <summary>The Intermission is finished; the FSM returns to the round loop.</summary>
        Complete
    }
}
