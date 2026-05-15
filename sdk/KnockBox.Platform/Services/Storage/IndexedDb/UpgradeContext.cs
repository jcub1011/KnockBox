using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Schema-mutation op queued by <see cref="UpgradeContext"/>. Sent in bulk to JS
/// during <see cref="UpgradeContext.FlushAsync"/>; the receiver runs the ops in
/// order on the live versionchange transaction.
/// </summary>
internal sealed record SchemaOp(
    string Type,
    string Name,
    string? StoreName = null,
    string[]? KeyPath = null,
    bool? AutoIncrement = null,
    bool? Unique = null,
    bool? MultiEntry = null);

internal sealed class UpgradeContext : IUpgradeContext
{
    private readonly IndexedDbInterop _interop;
    private readonly int _upgradeTxId;
    private readonly List<SchemaOp> _pendingOps = new();
    private readonly Dictionary<string, UpgradeStoreHandle> _storeHandles;
    private readonly List<string> _storeNames;

    public int OldVersion { get; }
    public int NewVersion { get; }
    public IReadOnlyList<string> ObjectStoreNames => _storeNames;

    public UpgradeContext(
        IndexedDbInterop interop,
        int upgradeTxId,
        int oldVersion,
        int newVersion,
        IReadOnlyDictionary<string, IReadOnlyList<string>> existingSchema)
    {
        _interop = interop;
        _upgradeTxId = upgradeTxId;
        OldVersion = oldVersion;
        NewVersion = newVersion;
        _storeNames = existingSchema.Keys.ToList();
        _storeHandles = new Dictionary<string, UpgradeStoreHandle>(existingSchema.Count);
        foreach (var (name, indexNames) in existingSchema)
        {
            _storeHandles[name] = new UpgradeStoreHandle(this, name, indexNames.ToList());
        }
    }

    public IUpgradeStoreHandle CreateJsonObjectStore(string name, KeyPath? keyPath = null, bool autoIncrement = false)
        => CreateStore(name, keyPath, autoIncrement);

    public IUpgradeStoreHandle CreateBlobObjectStore(string name, KeyPath? keyPath = null, bool autoIncrement = false)
        => CreateStore(name, keyPath, autoIncrement);

    private IUpgradeStoreHandle CreateStore(string name, KeyPath? keyPath, bool autoIncrement)
    {
        if (_storeHandles.ContainsKey(name))
            throw new InvalidOperationException($"Object store '{name}' already exists in this database.");

        _pendingOps.Add(new SchemaOp(
            Type: "createStore",
            Name: name,
            KeyPath: keyPath?.Paths.ToArray(),
            AutoIncrement: autoIncrement ? true : null));

        _storeNames.Add(name);
        var handle = new UpgradeStoreHandle(this, name, new List<string>());
        _storeHandles[name] = handle;
        return handle;
    }

    public IUpgradeStoreHandle Store(string name)
    {
        if (!_storeHandles.TryGetValue(name, out var handle))
            throw new InvalidOperationException($"Object store '{name}' does not exist in this database.");
        return handle;
    }

    public void DeleteObjectStore(string name)
    {
        if (!_storeHandles.Remove(name))
            throw new InvalidOperationException($"Object store '{name}' does not exist in this database.");
        _storeNames.Remove(name);
        _pendingOps.Add(new SchemaOp(Type: "deleteStore", Name: name));
    }

    // Data-access views deferred to Phase 2 (transactions/stores impl).
    public IObjectStore<TValue> ObjectStore<TValue>(string name)
        => throw new NotImplementedException("Data access during upgrade is implemented in Phase 2.");
    public IJsonObjectStore JsonObjectStore(string name)
        => throw new NotImplementedException("Data access during upgrade is implemented in Phase 2.");
    public IBlobObjectStore BlobObjectStore(string name)
        => throw new NotImplementedException("Data access during upgrade is implemented in Phase 2.");

    internal void Queue(SchemaOp op) => _pendingOps.Add(op);

    /// <summary>
    /// Drains the queued schema ops so <see cref="VersionChangeBridge.OnUpgrade"/>
    /// can return them across the JS boundary. Sending the ops back as the
    /// return value of <c>OnUpgrade</c> (rather than via a separate JS call)
    /// keeps the upgrade transaction alive across the single
    /// <c>await</c> in <c>onupgradeneeded</c>.
    /// </summary>
    internal SchemaOp[] DrainPending()
    {
        var batch = _pendingOps.ToArray();
        _pendingOps.Clear();
        return batch;
    }
}
