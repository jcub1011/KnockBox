using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.Services.State.PlayLog;

/// <summary>
/// <see cref="IPlayLogService"/> backed by browser <c>localStorage</c> via
/// <see cref="ILocalStorageService"/>. The whole history is stored as a single
/// JSON array under <c>play-log.history</c>; appends are a guarded
/// read-modify-write so overlapping <see cref="StoreLogAsync"/> calls on the
/// same circuit can't lose entries. Mirrors the per-circuit, best-effort
/// persistence pattern of <c>UserService</c>.
/// </summary>
public sealed class PlayLogService(
    ILocalStorageService localStorage,
    ILogger<PlayLogService> logger) : IPlayLogService
{
    /// <summary>Most recent entries retained; older ones are dropped on append.</summary>
    internal const int MaxEntries = 50;

    private const string Scope = "play-log";
    private const string Key = "history";

    // Serializes the read-modify-write in StoreLogAsync. The service is scoped
    // per circuit, so a single semaphore per instance is enough.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async ValueTask StoreLogAsync(GameLog log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        // The service is the source of truth for when a game was played.
        log = log with { PlayedAt = DateTimeOffset.UtcNow };

        try
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var history = await ReadAsync(ct).ConfigureAwait(false);
                history.Insert(0, log);
                if (history.Count > MaxEntries)
                    history.RemoveRange(MaxEntries, history.Count - MaxEntries);

                await localStorage.SetAsync(Scope, Key, history, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException) { /* Circuit/service tearing down — drop silently. */ }
        catch (ObjectDisposedException) { /* Storage disposed — drop silently. */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error storing play log for game [{game}].", log.GameIdentifier);
        }
    }

    public async ValueTask<IReadOnlyList<GameLog>> GetLogsAsync(CancellationToken ct = default)
    {
        try
        {
            var history = await ReadAsync(ct).ConfigureAwait(false);
            // Defensively honor the cap even if a larger list was hand-written to storage.
            if (history.Count > MaxEntries)
                history.RemoveRange(MaxEntries, history.Count - MaxEntries);
            return history;
        }
        catch (OperationCanceledException) { return []; }
        catch (ObjectDisposedException) { return []; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading play log.");
            return [];
        }
    }

    public async ValueTask<IReadOnlyList<GameLog>> GetLogsAsync(string gameIdentifier, CancellationToken ct = default)
    {
        var all = await GetLogsAsync(ct).ConfigureAwait(false);
        return all
            .Where(l => string.Equals(l.GameIdentifier, gameIdentifier, StringComparison.Ordinal))
            .ToList();
    }

    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        try
        {
            await localStorage.RemoveAsync(Scope, Key).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* Drop silently. */ }
        catch (ObjectDisposedException) { /* Drop silently. */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error clearing play log.");
        }
    }

    private async ValueTask<List<GameLog>> ReadAsync(CancellationToken ct)
    {
        var stored = await localStorage.GetAsync<List<GameLog>>(Scope, Key, ct).ConfigureAwait(false);
        return stored ?? [];
    }
}
