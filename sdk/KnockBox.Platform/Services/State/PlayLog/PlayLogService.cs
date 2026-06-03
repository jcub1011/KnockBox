using KnockBox.Core.Primitives.Returns;
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
        }
        catch (OperationCanceledException) { return; /* Circuit/service tearing down — drop silently. */ }

        try
        {
            var readResult = await ReadAsync(ct).ConfigureAwait(false);
            if (readResult.IsCanceled) return;

            // On a read failure ReadAsync has already reset the (unreadable) storage, so we
            // start from an empty history — the new entry is still recorded and the log heals.
            readResult.TryGetSuccess(out var history);
            history ??= [];

            history.Insert(0, log);
            if (history.Count > MaxEntries)
                history.RemoveRange(MaxEntries, history.Count - MaxEntries);

            var setResult = await localStorage.SetAsync(Scope, Key, history, ct).ConfigureAwait(false);
            if (setResult.TryGetFailure(out var error))
                logger.LogError("Error storing play log for game [{game}]: {error}", log.GameIdentifier, error.InternalMessage);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask<ValueResult<IReadOnlyList<GameLog>>> GetLogsAsync(CancellationToken ct = default)
    {
        var readResult = await ReadAsync(ct).ConfigureAwait(false);
        if (readResult.IsCanceled) return ValueResult<IReadOnlyList<GameLog>>.FromCancellation();
        if (readResult.TryGetFailure(out var error)) return error;

        readResult.TryGetSuccess(out var history);
        // Defensively honor the cap even if a larger list was hand-written to storage.
        if (history.Count > MaxEntries)
            history.RemoveRange(MaxEntries, history.Count - MaxEntries);
        return ValueResult<IReadOnlyList<GameLog>>.FromValue(history);
    }

    public async ValueTask<ValueResult<IReadOnlyList<GameLog>>> GetLogsAsync(string gameIdentifier, CancellationToken ct = default)
    {
        var allResult = await GetLogsAsync(ct).ConfigureAwait(false);
        if (allResult.IsCanceled) return ValueResult<IReadOnlyList<GameLog>>.FromCancellation();
        if (allResult.TryGetFailure(out var error)) return error;

        allResult.TryGetSuccess(out var all);
        IReadOnlyList<GameLog> filtered = all
            .Where(l => string.Equals(l.GameIdentifier, gameIdentifier, StringComparison.Ordinal))
            .ToList();
        return ValueResult<IReadOnlyList<GameLog>>.FromValue(filtered);
    }

    public async ValueTask<Result> ClearAsync(CancellationToken ct = default)
    {
        var result = await localStorage.RemoveAsync(Scope, Key).ConfigureAwait(false);
        if (result.TryGetFailure(out var error))
            logger.LogError("Error clearing play log: {error}", error.InternalMessage);
        return result;
    }

    private async ValueTask<ValueResult<List<GameLog>>> ReadAsync(CancellationToken ct)
    {
        var result = await localStorage.GetAsync<List<GameLog>>(Scope, Key, ct).ConfigureAwait(false);
        if (result.IsCanceled) return ValueResult<List<GameLog>>.FromCancellation();
        if (result.TryGetFailure(out var error))
        {
            // Corrupt/legacy-shaped JSON (or an interop failure) would otherwise wedge every
            // read and append. Best-effort reset so the log self-heals rather than staying broken,
            // then surface the failure so callers can distinguish "no logs" from "couldn't read".
            logger.LogWarning("Discarding unreadable play-log history ({error}); resetting storage.", error.InternalMessage);
            await localStorage.RemoveAsync(Scope, Key).ConfigureAwait(false);
            return error;
        }

        result.TryGetSuccess(out var stored);
        return stored ?? [];
    }
}
