using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// An open database connection. Disposing releases the underlying JS
    /// reference and unblocks pending <c>versionchange</c> transactions from
    /// other tabs.
    /// </summary>
    public interface IIndexedDatabase : IAsyncDisposable
    {
        string Name { get; }
        int Version { get; }
        IReadOnlyList<string> ObjectStoreNames { get; }

        /// <summary>
        /// Runs <paramref name="work"/> inside a transaction, committing on a
        /// successful result and aborting on failure or exception. Awaits
        /// <see cref="IIndexedDbTransaction.Completed"/> before returning, so
        /// a successful result implies durable commit. The preferred entry
        /// point for most callers.
        /// </summary>
        ValueTask<ValueResult<T, IndexedDbError>> RunAsync<T>(
            IReadOnlyList<string> storeNames,
            TransactionMode mode,
            Func<IIndexedDbTransaction, CancellationToken, ValueTask<ValueResult<T, IndexedDbError>>> work,
            CancellationToken ct = default);

        /// <summary>
        /// Runs <paramref name="work"/> inside a transaction with no result
        /// value. Same commit/abort/await-completion semantics as the generic
        /// overload.
        /// </summary>
        ValueTask<Result<IndexedDbError>> RunAsync(
            IReadOnlyList<string> storeNames,
            TransactionMode mode,
            Func<IIndexedDbTransaction, CancellationToken, ValueTask<Result<IndexedDbError>>> work,
            CancellationToken ct = default);

        /// <summary>
        /// Fires when another connection (typically a different browser tab)
        /// requests an upgrade and we must close to let it proceed. Handlers
        /// should drop any pending work and dispose this database promptly.
        /// </summary>
        event Func<ValueTask>? VersionChangeRequested;

        // ─── Atomic single-op transactions ────────────────────────────────────
        //
        // Each begins a tx, issues one IDB request, and resolves on the JS-side
        // tx.oncomplete — the entire lifecycle stays inside one JS Promise.
        // Prefer these over RunAsync for one-shot reads / writes: under the
        // IDB v3 spec the transaction's active flag is reset between event-
        // loop tasks, so any store call issued from C# after the begin-tx
        // SignalR round-trip throws TransactionInactiveError. The atomic
        // routines sidestep that entirely.

        /// <summary>Counts entries in <paramref name="storeName"/> (optionally bounded by <paramref name="range"/>) in a single atomic readonly transaction.</summary>
        ValueTask<ValueResult<long, IndexedDbError>> CountSingleAsync(
            string storeName, KeyRange? range = null, CancellationToken ct = default);

        /// <summary>Reads a single JSON record by key in a single atomic readonly transaction. Returns <see langword="null"/> on miss.</summary>
        ValueTask<ValueResult<T?, IndexedDbError>> JsonGetSingleAsync<T>(
            string storeName, IndexedDbKey key, CancellationToken ct = default);

        /// <summary>Writes a single JSON record under <paramref name="key"/> in a single atomic readwrite transaction.</summary>
        ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> JsonPutSingleAsync<T>(
            string storeName, T value, IndexedDbKey? key = null, CancellationToken ct = default);

        /// <summary>Reads a single blob record by key in a single atomic readonly transaction. Returns <see langword="null"/> on miss.</summary>
        ValueTask<ValueResult<IndexedDbBlob?, IndexedDbError>> BlobGetSingleAsync(
            string storeName, IndexedDbKey key, CancellationToken ct = default);

        /// <summary>Writes a single blob under <paramref name="key"/> in a single atomic readwrite transaction.</summary>
        ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> BlobPutSingleAsync(
            string storeName, IndexedDbBlob blob, IndexedDbKey? key = null, CancellationToken ct = default);

        /// <summary>Deletes a single record by key in a single atomic readwrite transaction. No-op on miss.</summary>
        ValueTask<Result<IndexedDbError>> DeleteSingleAsync(
            string storeName, IndexedDbKey key, CancellationToken ct = default);

        /// <summary>Clears the named stores in a single atomic readwrite transaction.</summary>
        ValueTask<Result<IndexedDbError>> ClearStoresAsync(
            IReadOnlyList<string> storeNames, CancellationToken ct = default);
    }
}
