using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Per-circuit gateway to the browser's IndexedDB API. Registered with
    /// scoped lifetime so the cached JS module reference stays bound to one
    /// Blazor circuit.
    /// </summary>
    public interface IIndexedDbService
    {
        /// <summary>
        /// Opens (and if needed upgrades) the database described by
        /// <paramref name="schema"/>. Disposing the returned handle releases
        /// the underlying JS reference.
        /// </summary>
        ValueTask<ValueResult<IIndexedDatabase, IndexedDbError>> OpenAsync(
            IndexedDbSchema schema,
            CancellationToken ct = default);

        /// <summary>
        /// Permanently deletes a database. No-op when no database with that
        /// name exists at the current origin.
        /// </summary>
        ValueTask<Result<IndexedDbError>> DeleteDatabaseAsync(
            string name,
            CancellationToken ct = default);

        /// <summary>
        /// Lists every database currently known to this origin. Fails with
        /// <see cref="IndexedDbErrorKind.NotSupported"/> on user agents that do
        /// not implement <c>indexedDB.databases()</c> (e.g. older Safari) so
        /// the caller can distinguish "no databases" from "can't tell."
        /// </summary>
        ValueTask<ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>> ListDatabasesAsync(
            CancellationToken ct = default);

        /// <summary>
        /// Allocates a JS-side <c>Blob</c> from a contiguous .NET buffer. On
        /// success the returned blob holds a live JS reference and must be
        /// disposed.
        /// </summary>
        ValueTask<ValueResult<IndexedDbBlob, IndexedDbError>> CreateBlobAsync(
            ReadOnlyMemory<byte> bytes,
            string contentType,
            CancellationToken ct = default);

        /// <summary>
        /// Allocates a JS-side <c>Blob</c> from a streamed source.
        /// <paramref name="stream"/> must be readable and report
        /// <paramref name="length"/> bytes of remaining content. When
        /// <paramref name="leaveOpen"/> is <see langword="false"/>, the stream
        /// is disposed alongside the operation (regardless of success).
        /// </summary>
        ValueTask<ValueResult<IndexedDbBlob, IndexedDbError>> CreateBlobAsync(
            Stream stream,
            long length,
            string contentType,
            bool leaveOpen = false,
            CancellationToken ct = default);
    }
}
