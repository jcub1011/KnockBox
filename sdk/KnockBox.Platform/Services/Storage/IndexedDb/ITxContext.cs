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
}
