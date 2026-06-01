namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Discriminated outcome of a <see cref="SubmitWordCommand"/>, surfaced to the UI
    /// so it can show inline acceptance/rejection feedback. The FSM computes this inside
    /// the state lock and stashes it on <c>AlphaChainGameContext.LastSubmitResult</c>;
    /// the engine returns it from <c>SubmitWordAsync</c>.
    /// </summary>
    public abstract record SubmitWordResult
    {
        /// <summary>The word was valid and scored <paramref name="Score"/> points (length-only in M2).</summary>
        public sealed record Accepted(int Score) : SubmitWordResult;

        /// <summary>
        /// The word was valid but contained the banned letter, so the Zero-Point Tax
        /// applied: it scored 0 yet still keeps (or, as a last letter, clears) the chain.
        /// </summary>
        public sealed record AcceptedZeroPointTax : SubmitWordResult;

        /// <summary>The submitting player is not the active player.</summary>
        public sealed record RejectedNotYourTurn : SubmitWordResult;

        /// <summary>The word did not start with the required succession letter.</summary>
        public sealed record RejectedChainBroken(char Required) : SubmitWordResult;

        /// <summary>The word is not in the dictionary.</summary>
        public sealed record RejectedNotInDictionary : SubmitWordResult;

        /// <summary>The word has already been played this match.</summary>
        public sealed record RejectedDuplicate : SubmitWordResult;

        /// <summary>The submission was empty after trimming.</summary>
        public sealed record RejectedEmpty : SubmitWordResult;
    }
}
