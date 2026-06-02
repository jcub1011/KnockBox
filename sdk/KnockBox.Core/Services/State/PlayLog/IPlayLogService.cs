namespace KnockBox.Core.Services.State.PlayLog;

/// <summary>
/// Records a per-user history of games played, persisted on the client so it
/// survives across sessions. Any game can append to it via
/// <see cref="StoreLogAsync"/>; the log keeps only the most recent entries
/// (capped — see the implementation) so it can't grow without bound. Scoped
/// per browser circuit, mirroring <c>IUserService</c>.
/// <para>
/// <b>Prerendering:</b> every method reaches the browser via JS interop, which is
/// unavailable during server prerendering. Consumers must call these from
/// <c>OnAfterRenderAsync</c> (or a later interactive event), never from
/// <c>OnInitialized</c>/<c>OnInitializedAsync</c> of a prerendered component.
/// </para>
/// </summary>
public interface IPlayLogService
{
    /// <summary>
    /// Appends <paramref name="log"/> to the front of the history (newest
    /// first), trimming the oldest entries past the cap.
    /// <see cref="GameLog.PlayedAt"/> is stamped here with the current time.
    /// Best-effort: storage failures are logged and swallowed, never thrown
    /// into the calling game.
    /// </summary>
    ValueTask StoreLogAsync(GameLog log, CancellationToken ct = default);

    /// <summary>
    /// Returns the full play history, newest first. Empty when nothing has been
    /// logged yet.
    /// </summary>
    ValueTask<IReadOnlyList<GameLog>> GetLogsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the play history for a single game (matched on
    /// <see cref="GameLog.GameIdentifier"/>), newest first.
    /// </summary>
    ValueTask<IReadOnlyList<GameLog>> GetLogsAsync(string gameIdentifier, CancellationToken ct = default);

    /// <summary>
    /// Clears the entire play log.
    /// </summary>
    ValueTask ClearAsync(CancellationToken ct = default);
}
