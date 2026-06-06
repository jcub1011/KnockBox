namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// The running score a word accumulates as it folds through the Engine Bay. Seeded with the word
    /// length and rebuilt (via <c>with</c>) by each triggered card, which decides how it changes
    /// <see cref="CurrentScore"/> — additive cards add, multiplicative cards multiply. A
    /// <see langword="readonly"/> value carried on <see cref="EngineEvaluationContext"/> so the scoring
    /// surface stays small; new score-shaping inputs become new properties here rather than new
    /// context fields.
    /// </summary>
    /// <param name="CurrentScore">The running score after every card folded in so far.</param>
    public readonly record struct ScoreContext(double CurrentScore)
    {
        /// <summary>The running score with <paramref name="magnitude"/> added.</summary>
        public ScoreContext Add(double magnitude) => this with { CurrentScore = CurrentScore + magnitude };

        /// <summary>The running score multiplied by <paramref name="factor"/>.</summary>
        public ScoreContext Multiply(double factor) => this with { CurrentScore = CurrentScore * factor };
    }
}
