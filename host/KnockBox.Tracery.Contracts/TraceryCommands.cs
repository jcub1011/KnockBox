namespace KnockBox.Tracery.Contracts
{
    /// <summary>
    /// The command names the browser UI sends over the hub. The server's
    /// <c>TraceryGameEngine.HandleCommandAsync</c> maps each to the engine method a Razor page used
    /// to call directly. Lobby creation is NOT a command — it flows through <c>GameHub.CreateRoom</c>.
    /// </summary>
    public static class TraceryCommands
    {
        /// <summary>Host: start the match. Payload: <see cref="StartPayload"/>.</summary>
        public const string Start = "start";

        /// <summary>Player: submit a traced word. Payload: <see cref="SubmitTracePayload"/>.</summary>
        public const string SubmitTrace = "submit-trace";

        /// <summary>Host: skip the remaining post-round reveal time. No payload.</summary>
        public const string SkipReveal = "skip-reveal";

        /// <summary>Host: replace the lobby settings. Payload: <see cref="TracerySettingsView"/>.</summary>
        public const string UpdateSettings = "update-settings";

        /// <summary>Host: kick a player from the lobby. Payload: <see cref="KickPlayerPayload"/>.</summary>
        public const string KickPlayer = "kick-player";

        /// <summary>Host: return a finished match to the joinable lobby. No payload.</summary>
        public const string ReturnToLobby = "return-to-lobby";
    }

    /// <summary>Start payload — whether the host plays as a participant rather than the display.</summary>
    public sealed record StartPayload(bool HostPlays);

    /// <summary>A submitted trace: the ordered list of grid cell ids the player traced.</summary>
    public sealed record SubmitTracePayload(IReadOnlyList<int> Path);

    /// <summary>The id of the player the host wants to kick.</summary>
    public sealed record KickPlayerPayload(Guid PlayerId);
}
