namespace KnockBox.LinkedList.Contracts
{
    /// <summary>
    /// The command names the browser UI sends over the hub. The server's
    /// <c>LinkedListGameEngine.HandleCommandAsync</c> maps each to the engine method a Razor page
    /// used to call directly. Lobby creation is NOT a command — it flows through
    /// <c>GameHub.CreateRoom</c>.
    /// </summary>
    public static class LinkedListCommands
    {
        /// <summary>Host: start the match. Payload: <see cref="StartPayload"/>.</summary>
        public const string Start = "start";

        /// <summary>Active submitter: propose the next word. Payload: <see cref="SubmitPairPayload"/>.</summary>
        public const string SubmitPair = "submit-pair";

        /// <summary>Auditor: accept the front group's pending submission. No payload.</summary>
        public const string Approve = "approve";

        /// <summary>Auditor: reject the front group's pending submission. No payload.</summary>
        public const string Reject = "reject";

        /// <summary>Host: end the in-progress round from its current progress (§10.3). No payload.</summary>
        public const string EndRound = "end-round";

        /// <summary>Host: rotate the Auditor and start the next round (§6/§10). No payload.</summary>
        public const string NextRound = "next-round";

        /// <summary>Host: end the match and show the Results screen (§10). No payload.</summary>
        public const string EndMatch = "end-match";

        /// <summary>Host: return a finished match to the joinable lobby. No payload.</summary>
        public const string ReturnToLobby = "return-to-lobby";

        /// <summary>Host: kick a player from the lobby. Payload: <see cref="KickPlayerPayload"/>.</summary>
        public const string KickPlayer = "kick-player";

        /// <summary>Host: replace the lobby settings. Payload: <see cref="LinkedListSettingsView"/>.</summary>
        public const string UpdateSettings = "update-settings";
    }

    /// <summary>
    /// Start payload. Carries the start-time lobby setup the client owns — whether the host plays
    /// (vs. being the shared display), the Groups-mode team assignment, the first Auditor, and any
    /// host-typed start/destination words. The engine applies these onto state inside the execute
    /// lock <em>before</em> delegating to <c>StartAsync</c>. A null collection / id / word means
    /// "no override" (the engine auto-assigns / draws random words).
    /// </summary>
    public sealed record StartPayload(
        bool HostPlays,
        IReadOnlyList<IReadOnlyList<Guid>>? GroupAssignments,
        Guid? FirstAuditorId,
        string? StartWordOverride,
        string? DestinationWordOverride);

    /// <summary>The active submitter's proposed next word.</summary>
    public sealed record SubmitPairPayload(string Word);

    /// <summary>The id of the player the host wants to kick.</summary>
    public sealed record KickPlayerPayload(Guid PlayerId);
}
