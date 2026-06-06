namespace KnockBox.AlphaChain.Services.Logic.Games.Evaluation
{
    /// <summary>
    /// Draws legal banned letters from the match's ban pool. Plugin-internal; resolved by cards from
    /// <see cref="Data.EngineEvaluationContext.Services"/> at era start (Roulette Wheel, Toll Booth).
    /// </summary>
    public interface IBanLetterService
    {
        /// <summary>
        /// Draws a legal personal banned letter from the match pool, automatically nudging off the
        /// current era banned letter when it collides so the personal ban stays a distinct hazard.
        /// </summary>
        char RollPersonalBan();
    }
}
