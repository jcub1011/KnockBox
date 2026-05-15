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
    }
}
