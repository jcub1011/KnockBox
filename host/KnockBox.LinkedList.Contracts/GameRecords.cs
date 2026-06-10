namespace KnockBox.LinkedList.Services.State.Games
{
    // These gameplay records + the phase enum originally lived on the server plugin
    // (LinkedListGameState.cs). They moved to the contracts assembly — keeping their
    // original KnockBox.LinkedList.Services.State.Games namespace — so the server's
    // pervasive `using`s and the projected view both bind the same CLR types. The
    // server-only RoundResult and LinkedListPlayerState stay in the server project
    // (the view carries projected equivalents).

    /// <summary>The current phase of the game.</summary>
    public enum LinkedListGamePhase { Setup, Playing, RoundOver, GameOver }

    /// <summary>An accepted link in the chain (<c>FromWord</c> → <c>ToWord</c>).</summary>
    public sealed record ChainLink(string FromWord, string ToWord, Guid PlayerId, string PlayerName, bool IsLoop);

    /// <summary>A rejected attempt by the Auditor.</summary>
    public sealed record RejectionInfo(Guid PlayerId, string AttemptedWord);

    /// <summary>A player's proposed next word (the first word is the carried word).</summary>
    public sealed record Submission(Guid PlayerId, string ProposedWord);

    /// <summary>A fun end-of-match award (§10): a title, the winning player, and a
    /// short detail line explaining why they earned it.</summary>
    public sealed record Superlative(string Title, string Emoji, Guid PlayerId, string PlayerName, string Detail);

    /// <summary>
    /// A group's place on the competitive scoreboard (§8.2). <paramref name="Primary"/>
    /// is the active mode's metric (guess count or elapsed time) and
    /// <paramref name="Secondary"/> the other; ranking sorts on the primary and breaks
    /// ties with the secondary. <paramref name="IsTieBreakWinner"/> flags a group that
    /// out-ranked another on equal primary metric purely via the secondary.
    /// </summary>
    public sealed record GroupStanding(
        string GroupId, string GroupName, int Rank,
        int Guesses, TimeSpan Elapsed, bool DestinationReached,
        bool IsTieBreakWinner);
}
