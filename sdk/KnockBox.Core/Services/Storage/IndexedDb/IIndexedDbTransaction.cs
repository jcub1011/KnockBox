using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// A live IndexedDB transaction. Must be disposed: an explicit
    /// <see cref="CommitAsync"/> finalizes pending operations and the
    /// <see cref="Completed"/> task signals the durable <c>oncomplete</c>
    /// callback; dropping the handle without committing aborts the transaction,
    /// matching <c>IDBTransaction</c> semantics.
    /// </summary>
    public interface IIndexedDbTransaction : IAsyncDisposable
    {
        TransactionMode Mode { get; }

        IReadOnlyList<string> StoreNames { get; }

        /// <summary><see langword="true"/> until <see cref="CommitAsync"/>,
        /// <see cref="AbortAsync"/>, or <see cref="DisposeAsync"/> runs.</summary>
        bool IsActive { get; }

        /// <summary>
        /// Completes when the JS-side <c>oncomplete</c> event fires (durable
        /// commit) and faults with an <see cref="IndexedDbError"/>-bearing
        /// exception on <c>onerror</c> / <c>onabort</c>. Equivalent to idb's
        /// <c>tx.done</c>; await this before navigating away from a page that
        /// must have persisted its writes.
        /// </summary>
        Task Completed { get; }

        /// <summary>Typed POCO view of a store named in <see cref="StoreNames"/>.</summary>
        IObjectStore<TValue> ObjectStore<TValue>(string name);

        /// <summary>Untyped JSON view of a store named in <see cref="StoreNames"/>.</summary>
        IJsonObjectStore JsonObjectStore(string name);

        /// <summary>Blob view of a store named in <see cref="StoreNames"/>.</summary>
        IBlobObjectStore BlobObjectStore(string name);

        /// <summary>
        /// Initiates commit of the transaction. After this returns successfully
        /// the transaction is no longer <see cref="IsActive"/>, but durable
        /// completion is only guaranteed once <see cref="Completed"/> finishes.
        /// </summary>
        ValueTask<Result<IndexedDbError>> CommitAsync(CancellationToken ct = default);

        /// <summary>
        /// Aborts the transaction, discarding all pending changes. Idempotent.
        /// </summary>
        ValueTask AbortAsync(CancellationToken ct = default);
    }
}
