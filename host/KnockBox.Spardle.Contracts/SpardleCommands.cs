namespace KnockBox.Spardle.Contracts;

/// <summary>
/// The command names the browser UI sends over the hub. The server's
/// <c>SpardleEngine.HandleCommandAsync</c> maps each to the engine method a Razor
/// page used to call directly. Lobby creation is NOT a command (it flows through
/// <c>GameHub.CreateRoom</c>), and the custom word pool is NOT a command — it's a
/// streamed file upload (<c>IGameUploadHandler</c>, kind <c>"word-pool"</c>).
/// </summary>
public static class SpardleCommands
{
    /// <summary>Host: start the match. Payload: <see cref="StartPayload"/>.</summary>
    public const string Start = "start";

    /// <summary>Participant: submit a guess. Payload: <see cref="SubmitGuessPayload"/>.</summary>
    public const string SubmitGuess = "submit-guess";

    /// <summary>Participant: forfeit the current round. No payload.</summary>
    public const string GiveUp = "give-up";

    /// <summary>Host: replace the lobby settings. Payload: <see cref="SpardleSettingsView"/>.</summary>
    public const string UpdateSettings = "update-settings";

    /// <summary>Host: return a finished match to the joinable lobby. No payload.</summary>
    public const string ReturnToLobby = "return-to-lobby";

    /// <summary>Host: kick a player from the lobby. Payload: <see cref="KickPlayerPayload"/>.</summary>
    public const string KickPlayer = "kick-player";

    /// <summary>The upload kind the host's CSV/typed word pool is sent under (see IGameUploadHandler).</summary>
    public const string WordPoolUploadKind = "word-pool";
}

/// <summary>
/// Start payload. Carries the one start-time choice the client owns —
/// whether the host plays as a participant or becomes the shared display. The
/// custom word pool is uploaded separately (it's too large for a hub message and
/// is a host secret), so it isn't on this payload. The engine applies
/// <see cref="HostPlaysAlong"/> via <c>UpdateSettings</c> before delegating to
/// <c>StartAsync</c>.
/// </summary>
public sealed record StartPayload(bool HostPlaysAlong);

/// <summary>A participant's guess for the current round.</summary>
public sealed record SubmitGuessPayload(string Word);

/// <summary>The id of the player the host wants to kick.</summary>
public sealed record KickPlayerPayload(Guid PlayerId);
