using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class IndexedDbService : IIndexedDbService, IAsyncDisposable
{
    private readonly IndexedDbInterop _interop;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IndexedDbService> _logger;
    private readonly BlobShareRegistry _shareRegistry;

    public IndexedDbService(
        IJSRuntime jsRuntime,
        ILoggerFactory loggerFactory,
        BlobShareRegistry shareRegistry)
        : this(new IndexedDbInterop(jsRuntime, loggerFactory.CreateLogger<IndexedDbInterop>()),
               loggerFactory, shareRegistry)
    {
    }

    // Test seam: lets unit tests inject a mocked interop without constructing
    // a real Blazor IJSRuntime. Not part of the public surface.
    internal IndexedDbService(
        IndexedDbInterop interop,
        ILoggerFactory loggerFactory,
        BlobShareRegistry shareRegistry)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<IndexedDbService>();
        _shareRegistry = shareRegistry;
        _interop = interop;
    }

    public async ValueTask<ValueResult<IIndexedDatabase, IndexedDbError>> OpenAsync(
        IndexedDbSchema schema, CancellationToken ct = default)
    {
        var bridge = new VersionChangeBridge(_loggerFactory);
        var bridgeRef = DotNetObjectReference.Create(bridge);

        var declaredStores = SerializeDeclaredStores(schema.Stores);
        var result = await _interop.InvokeAsync<OpenDatabaseResponse>(
            "openDatabase", ct, schema.Name, schema.Version, declaredStores, bridgeRef)
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
            _shareRegistry,
            resp.DbId, schema.Name, resp.Version,
            resp.ObjectStoreNames,
            schema.JsonOptions ?? IndexedDbWireFormat.DefaultJsonOptions,
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

    public async ValueTask<Result<IndexedDbError>> MigrateDatabaseAsync(
        string fromName, string toName, CancellationToken ct = default)
    {
        var result = await _interop.InvokeVoidAsync("migrateDatabase", ct, fromName, toName).ConfigureAwait(false);
        if (result.TryGetFailure(out var err))
        {
            _logger.LogError(
                "Migrating IndexedDB '{FromName}' -> '{ToName}' failed: [{Kind}] {Message} (jsName: {JsName}).",
                fromName, toName, err.Kind, err.Message, err.JsName);
        }
        return result;
    }

    public ValueTask<ValueResult<IndexedDbBlob, IndexedDbError>> CreateBlobAsync(
        ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct = default)
    {
        // Small payloads (one chunk) take the single-call createBlobFromBytes
        // path; anything larger goes through the chunked stream upload to keep
        // a single SignalR message under the receive limit.
        if (bytes.Length <= IndexedDbBlobChunking.ChunkSize)
        {
            return CreateBlobFromBytesAsync(bytes, contentType, ct);
        }
        // Fall through to the stream path with a backing MemoryStream.
        var arr = bytes.ToArray();
        return CreateBlobAsync(new MemoryStream(arr, writable: false), arr.LongLength, contentType, leaveOpen: false, ct);
    }

    private async ValueTask<ValueResult<IndexedDbBlob, IndexedDbError>> CreateBlobFromBytesAsync(
        ReadOnlyMemory<byte> bytes, string contentType, CancellationToken ct)
    {
        var base64 = Convert.ToBase64String(bytes.Span);
        var result = await _interop.InvokeAsync<BlobCreateResponse>(
            "createBlobFromBytes", ct, base64, contentType).ConfigureAwait(false);
        if (!result.TryGetSuccess(out var resp))
        {
            if (result.IsCanceled)
                return ValueResult<IndexedDbBlob, IndexedDbError>.Canceled;
            var err = result.Error.Error;
            _logger.LogError("createBlobFromBytes failed: [{Kind}] {Message}", err.Kind, err.Message);
            return err;
        }
        return new IndexedDbBlobImpl(
            _interop,
            _loggerFactory.CreateLogger<IndexedDbBlobImpl>(),
            _shareRegistry,
            resp.BlobId, contentType, resp.Length);
    }

    public async ValueTask<ValueResult<IndexedDbBlob, IndexedDbError>> CreateBlobAsync(
        Stream stream, long length, string contentType, bool leaveOpen = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

        try
        {
            // DotNetStreamReference hands the C# stream to Blazor, which
            // frames the bytes natively over SignalR (no base64, no JSON
            // envelope). The JS side does one `streamRef.arrayBuffer()`
            // read and constructs a Blob — no per-chunk InvokeAsync loop.
            using var streamRef = new DotNetStreamReference(stream, leaveOpen: true);
            var result = await _interop.InvokeAsync<BlobCreateResponse>(
                "createBlobFromDotNetStream", ct, streamRef, contentType, length).ConfigureAwait(false);

            if (!result.TryGetSuccess(out var resp))
            {
                if (result.IsCanceled)
                    return ValueResult<IndexedDbBlob, IndexedDbError>.Canceled;
                var err = result.Error.Error;
                _logger.LogError("createBlobFromDotNetStream failed: [{Kind}] {Message}", err.Kind, err.Message);
                return err;
            }
            return new IndexedDbBlobImpl(
                _interop,
                _loggerFactory.CreateLogger<IndexedDbBlobImpl>(),
                _shareRegistry,
                resp.BlobId, contentType, resp.Length);
        }
        finally
        {
            // DotNetStreamReference's `leaveOpen: true` prevents Blazor from
            // touching the stream lifecycle — we own it. Honor the caller's
            // request (matching the legacy contract): default `false` means
            // we dispose the source stream once the upload completes or
            // throws.
            if (!leaveOpen)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public ValueTask DisposeAsync() => _interop.DisposeAsync();

    // Projects the declared store list into the camelCase shape consumed by
    // applyDeclaredStoresSync in indexedDbService.js. Returns null when no
    // stores are declared so the JS side can short-circuit.
    private static object?[]? SerializeDeclaredStores(IReadOnlyList<DeclaredStore>? stores)
    {
        if (stores is null || stores.Count == 0) return null;
        var result = new object?[stores.Count];
        for (var i = 0; i < stores.Count; i++)
        {
            var s = stores[i];
            result[i] = new
            {
                name = s.Name,
                kind = s.Kind == DeclaredStoreKind.Blob ? "blob" : "json",
                keyPath = s.KeyPath?.Paths.ToArray(),
                autoIncrement = s.AutoIncrement,
                indexes = s.Indexes?.Select(idx => new
                {
                    name = idx.Name,
                    keyPath = idx.KeyPath.Paths.ToArray(),
                    unique = idx.Unique,
                    multiEntry = idx.MultiEntry,
                }).ToArray(),
            };
        }
        return result;
    }
}

internal sealed record OpenDatabaseResponse(
    int DbId,
    int Version,
    IReadOnlyList<string> ObjectStoreNames);
internal sealed record ListDatabasesResponse(IReadOnlyList<DatabaseInfoEntry> Infos);
internal sealed record DatabaseInfoEntry(string Name, int Version);
