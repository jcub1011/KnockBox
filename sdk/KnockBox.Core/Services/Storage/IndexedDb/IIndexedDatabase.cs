using KnockBox.Core.Primitives.Returns;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// An open database connection. Disposing releases the underlying JS
    /// reference and unblocks pending <c>versionchange</c> transactions from
    /// other tabs.
    /// <para>
    /// Every data op on this surface is a complete one-shot transaction —
    /// the SDK does not expose a multi-step transaction API because Blazor
    /// Server's SignalR round-trip between C# and JS sits outside any IDB
    /// event handler, and per the IDB v3 spec a transaction's active flag is
    /// false outside those handlers (any store method then throws
    /// <c>TransactionInactiveError</c>). The atomic methods below keep the
    /// entire <c>begin → request → oncomplete</c> lifecycle inside one JS
    /// Promise so the active-flag rule is never violated.
    /// </para>
    /// </summary>
    public interface IIndexedDatabase : IAsyncDisposable
    {
        string Name { get; }
        int Version { get; }
        IReadOnlyList<string> ObjectStoreNames { get; }

        /// <summary>
        /// Fires when another connection (typically a different browser tab)
        /// requests an upgrade and we must close to let it proceed. Handlers
        /// should drop any pending work and dispose this database promptly.
        /// </summary>
        event Func<ValueTask>? VersionChangeRequested;

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

        /// <summary>
        /// Iterates the files inside an <c>&lt;input type="file"&gt;</c>
        /// element entirely on the JS side and persists each one into
        /// <paramref name="storeName"/> under a freshly generated GUID key.
        /// The bytes never cross the SignalR boundary — the .NET side only
        /// receives metadata plus a JS-side blob handle per file. Returns
        /// one entry per file in the input's selection order; per-file
        /// failures (type rejected, decode failed, IDB put failed) are
        /// reported as <see cref="AdoptedInputFile.Error"/> rather than
        /// aborting the batch. Successful entries' <see cref="IndexedDbBlob"/>
        /// handles must be disposed by the caller.
        /// </summary>
        ValueTask<ValueResult<IReadOnlyList<AdoptedInputFile>, IndexedDbError>>
            AdoptInputElementFilesAsync(
                ElementReference inputElement,
                string storeName,
                AdoptInputFilesOptions options,
                CancellationToken ct = default);
    }
}
