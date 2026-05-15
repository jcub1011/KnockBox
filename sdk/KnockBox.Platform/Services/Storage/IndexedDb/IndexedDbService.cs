using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class IndexedDbService : IIndexedDbService, IAsyncDisposable
{
    private readonly IndexedDbInterop _interop;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IndexedDbService> _logger;

    public IndexedDbService(IJSRuntime jsRuntime, ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<IndexedDbService>();
        _interop = new IndexedDbInterop(jsRuntime, loggerFactory.CreateLogger<IndexedDbInterop>());
    }

    public async ValueTask<ValueResult<IIndexedDatabase, IndexedDbError>> OpenAsync(
        IndexedDbSchema schema, CancellationToken ct = default)
    {
        var bridge = new VersionChangeBridge(
            _interop,
            _loggerFactory.CreateLogger<VersionChangeBridge>(),
            schema);
        var bridgeRef = DotNetObjectReference.Create(bridge);

        var hasUpgrade = schema.OnUpgrade is not null;
        var result = await _interop.InvokeAsync<OpenDatabaseResponse>(
            "openDatabase", ct, schema.Name, schema.Version, hasUpgrade, bridgeRef)
            .ConfigureAwait(false);

        if (!result.TryGetSuccess(out var resp))
        {
            bridgeRef.Dispose();
            if (result.IsCanceled)
                return ValueResult<IIndexedDatabase, IndexedDbError>.Canceled;
            var err = result.Error.Error;
            _logger.LogError(
                "Opening IndexedDB '{DatabaseName}' v{Version} failed: [{Kind}] {Message} (jsName: {JsName}).",
                schema.Name, schema.Version, err.Kind, err.Message, err.JsName);
            return err;
        }

        var db = new IndexedDatabase(
            _interop,
            _loggerFactory,
            resp.DbId, schema.Name, resp.Version,
            resp.ObjectStoreNames,
            schema.JsonOptions ?? IndexedDbWireFormat.DefaultJsonOptions,
            resp.Schema ?? new Dictionary<string, StoreSchema>(),
            bridgeRef);
        bridge.AttachDatabase(db);
        return db;
    }

    public async ValueTask<Result<IndexedDbError>> DeleteDatabaseAsync(string name, CancellationToken ct = default)
    {
        var result = await _interop.InvokeVoidAsync("deleteDatabase", ct, name).ConfigureAwait(false);
        if (result.TryGetFailure(out var err))
        {
            _logger.LogError(
                "Deleting IndexedDB '{DatabaseName}' failed: [{Kind}] {Message} (jsName: {JsName}).",
                name, err.Kind, err.Message, err.JsName);
        }
        return result;
    }

    public async ValueTask<ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>> ListDatabasesAsync(
        CancellationToken ct = default)
    {
        var result = await _interop.InvokeAsync<ListDatabasesResponse>("listDatabases", ct)
            .ConfigureAwait(false);
        if (!result.TryGetSuccess(out var resp))
        {
            if (result.IsCanceled)
                return ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>.Canceled;
            var err = result.Error.Error;
            _logger.LogWarning(
                "Listing IndexedDB databases failed: [{Kind}] {Message} (jsName: {JsName}).",
                err.Kind, err.Message, err.JsName);
            return err;
        }

        IReadOnlyList<DatabaseInfo> infos = resp.Infos
            .Select(i => new DatabaseInfo(i.Name, i.Version))
            .ToList();
        return ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>.FromValue(infos);
    }

    public ValueTask<IndexedDbBlob> CreateBlobAsync(
        ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default)
        => throw new NotImplementedException("Blob support lands in Phase 4 of the IndexedDB rollout.");

    public ValueTask<IndexedDbBlob> CreateBlobAsync(
        Stream stream, long length, string contentType, bool leaveOpen = false, CancellationToken ct = default)
        => throw new NotImplementedException("Blob support lands in Phase 4 of the IndexedDB rollout.");

    public ValueTask DisposeAsync() => _interop.DisposeAsync();
}

internal sealed record OpenDatabaseResponse(
    int DbId,
    int Version,
    IReadOnlyList<string> ObjectStoreNames,
    Dictionary<string, StoreSchema>? Schema);
internal sealed record ListDatabasesResponse(IReadOnlyList<DatabaseInfoEntry> Infos);
internal sealed record DatabaseInfoEntry(string Name, int Version);
