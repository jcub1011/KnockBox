namespace KnockBox.LinkedList
{
    // These enums originally lived in the server plugin (LinkedListSettings.cs). They
    // moved to the contracts assembly — keeping their original KnockBox.LinkedList
    // namespace — so the server `using`s and the wire DTOs both bind the same CLR type.

    /// <summary>How a finished round/match is scored.</summary>
    public enum ScoringMode { FewestGuesses, FastestTime }

    /// <summary>Whether everyone shares one chain or competes in groups.</summary>
    public enum PlayerStructure { Collective, Groups }
}
