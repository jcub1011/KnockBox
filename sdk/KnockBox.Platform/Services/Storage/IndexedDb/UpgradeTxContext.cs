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
}
