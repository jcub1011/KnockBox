using System.Text.Json;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Minimum surface a store/cursor/index wrapper needs from its owning
/// transaction. Implemented by both <see cref="IndexedDbTransaction"/>
/// (normal flows) and <see cref="UpgradeTxContext"/> (data ops during an
/// upgrade callback).
/// </summary>
internal interface ITxContext
{
    IndexedDbInterop Interop { get; }
    int TxId { get; }
    JsonSerializerOptions JsonOptions { get; }
    bool IsActive { get; }

    /// <summary>
    /// Looks up the immutable metadata for a previously-defined index on the
    /// given store. Returns <see langword="false"/> when the index does not
    /// exist (or, during an upgrade callback, has not yet been committed to
    /// the schema snapshot the context observes).
    /// </summary>
    bool TryGetIndexSchema(string storeName, string indexName, out IndexSchema schema);
}
