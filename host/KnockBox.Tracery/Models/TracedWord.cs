namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// A word found on the grid together with the cell path that spells it.
    /// Produced by <c>TracerySolver.Solve</c> (one per distinct findable word) and
    /// by accepted runtime submissions. The <see cref="Path"/> is an ordered list of
    /// <c>Grid</c> cell ids; consecutive entries are 8-way adjacent and no cell repeats.
    /// Paths exist for the reveal animation (Milestone 07), not for scoring — scoring
    /// only cares about the set of distinct <see cref="Word"/>s.
    /// </summary>
    public sealed record TracedWord(string Word, IReadOnlyList<int> Path);
}
