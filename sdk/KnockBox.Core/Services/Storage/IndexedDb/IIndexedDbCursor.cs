using System.Text.Json;
using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Typed asynchronous cursor over an object store or index. Iterate with
    /// <c>await foreach</c> for the common case, or step manually via
    /// <see cref="MoveNextAsync"/> to inspect / mutate the current entry.
    /// Disposing releases the underlying JS reference and ends iteration.
    /// </summary>
    public interface IIndexedDbCursor<TValue> : IAsyncEnumerable<CursorEntry<TValue>>, IAsyncDisposable
    {
        /// <summary>The current entry, or <see langword="null"/> before the first
        /// <see cref="MoveNextAsync"/> or after the cursor is exhausted.</summary>
        CursorEntry<TValue>? Current { get; }

        /// <summary>
        /// Advances to the next record. Returns <see langword="false"/> when
        /// the cursor is exhausted.
        /// </summary>
        ValueTask<bool> MoveNextAsync(CancellationToken ct = default);

        /// <summary>
        /// Skips <paramref name="count"/> records forward without materializing them.
        /// </summary>
        ValueTask<Result<IndexedDbError>> AdvanceAsync(int count, CancellationToken ct = default);

        /// <summary>
        /// Continues iteration starting at <paramref name="key"/> (or just past
        /// it when <paramref name="key"/> equals the cursor's current key).
        /// Passing <see langword="null"/> is equivalent to <see cref="MoveNextAsync"/>.
        /// </summary>
        ValueTask<Result<IndexedDbError>> ContinueAsync(IndexedDbKey? key = null, CancellationToken ct = default);

        /// <summary>
        /// Replaces the record under the cursor. Read-write transactions only.
        /// </summary>
        ValueTask<Result<IndexedDbError>> UpdateAsync(TValue value, CancellationToken ct = default);

        /// <summary>
        /// Deletes the record under the cursor. Read-write transactions only.
        /// </summary>
        ValueTask<Result<IndexedDbError>> DeleteAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Untyped JSON-valued cursor for schema-flexible stores. Yields raw
    /// <see cref="JsonElement"/> values; callers deserialize as needed.
    /// </summary>
    public interface IJsonObjectCursor : IAsyncEnumerable<CursorEntry<JsonElement>>, IAsyncDisposable
    {
        CursorEntry<JsonElement>? Current { get; }
        ValueTask<bool> MoveNextAsync(CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> AdvanceAsync(int count, CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> ContinueAsync(IndexedDbKey? key = null, CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> UpdateAsync(JsonElement value, CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> DeleteAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Cursor over a blob-valued store. Each yielded blob holds a live JS
    /// reference and must be disposed; otherwise object URLs and ALC handles
    /// leak.
    /// </summary>
    public interface IBlobObjectCursor : IAsyncEnumerable<CursorEntry<IndexedDbBlob>>, IAsyncDisposable
    {
        CursorEntry<IndexedDbBlob>? Current { get; }
        ValueTask<bool> MoveNextAsync(CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> AdvanceAsync(int count, CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> ContinueAsync(IndexedDbKey? key = null, CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> UpdateAsync(IndexedDbBlob value, CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> DeleteAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Key-only cursor. Cheaper than a value cursor when only keys are needed
    /// (e.g. building a key set for batch processing).
    /// </summary>
    public interface IIndexedDbKeyCursor : IAsyncEnumerable<KeyCursorEntry>, IAsyncDisposable
    {
        KeyCursorEntry? Current { get; }
        ValueTask<bool> MoveNextAsync(CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> AdvanceAsync(int count, CancellationToken ct = default);
        ValueTask<Result<IndexedDbError>> ContinueAsync(IndexedDbKey? key = null, CancellationToken ct = default);
    }

    /// <summary>A single key returned by an <see cref="IIndexedDbKeyCursor"/>.</summary>
    public readonly record struct KeyCursorEntry(IndexedDbKey Key, IndexedDbKey PrimaryKey);
}
