using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Wraps the <see cref="IndexedDbError"/> that caused
/// <see cref="IIndexedDbTransaction.Completed"/> to fault. Only thrown into
/// <c>Completed</c>; callers that route through
/// <see cref="IIndexedDatabase.RunAsync{T}"/> see the error surfaced as a
/// failed result instead.
/// </summary>
public sealed class IndexedDbTransactionException : Exception
{
    public IndexedDbError Error { get; }

    public IndexedDbTransactionException(IndexedDbError error)
        : base($"[{error.Kind}] {error.Message}")
    {
        Error = error;
    }
}
