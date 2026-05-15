using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class IndexedDbTransaction : IIndexedDbTransaction, ITxContext
{
    private readonly ILogger<IndexedDbTransaction> _logger;
    private readonly TxCompletionBridge _bridge;
    private readonly DotNetObjectReference<TxCompletionBridge> _bridgeRef;
    private readonly IReadOnlyDictionary<string, StoreSchema> _schema;
    private readonly object _stateLock = new();
    private bool _active = true;
    private bool _disposed;

    // Public on this internal class so the internal ITxContext interface can
    // bind them — C# requires implementations of (implicitly public) interface
    // members to be public regardless of the interface's own accessibility.
    public IndexedDbInterop Interop { get; }
    public int TxId { get; }
    public JsonSerializerOptions JsonOptions { get; }

    public TransactionMode Mode { get; }
    public IReadOnlyList<string> StoreNames { get; }

    public bool IsActive
    {
        get { lock (_stateLock) return _active; }
    }

    public Task Completed => _bridge.CompletedTask;

    public IndexedDbTransaction(
        IndexedDbInterop interop,
        ILogger<IndexedDbTransaction> logger,
        int txId,
        TransactionMode mode,
        IReadOnlyList<string> storeNames,
        JsonSerializerOptions jsonOptions,
        IReadOnlyDictionary<string, StoreSchema> schema,
        TxCompletionBridge bridge,
        DotNetObjectReference<TxCompletionBridge> bridgeRef)
    {
        Interop = interop;
        _logger = logger;
        TxId = txId;
        Mode = mode;
        StoreNames = storeNames;
        JsonOptions = jsonOptions;
        _schema = schema;
        _bridge = bridge;
        _bridgeRef = bridgeRef;
    }

    public bool TryGetIndexSchema(string storeName, string indexName, out IndexSchema schema)
    {
        schema = default!;
        return _schema.TryGetValue(storeName, out var store)
            && store.Indexes.TryGetValue(indexName, out schema!);
    }

    public IObjectStore<TValue> ObjectStore<TValue>(string name)
    {
        EnsureStoreInScope(name);
        return new ObjectStore<TValue>(this, name);
    }

    public IJsonObjectStore JsonObjectStore(string name)
    {
        EnsureStoreInScope(name);
        return new JsonObjectStore(this, name);
    }

    public IBlobObjectStore BlobObjectStore(string name)
        => throw new NotImplementedException("Blob stores land in Phase 4 of the IndexedDB rollout.");

    public async ValueTask<Result<IndexedDbError>> CommitAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (!_active)
                return Result<IndexedDbError>.Success; // idempotent
            _active = false;
        }

        var result = await Interop.InvokeVoidAsync("commitTransaction", ct, TxId)
            .ConfigureAwait(false);
        if (result.TryGetFailure(out var err))
        {
            _logger.LogError(
                "commitTransaction({TxId}) failed: [{Kind}] {Message}.",
                TxId, err.Kind, err.Message);
        }
        return result;
    }

    public async ValueTask AbortAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (!_active) return;
            _active = false;
        }

        var result = await Interop.InvokeVoidAsync("abortTransaction", ct, TxId)
            .ConfigureAwait(false);
        if (result.TryGetFailure(out var err))
        {
            _logger.LogWarning(
                "abortTransaction({TxId}) failed: [{Kind}] {Message}.",
                TxId, err.Kind, err.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsActive)
        {
            await AbortAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _bridgeRef.Dispose();
    }

    private void EnsureStoreInScope(string name)
    {
        if (!StoreNames.Contains(name))
        {
            throw new InvalidOperationException(
                $"Object store '{name}' is not in this transaction's store list " +
                $"[{string.Join(", ", StoreNames)}]. " +
                "Include it in BeginTransaction(...) / RunAsync(...).");
        }
    }
}
