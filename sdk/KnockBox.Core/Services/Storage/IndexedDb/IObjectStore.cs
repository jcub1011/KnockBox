using System.Text.Json;
using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Typed view of an object store within a transaction. Values cross the JS
    /// boundary via <c>System.Text.Json</c> using the schema's serializer
    /// options. A missing record is represented as a <see langword="null"/>
    /// success result, not an error.
    /// </summary>
    public interface IObjectStore<TValue>
    {
        string Name { get; }

        /// <summary>Reads a record by primary key. Returns <see langword="null"/> when no record exists for <paramref name="key"/>.</summary>
        ValueTask<ValueResult<TValue?, IndexedDbError>> GetAsync(
            IndexedDbKey key,
            CancellationToken ct = default);

        ValueTask<ValueResult<IReadOnlyList<TValue>, IndexedDbError>> GetAllAsync(
            KeyRange? range = null,
            int? count = null,
            CancellationToken ct = default);

        /// <summary>Returns the primary keys matching the range without materializing the values.</summary>
        ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
            KeyRange? range = null,
            int? count = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<long, IndexedDbError>> CountAsync(
            KeyRange? range = null,
            CancellationToken ct = default);

        /// <summary>
        /// Inserts a new record. Fails with <see cref="IndexedDbErrorKind.Constraint"/>
        /// when a record with the same key already exists. The returned value
        /// is the effective key (relevant for auto-increment stores).
        /// </summary>
        ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> AddAsync(
            TValue value,
            IndexedDbKey? key = null,
            CancellationToken ct = default);

        /// <summary>Inserts or replaces a record.</summary>
        ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> PutAsync(
            TValue value,
            IndexedDbKey? key = null,
            CancellationToken ct = default);

        ValueTask<Result<IndexedDbError>> DeleteAsync(
            IndexedDbKey key,
            CancellationToken ct = default);

        ValueTask<Result<IndexedDbError>> DeleteRangeAsync(
            KeyRange range,
            CancellationToken ct = default);

        ValueTask<Result<IndexedDbError>> ClearAsync(CancellationToken ct = default);

        /// <summary>Returns a typed handle to a previously-defined index.</summary>
        IIndex<TValue> Index(string name);

        /// <summary>Opens a cursor over the store's primary key.</summary>
        ValueTask<ValueResult<IIndexedDbCursor<TValue>, IndexedDbError>> OpenCursorAsync(
            KeyRange? range = null,
            CursorDirection direction = CursorDirection.Next,
            CancellationToken ct = default);

        /// <summary>
        /// Opens a key-only cursor that yields keys without materializing values.
        /// Cheaper for large records when only keys are needed.
        /// </summary>
        ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
            KeyRange? range = null,
            CursorDirection direction = CursorDirection.Next,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Schema-flexible store view backed by raw <see cref="JsonElement"/>.
    /// Useful when records do not share a single POCO shape. A missing record
    /// is represented as a <see langword="null"/> success result.
    /// </summary>
    public interface IJsonObjectStore
    {
        string Name { get; }

        ValueTask<ValueResult<JsonElement?, IndexedDbError>> GetAsync(
            IndexedDbKey key,
            CancellationToken ct = default);

        ValueTask<ValueResult<IReadOnlyList<JsonElement>, IndexedDbError>> GetAllAsync(
            KeyRange? range = null,
            int? count = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
            KeyRange? range = null,
            int? count = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> AddAsync(
            JsonElement value,
            IndexedDbKey? key = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> PutAsync(
            JsonElement value,
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

        IIndex Index(string name);

        ValueTask<ValueResult<IJsonObjectCursor, IndexedDbError>> OpenCursorAsync(
            KeyRange? range = null,
            CursorDirection direction = CursorDirection.Next,
            CancellationToken ct = default);

        ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
            KeyRange? range = null,
            CursorDirection direction = CursorDirection.Next,
            CancellationToken ct = default);
    }
}
