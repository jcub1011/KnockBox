using System.Text.Json;
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
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _upgradeTxId;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly UpgradeTxContext _txContext;
    private readonly List<SchemaOp> _pendingOps = new();
    private readonly Dictionary<string, UpgradeStoreHandle> _storeHandles;
    private readonly List<string> _storeNames;
    private bool _active = true;

    public int OldVersion { get; }
    public int NewVersion { get; }
    public IReadOnlyList<string> ObjectStoreNames => _storeNames;

    public UpgradeContext(
        IndexedDbInterop interop,
        ILoggerFactory loggerFactory,
        int upgradeTxId,
        int oldVersion,
        int newVersion,
        JsonSerializerOptions jsonOptions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> existingSchema)
    {
        _interop = interop;
        _loggerFactory = loggerFactory;
        _upgradeTxId = upgradeTxId;
        _jsonOptions = jsonOptions;
        _txContext = new UpgradeTxContext(interop, upgradeTxId, jsonOptions, () => _active);
        OldVersion = oldVersion;
        NewVersion = newVersion;
        _storeNames = existingSchema.Keys.ToList();
        _storeHandles = new Dictionary<string, UpgradeStoreHandle>(existingSchema.Count);
        foreach (var (name, indexNames) in existingSchema)
        {
            _storeHandles[name] = new UpgradeStoreHandle(this, name, indexNames.ToList());
        }
    }

    internal void Deactivate() => _active = false;

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

    public IObjectStore<TValue> ObjectStore<TValue>(string name)
    {
        EnsureStoreExists(name);
        FlushPendingSchemaOpsBeforeData();
        return new ObjectStore<TValue>(_txContext, name);
    }

    public IJsonObjectStore JsonObjectStore(string name)
    {
        EnsureStoreExists(name);
        FlushPendingSchemaOpsBeforeData();
        return new JsonObjectStore(_txContext, name);
    }

    public IBlobObjectStore BlobObjectStore(string name)
    {
        EnsureStoreExists(name);
        FlushPendingSchemaOpsBeforeData();
        return new BlobObjectStore(_txContext, _loggerFactory, name);
    }

    private void EnsureStoreExists(string name)
    {
        if (!_storeHandles.ContainsKey(name))
            throw new InvalidOperationException(
                $"Object store '{name}' does not exist (or has not yet been created in this upgrade).");
    }

    /// <summary>
    /// Schema mutations are queued by C# but must reach JS before any data op
    /// runs against the affected store. Pending ops are flushed synchronously
    /// from the JS-side <c>applySchemaOpsSync</c>; we instead call into JS to
    /// drain whatever has been queued so far without round-tripping through
    /// the OnUpgrade return value.
    /// </summary>
    private void FlushPendingSchemaOpsBeforeData()
    {
        if (_pendingOps.Count == 0) return;
        var batch = _pendingOps.ToArray();
        _pendingOps.Clear();
        // Fire-and-forget on the JS side: the upgrade tx is still active so
        // the synchronous JS applySchemaOps runs to completion before the next
        // data op fires (it's sequenced on the same SignalR pipe). A failure
        // surfaces on the next data op as TransactionInactive.
        _ = _interop.InvokeVoidAsync(
            "upgradeApplySchemaOps", CancellationToken.None, _upgradeTxId, batch).AsTask();
    }

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
