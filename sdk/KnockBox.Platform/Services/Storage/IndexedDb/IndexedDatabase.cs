using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class IndexedDatabase : IIndexedDatabase
{
    private readonly IndexedDbInterop _interop;
    private readonly ILogger<IndexedDatabase> _logger;
    private readonly int _dbId;
    private readonly DotNetObjectReference<VersionChangeBridge> _bridgeRef;
    private bool _disposed;

    public string Name { get; }
    public int Version { get; }
    public IReadOnlyList<string> ObjectStoreNames { get; }

    public event Func<ValueTask>? VersionChangeRequested;

    public IndexedDatabase(
        IndexedDbInterop interop,
        ILogger<IndexedDatabase> logger,
        int dbId,
        string name,
        int version,
        IReadOnlyList<string> objectStoreNames,
        DotNetObjectReference<VersionChangeBridge> bridgeRef)
    {
        _interop = interop;
        _logger = logger;
        _dbId = dbId;
        Name = name;
        Version = version;
        ObjectStoreNames = objectStoreNames;
        _bridgeRef = bridgeRef;
    }

    public IIndexedDbTransaction BeginTransaction(IReadOnlyList<string> storeNames, TransactionMode mode)
        => throw new NotImplementedException("Transactions land in Phase 2 of the IndexedDB rollout.");

    public ValueTask<ValueResult<T, IndexedDbError>> RunAsync<T>(
        IReadOnlyList<string> storeNames,
        TransactionMode mode,
        Func<IIndexedDbTransaction, CancellationToken, ValueTask<ValueResult<T, IndexedDbError>>> work,
        CancellationToken ct = default)
        => throw new NotImplementedException("Transactions land in Phase 2 of the IndexedDB rollout.");

    public ValueTask<Result<IndexedDbError>> RunAsync(
        IReadOnlyList<string> storeNames,
        TransactionMode mode,
        Func<IIndexedDbTransaction, CancellationToken, ValueTask<Result<IndexedDbError>>> work,
        CancellationToken ct = default)
        => throw new NotImplementedException("Transactions land in Phase 2 of the IndexedDB rollout.");

    internal async ValueTask RaiseVersionChangeRequestedAsync()
    {
        var handler = VersionChangeRequested;
        if (handler is null) return;

        foreach (var subscriber in handler.GetInvocationList().Cast<Func<ValueTask>>())
        {
            try
            {
                await subscriber.Invoke().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A failing subscriber must not break the others; log and continue.
                _logger.LogError(ex,
                    "VersionChangeRequested subscriber for database '{DatabaseName}' threw.",
                    Name);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        var close = await _interop
            .InvokeVoidAsync("closeDatabase", CancellationToken.None, _dbId)
            .ConfigureAwait(false);
        if (close.TryGetFailure(out var error))
        {
            _logger.LogWarning(
                "closeDatabase for '{DatabaseName}' (handle {DbId}) returned [{Kind}] {Message}.",
                Name, _dbId, error.Kind, error.Message);
        }

        _bridgeRef.Dispose();
    }
}
