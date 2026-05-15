using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class IndexedDatabase : IIndexedDatabase
{
    private readonly IndexedDbInterop _interop;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IndexedDatabase> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IReadOnlyDictionary<string, StoreSchema> _schema;
    private readonly int _dbId;
    private readonly DotNetObjectReference<VersionChangeBridge> _bridgeRef;
    private bool _disposed;

    public string Name { get; }
    public int Version { get; }
    public IReadOnlyList<string> ObjectStoreNames { get; }

    public event Func<ValueTask>? VersionChangeRequested;

    public IndexedDatabase(
        IndexedDbInterop interop,
        ILoggerFactory loggerFactory,
        int dbId,
        string name,
        int version,
        IReadOnlyList<string> objectStoreNames,
        JsonSerializerOptions jsonOptions,
        IReadOnlyDictionary<string, StoreSchema> schema,
        DotNetObjectReference<VersionChangeBridge> bridgeRef)
    {
        _interop = interop;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<IndexedDatabase>();
        _jsonOptions = jsonOptions;
        _schema = schema;
        _dbId = dbId;
        Name = name;
        Version = version;
        ObjectStoreNames = objectStoreNames;
        _bridgeRef = bridgeRef;
    }

    public IIndexedDbTransaction BeginTransaction(IReadOnlyList<string> storeNames, TransactionMode mode)
    {
        if (storeNames.Count == 0)
            throw new ArgumentException("At least one store name is required.", nameof(storeNames));

        var bridge = new TxCompletionBridge();
        var bridgeRef = DotNetObjectReference.Create(bridge);
        var storeNamesArr = storeNames.ToArray();

        // beginTransaction is synchronous in IDB; the JS wrapper still resolves
        // a promise (its envelope arrives async via SignalR). Block-on-sync is
        // not viable in Blazor Server, so we adopt a different convention:
        // BeginTransaction returns immediately with a pending tx that defers
        // its txId resolution to the first op. The current rollout does NOT
        // implement that deferral — callers should prefer RunAsync, which
        // handles the async-begin path internally.
        throw new NotSupportedException(
            "Synchronous BeginTransaction is not implementable over JS interop. " +
            "Use RunAsync(...) instead — it begins the transaction asynchronously, " +
            "runs the supplied work, and commits or aborts based on the result.");
    }

    public async ValueTask<ValueResult<T, IndexedDbError>> RunAsync<T>(
        IReadOnlyList<string> storeNames,
        TransactionMode mode,
        Func<IIndexedDbTransaction, CancellationToken, ValueTask<ValueResult<T, IndexedDbError>>> work,
        CancellationToken ct = default)
    {
        if (storeNames.Count == 0)
            throw new ArgumentException("At least one store name is required.", nameof(storeNames));

        var bridge = new TxCompletionBridge();
        var bridgeRef = DotNetObjectReference.Create(bridge);

        var beginResult = await _interop.InvokeAsync<BeginTransactionResponse>(
            "beginTransaction", ct, _dbId, storeNames.ToArray(), (int)mode, bridgeRef)
            .ConfigureAwait(false);
        if (!beginResult.TryGetSuccess(out var begin))
        {
            bridgeRef.Dispose();
            if (beginResult.IsCanceled) return ValueResult<T, IndexedDbError>.Canceled;
            return beginResult.Error.Error;
        }

        var tx = new IndexedDbTransaction(
            _interop,
            _loggerFactory,
            begin.TxId, mode, storeNames, _jsonOptions, _schema, bridge, bridgeRef);

        try
        {
            ValueResult<T, IndexedDbError> workResult;
            try
            {
                workResult = await work(tx, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await tx.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                return ValueResult<T, IndexedDbError>.Canceled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RunAsync<{T}> on database '{DatabaseName}' threw inside the user delegate; aborting.",
                    typeof(T).Name, Name);
                await tx.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            if (workResult.IsCanceled)
            {
                await tx.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                return ValueResult<T, IndexedDbError>.Canceled;
            }

            if (!workResult.IsSuccess)
            {
                await tx.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                return workResult;
            }

            var commit = await tx.CommitAsync(ct).ConfigureAwait(false);
            if (commit.TryGetFailure(out var commitErr)) return commitErr;
            if (commit.IsCanceled) return ValueResult<T, IndexedDbError>.Canceled;

            try
            {
                await tx.Completed.ConfigureAwait(false);
            }
            catch (IndexedDbTransactionException txEx)
            {
                return txEx.Error;
            }

            return workResult;
        }
        finally
        {
            await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<Result<IndexedDbError>> RunAsync(
        IReadOnlyList<string> storeNames,
        TransactionMode mode,
        Func<IIndexedDbTransaction, CancellationToken, ValueTask<Result<IndexedDbError>>> work,
        CancellationToken ct = default)
    {
        var wrapped = await RunAsync<bool>(
            storeNames, mode,
            async (tx, innerCt) =>
            {
                var r = await work(tx, innerCt).ConfigureAwait(false);
                if (r.IsCanceled) return ValueResult<bool, IndexedDbError>.Canceled;
                if (r.TryGetFailure(out var err)) return err;
                return true;
            },
            ct).ConfigureAwait(false);

        if (wrapped.IsCanceled) return Result<IndexedDbError>.Canceled;
        if (wrapped.TryGetFailure(out var failure)) return failure;
        return Result<IndexedDbError>.Success;
    }

    private sealed record BeginTransactionResponse(int TxId);

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
