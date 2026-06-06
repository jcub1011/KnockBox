namespace KnockBox.AlphaChain.Services.Logic.Scoring
{
    /// <summary>
    /// Maps a score-replay step's value to the CSS color its <em>delta</em> should render in, so every
    /// surface that shows a step (the replay strip, the card-library breakdown, the submission-history
    /// chips) colors it the same way: a gain is green, a loss is red, and a zero-change effect ("FX")
    /// step takes the effect violet. The card icon/border keeps its family accent — only the delta value
    /// carries this gain/loss/effect semantic.
    /// </summary>
    public static class ScoreStepColors
    {
        /// <summary>The CSS color token for <paramref name="step"/>'s delta value.</summary>
        public static string Delta(ScoreStep step)
        {
            if (!step.Triggered)
                return "var(--ac-accent-neutral, #8aa0b3)";          // "—" — never fired
            if (step.ValueText == "FX")
                return "var(--ac-violet, #b97bff)";                  // fired, no score change
            return step.ValueText.StartsWith('+')
                ? "var(--ac-additive, #14f195)"                      // gain
                : "var(--ac-danger, #ff3b5c)";                       // loss
        }
    }
}
