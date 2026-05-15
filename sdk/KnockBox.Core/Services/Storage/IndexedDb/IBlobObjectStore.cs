using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// View of a blob-valued object store. Each record is a single opaque
    /// binary payload. A missing record is represented as a
    /// <see langword="null"/> success result.
    /// </summary>
    /// <remarks>
    /// Blobs returned from this store hold live JS references and must be
    /// disposed (typically via <c>await using</c>) to release them and revoke
    /// any object URLs.
    /// </remarks>
    public interface IBlobObjectStore
    {
        string Name { get; }

        ValueTask<ValueResult<IndexedDbBlob?, IndexedDbError>> GetAsync(
            IndexedDbKey key,
            CancellationToken ct = default);

        ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
            KeyRange? range = null,
            int? count = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> AddAsync(
            IndexedDbBlob blob,
            IndexedDbKey? key = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> PutAsync(
            IndexedDbBlob blob,
            IndexedDbKey? key = null,
            CancellationToken ct = default);

        ValueTask<Result<IndexedDbError>> DeleteAsync(
            IndexedDbKey key,
            CancellationToken ct = default);

        ValueTask<Result<IndexedDbError>> DeleteRangeAsync(
            KeyRange range,
            CancellationToken ct = default);

        ValueTask<Result<IndexedDbError>> ClearAsync(CancellationToken ct = default);

        ValueTask<ValueResult<long, IndexedDbError>> CountAsync(
            KeyRange? range = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<IBlobObjectCursor, IndexedDbError>> OpenCursorAsync(
            KeyRange? range = null,
            CursorDirection direction = CursorDirection.Next,
            CancellationToken ct = default);

        ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
            KeyRange? range = null,
            CursorDirection direction = CursorDirection.Next,
            CancellationToken ct = default);
    }
}
