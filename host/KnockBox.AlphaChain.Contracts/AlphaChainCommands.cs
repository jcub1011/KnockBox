namespace KnockBox.AlphaChain.Contracts;

/// <summary>
/// The command names the browser UI sends over the hub (<c>GameHub.SubmitCommand</c>), routed
/// server-side by <c>AlphaChainGameEngine.HandleCommandAsync</c> to the matching engine method.
/// Lobby creation is NOT a command — it flows through <c>GameHub.CreateRoom</c>.
/// </summary>
public static class AlphaChainCommands
{
    /// <summary>Host starts the match (payload: <see cref="StartPayload"/>).</summary>
    public const string Start = "start";

    /// <summary>Active player submits a word (payload: <see cref="SubmitWordPayload"/>).</summary>
    public const string SubmitWord = "submit-word";

    /// <summary>Active player ends their turn without a play (no payload).</summary>
    public const string AdvanceTurn = "advance-turn";

    /// <summary>Player commits their Engine Bay ordering during Optimization (payload: <see cref="OptimizationPayload"/>).</summary>
    public const string SubmitOptimization = "submit-optimization";

    /// <summary>Last-place player picks the next era's banned letter (payload: <see cref="SniperBanPayload"/>).</summary>
    public const string SelectSniperBan = "select-sniper-ban";

    /// <summary>Host skips the currently-showing tutorial (no payload).</summary>
    public const string SkipTutorial = "skip-tutorial";

    /// <summary>Host returns a finished match to the lobby (no payload).</summary>
    public const string ReturnToLobby = "return-to-lobby";

    /// <summary>Host edits the match settings (payload: <see cref="AlphaChainSettings"/>).</summary>
    public const string UpdateSettings = "update-settings";

    /// <summary>Host kicks a player from the lobby (payload: <see cref="TargetPayload"/>).</summary>
    public const string KickPlayer = "kick-player";

    // ── Testing Bay (host-only developer card bench; only when no other players) ──

    /// <summary>Host opens the Testing Bay (creates a throwaway bench scenario; no payload).</summary>
    public const string BenchEnter = "bench-enter";

    /// <summary>Host closes the Testing Bay and returns to the lobby (no payload).</summary>
    public const string BenchExit = "bench-exit";

    /// <summary>Restart the bench scenario with a player count (payload: <see cref="BenchResetPayload"/>).</summary>
    public const string BenchReset = "bench-reset";

    /// <summary>Set/clear the bench's banned letter (payload: <see cref="BenchBanPayload"/>).</summary>
    public const string BenchSetBan = "bench-set-ban";

    /// <summary>Rebuild a bench player's Engine Bay (payload: <see cref="BenchBayPayload"/>).</summary>
    public const string BenchSetBay = "bench-set-bay";

    /// <summary>Set a bench player's running score (payload: <see cref="BenchScorePayload"/>).</summary>
    public const string BenchSetScore = "bench-set-score";

    /// <summary>Submit a word for the bench's current player (payload: <see cref="BenchSubmitPayload"/>).</summary>
    public const string BenchSubmit = "bench-submit";

    /// <summary>Advance the bench's active seat without playing a word (no payload).</summary>
    public const string BenchSkip = "bench-skip";
}

/// <summary>Payload for <see cref="AlphaChainCommands.Start"/>: whether the host deals themselves in.</summary>
public sealed record StartPayload(bool HostPlays);

/// <summary>Payload for <see cref="AlphaChainCommands.SubmitWord"/>.</summary>
public sealed record SubmitWordPayload(string Word);

/// <summary>Payload for <see cref="AlphaChainCommands.SubmitOptimization"/>: the desired left→right bay order.</summary>
public sealed record OptimizationPayload(IReadOnlyList<string> CardIds);

/// <summary>Payload for <see cref="AlphaChainCommands.SelectSniperBan"/>. A single-character string
/// (sent as a string rather than a JSON <c>char</c>, which round-trips awkwardly).</summary>
public sealed record SniperBanPayload(string Letter);

/// <summary>Payload for <see cref="AlphaChainCommands.KickPlayer"/>: the target player's id.</summary>
public sealed record TargetPayload(Guid PlayerId);

/// <summary>Payload for <see cref="AlphaChainCommands.BenchReset"/>: the player count to restart with.</summary>
public sealed record BenchResetPayload(int PlayerCount);

/// <summary>Payload for <see cref="AlphaChainCommands.BenchSetBan"/>. A single-character string; null/empty clears the ban.</summary>
public sealed record BenchBanPayload(string? Letter);

/// <summary>Payload for <see cref="AlphaChainCommands.BenchSetBay"/>: a bench player's left→right bay order
/// (each id is a <c>ModifierId.ToString()</c> value, matching <see cref="OptimizationPayload"/>).</summary>
public sealed record BenchBayPayload(Guid PlayerId, IReadOnlyList<string> CardIds);

/// <summary>Payload for <see cref="AlphaChainCommands.BenchSetScore"/>: set a bench player's running score.</summary>
public sealed record BenchScorePayload(Guid PlayerId, int Score);

/// <summary>Payload for <see cref="AlphaChainCommands.BenchSubmit"/>: a word for the current player, and an
/// optional shot-clock "remaining seconds" (positions the clock so Chrono Syphon can be staged).</summary>
public sealed record BenchSubmitPayload(string Word, int? RemainingSeconds);
