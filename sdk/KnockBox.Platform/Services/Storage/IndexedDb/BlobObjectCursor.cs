using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class BlobObjectCursor
    : IBlobObjectCursor, IAsyncEnumerator<CursorEntry<IndexedDbBlob>>
{
    private readonly ITxContext _tx;
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _cursorId;
    private readonly bool _hasBufferedFirst;
    private CursorEntry<IndexedDbBlob>? _current;
    private bool _firstYielded;
    private bool _disposed;
    private CancellationToken _enumCt;

    public CursorEntry<IndexedDbBlob>? Current => _current;

    public BlobObjectCursor(
        ITxContext tx,
        ILoggerFactory loggerFactory,
        int cursorId,
        CursorEntry<IndexedDbBlob>? firstEntry)
    {
        _tx = tx;
        _loggerFactory = loggerFactory;
        _cursorId = cursorId;
        if (firstEntry.HasValue)
        {
            _current = firstEntry;
            _hasBufferedFirst = true;
        }
    }

    public async ValueTask<bool> MoveNextAsync(CancellationToken ct = default)
    {
        if (_disposed) return false;
        if (_hasBufferedFirst && !_firstYielded)
        {
            _firstYielded = true;
            return _current.HasValue;
        }
        var step = await CursorRpc.ContinueAsync(_tx, _cursorId, null, ct).ConfigureAwait(false);
        if (!step.TryGetSuccess(out var resp) || resp.Done || resp.Entry is null)
        {
            _current = null;
            return false;
        }
        _current = ParseEntry(_tx.Interop, _loggerFactory, resp.Entry.Value);
        return true;
    }

    public async ValueTask<Result<IndexedDbError>> AdvanceAsync(int count, CancellationToken ct = default)
    {
        if (_disposed) return new IndexedDbError(IndexedDbErrorKind.TransactionInactive, "Cursor is disposed.");
        var step = await CursorRpc.AdvanceAsync(_tx, _cursorId, count, ct).ConfigureAwait(false);
        if (!step.TryGetSuccess(out var resp))
        {
            if (step.IsCanceled) return Result<IndexedDbError>.Canceled;
            return step.Error.Error;
        }
        if (resp.Done || resp.Entry is null) { _current = null; return Result<IndexedDbError>.Success; }
        _current = ParseEntry(_tx.Interop, _loggerFactory, resp.Entry.Value);
        return Result<IndexedDbError>.Success;
    }

    public async ValueTask<Result<IndexedDbError>> ContinueAsync(
        IndexedDbKey? key = null, CancellationToken ct = default)
    {
        if (_disposed) return new IndexedDbError(IndexedDbErrorKind.TransactionInactive, "Cursor is disposed.");
        var step = await CursorRpc.ContinueAsync(_tx, _cursorId, key, ct).ConfigureAwait(false);
        if (!step.TryGetSuccess(out var resp))
        {
            if (step.IsCanceled) return Result<IndexedDbError>.Canceled;
            return step.Error.Error;
        }
        if (resp.Done || resp.Entry is null) { _current = null; return Result<IndexedDbError>.Success; }
        _current = ParseEntry(_tx.Interop, _loggerFactory, resp.Entry.Value);
        return Result<IndexedDbError>.Success;
    }

    public async ValueTask<Result<IndexedDbError>> UpdateAsync(IndexedDbBlob value, CancellationToken ct = default)
    {
        if (!_tx.IsActive) return TxInactive.NoValue();
        if (value is not IndexedDbBlobImpl impl)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                "Blob must be one constructed via IIndexedDbService.CreateBlobAsync or read from a blob store.");
        }
        return await _tx.Interop.InvokeVoidAsync(
            "cursorUpdateBlob", ct, _cursorId, impl.BlobId).ConfigureAwait(false);
    }

    public ValueTask<Result<IndexedDbError>> DeleteAsync(CancellationToken ct = default)
        => CursorRpc.DeleteAsync(_tx, _cursorId, ct);

    public IAsyncEnumerator<CursorEntry<IndexedDbBlob>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        _enumCt = cancellationToken;
        return this;
    }

    CursorEntry<IndexedDbBlob> IAsyncEnumerator<CursorEntry<IndexedDbBlob>>.Current =>
        _current ?? throw new InvalidOperationException(
            "Cursor has no current entry. MoveNextAsync must be called first.");

    ValueTask<bool> IAsyncEnumerator<CursorEntry<IndexedDbBlob>>.MoveNextAsync() => MoveNextAsync(_enumCt);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await CursorRpc.ReleaseAsync(_tx, _cursorId).ConfigureAwait(false);
    }

    internal static CursorEntry<IndexedDbBlob> ParseEntry(
        IndexedDbInterop interop, ILoggerFactory loggerFactory, JsonElement entry)
    {
        var key = IndexedDbWireFormat.FromKeyEnvelope(entry.GetProperty("key"));
        var primaryKey = IndexedDbWireFormat.FromKeyEnvelope(entry.GetProperty("primaryKey"));
        var v = entry.GetProperty("value");
        var blobId = v.GetProperty("blobId").GetInt32();
        var contentType = v.GetProperty("contentType").GetString() ?? "application/octet-stream";
        var length = v.GetProperty("length").GetInt64();
        var blob = new IndexedDbBlobImpl(
            interop,
            loggerFactory.CreateLogger<IndexedDbBlobImpl>(),
            blobId, contentType, length);
        return new CursorEntry<IndexedDbBlob>(key, primaryKey, blob);
    }
}
