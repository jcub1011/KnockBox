namespace KnockBox.AlphaChain.Contracts;

/// <summary>
/// The ordered sub-phases the Intermission walks through between eras. The
/// <c>AlphaChainGameState.IntermissionPhase</c> field tracks the current step; the
/// Intermission state's <c>Tick</c> drives the progression deterministically:
/// <see cref="Optimization"/> → (<see cref="TaxTutorial"/>) → <see cref="SniperBan"/> →
/// <see cref="Complete"/> (which hands back to the round loop). Dealing cards and the +1
/// Engine Bay slot are applied instantly in <c>OnEnter</c> (no dedicated dwell sub-phases);
/// the freshly-dealt cards are revealed inside Optimization.
/// </summary>
public enum IntermissionSubPhase
{
    /// <summary>Players privately reorder their (just-dealt, expanded) Engine Bay under a countdown.</summary>
    Optimization,

    /// <summary>The Tax tutorial (first Intermission only, when tutorials are enabled). Plays before
    /// the first Sniper Ban so players understand the tax before any letter is banned; touches no
    /// game state, then hands to <see cref="SniperBan"/>.</summary>
    TaxTutorial,

    /// <summary>The lowest-scoring active player picks the next era's banned letter.</summary>
    SniperBan,

    /// <summary>The Intermission is finished; the FSM returns to the round loop.</summary>
    Complete
}
