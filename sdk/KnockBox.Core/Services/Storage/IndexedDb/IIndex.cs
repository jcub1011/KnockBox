using System.Text.Json;
using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Typed index attached to an object store. Lookups and ranges use the
    /// indexed property value as the key. A missing record is represented as a
    /// <see langword="null"/> success result.
    /// </summary>
    public interface IIndex<TValue>
    {
        string Name { get; }
        KeyPath KeyPath { get; }
        bool Unique { get; }

        /// <summary>
        /// When <see langword="true"/>, an indexed array-valued property
        /// contributes one entry per element rather than one entry for the
        /// array as a whole.
        /// </summary>
        bool MultiEntry { get; }

        ValueTask<ValueResult<TValue?, IndexedDbError>> GetAsync(
            IndexedDbKey key,
            CancellationToken ct = default);

        ValueTask<ValueResult<IReadOnlyList<TValue>, IndexedDbError>> GetAllAsync(
            KeyRange? range = null,
            int? count = null,
            CancellationToken ct = default);

        /// <summary>Returns matching primary keys without materializing values.</summary>
        ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
            KeyRange? range = null,
            int? count = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<long, IndexedDbError>> CountAsync(
            KeyRange? range = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<IIndexedDbCursor<TValue>, IndexedDbError>> OpenCursorAsync(
            KeyRange? range = null,
            CursorDirection direction = CursorDirection.Next,
            CancellationToken ct = default);

        /// <summary>
        /// Opens a key-only cursor. Cheap on indexes because the underlying
        /// record never has to be loaded.
        /// </summary>
        ValueTask<ValueResult<IIndexedDbKeyCursor, IndexedDbError>> OpenKeyCursorAsync(
            KeyRange? range = null,
            CursorDirection direction = CursorDirection.Next,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Untyped peer of <see cref="IIndex{TValue}"/> for use with
    /// <see cref="IJsonObjectStore"/>.
    /// </summary>
    public interface IIndex
    {
        string Name { get; }
        KeyPath KeyPath { get; }
        bool Unique { get; }
        bool MultiEntry { get; }

        ValueTask<ValueResult<JsonElement?, IndexedDbError>> GetAsync(
            IndexedDbKey key,
            CancellationToken ct = default);

        ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> GetAllKeysAsync(
            KeyRange? range = null,
            int? count = null,
            CancellationToken ct = default);

        ValueTask<ValueResult<long, IndexedDbError>> CountAsync(
            KeyRange? range = null,
            CancellationToken ct = default);

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
