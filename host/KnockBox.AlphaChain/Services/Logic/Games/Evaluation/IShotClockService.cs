using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Evaluation
{
    /// <summary>
    /// Owns shot-clock arming: it walks a player's bay for the clock capabilities
    /// (<see cref="Data.Cards.Library.IShotClockOverride"/>,
    /// <see cref="Data.Cards.Library.IBaseShotClockProvider"/>,
    /// <see cref="Data.Cards.Library.IShotClockModifier"/>) and resolves the armed length.
    /// Plugin-internal; resolved from <see cref="Data.EngineEvaluationContext.Services"/> (The Prism
    /// refill) and called by <c>AlphaChainGameState.ResetTurnTimer</c>.
    /// </summary>
    public interface IShotClockService
    {
        /// <summary>
        /// The shot-clock length to arm for <paramref name="player"/>: the configured base (or a
        /// Hyper-Drive base replacement when latched) with every clock effect folded in, floored at the
        /// minimum — unless an Anchor Chain pins it to a fixed, unmodifiable length.
        /// </summary>
        int ComputeArmedSeconds(AlphaChainPlayerState player);

        /// <summary>Re-arms <paramref name="player"/>'s shot clock to a freshly-computed full length from now (The Prism).</summary>
        void RefillToFull(AlphaChainPlayerState player);
    }
}
