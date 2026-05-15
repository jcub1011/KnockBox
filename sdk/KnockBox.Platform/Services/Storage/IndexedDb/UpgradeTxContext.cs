using System.Text.Json;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// <see cref="ITxContext"/> implementation for data ops invoked from within
/// a <see cref="UpgradeContext"/>. The underlying upgrade transaction lives
/// for as long as the user's <see cref="Core.Services.Storage.IndexedDb.UpgradeHandler"/>
/// is running — JS keeps it alive via the original <c>onupgradeneeded</c>
/// callback's pending Promise — so <see cref="IsActive"/> is simply tied to
/// the parent context flag, not to a per-tx commit/abort state.
/// </summary>
internal sealed class UpgradeTxContext : ITxContext
{
    private readonly Func<bool> _isActive;

    public IndexedDbInterop Interop { get; }
    public int TxId { get; }
    public JsonSerializerOptions JsonOptions { get; }
    public bool IsActive => _isActive();

    public UpgradeTxContext(
        IndexedDbInterop interop,
        int upgradeTxId,
        JsonSerializerOptions jsonOptions,
        Func<bool> isActive)
    {
        Interop = interop;
        TxId = upgradeTxId;
        JsonOptions = jsonOptions;
        _isActive = isActive;
    }

    /// <summary>
    /// Index metadata is not exposed during an upgrade — the schema is being
    /// mutated in flight and the per-index <c>keyPath</c> / <c>unique</c> /
    /// <c>multiEntry</c> values aren't snapshotted until the upgrade tx
    /// commits. <see cref="IObjectStore{TValue}.Index"/> against this context
    /// surfaces an <see cref="InvalidOperationException"/> as a result.
    /// </summary>
    public bool TryGetIndexSchema(string storeName, string indexName, out IndexSchema schema)
    {
        schema = default!;
        return false;
    }
}
