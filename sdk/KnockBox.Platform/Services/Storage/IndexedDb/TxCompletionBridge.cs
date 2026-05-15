using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// JS-invokable sink for the three terminal events of an IDB transaction.
/// One bridge per transaction; the resulting <see cref="CompletedTask"/>
/// is exposed via <see cref="IIndexedDbTransaction.Completed"/>.
/// </summary>
internal sealed class TxCompletionBridge
{
    private readonly TaskCompletionSource _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task CompletedTask => _tcs.Task;

    [JSInvokable]
    public void OnComplete() => _tcs.TrySetResult();

    [JSInvokable]
    public void OnError(string? jsName, string? message)
    {
        var kind = IndexedDbErrorMapper.ParseKind(jsName);
        var err = new IndexedDbError(
            kind == IndexedDbErrorKind.Unknown && jsName is null ? IndexedDbErrorKind.Aborted : kind,
            message ?? "Transaction errored",
            jsName);
        _tcs.TrySetException(new IndexedDbTransactionException(err));
    }

    [JSInvokable]
    public void OnAbort()
    {
        var err = new IndexedDbError(IndexedDbErrorKind.Aborted, "Transaction aborted.");
        _tcs.TrySetException(new IndexedDbTransactionException(err));
    }
}
