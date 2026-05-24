using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class IndexedDatabase : IIndexedDatabase
{
    private readonly IndexedDbInterop _interop;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IndexedDatabase> _logger;
    private readonly BlobShareRegistry _shareRegistry;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _dbId;
    private readonly DotNetObjectReference<VersionChangeBridge> _bridgeRef;
    private bool _disposed;

    public string Name { get; }
    public int Version { get; }
    public IReadOnlyList<string> ObjectStoreNames { get; }

    public event Func<ValueTask>? VersionChangeRequested;

    public IndexedDatabase(
        IndexedDbInterop interop,
        ILoggerFactory loggerFactory,
        BlobShareRegistry shareRegistry,
        int dbId,
        string name,
        int version,
        IReadOnlyList<string> objectStoreNames,
        JsonSerializerOptions jsonOptions,
        DotNetObjectReference<VersionChangeBridge> bridgeRef)
    {
        _interop = interop;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<IndexedDatabase>();
        _shareRegistry = shareRegistry;
        _jsonOptions = jsonOptions;
        _dbId = dbId;
        Name = name;
        Version = version;
        ObjectStoreNames = objectStoreNames;
        _bridgeRef = bridgeRef;
    }

    public async ValueTask<ValueResult<long, IndexedDbError>> CountSingleAsync(
        string storeName, KeyRange? range = null, CancellationToken ct = default)
    {
        return await _interop.InvokeAsync<long>(
            "singleOpCount", ct, _dbId, storeName, IndexedDbWireFormat.ToRangeEnvelope(range))
            .ConfigureAwait(false);
    }

    public async ValueTask<ValueResult<T?, IndexedDbError>> JsonGetSingleAsync<T>(
        string storeName, IndexedDbKey key, CancellationToken ct = default)
    {
        var raw = await _interop.InvokeRawAsync(
            "singleOpJsonGet", ct, _dbId, storeName, IndexedDbWireFormat.ToKeyEnvelope(key))
            .ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<T?, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueResult<T?, IndexedDbError>.FromValue(default);
        try
        {
            var value = element.Deserialize<T>(_jsonOptions);
            return ValueResult<T?, IndexedDbError>.FromValue(value);
        }
        catch (JsonException ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to deserialize value from store '{storeName}': {ex.Message}");
        }
    }

    public async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> JsonPutSingleAsync<T>(
        string storeName, T value, IndexedDbKey? key = null, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToElement(value, _jsonOptions);
        var raw = await _interop.InvokeRawAsync(
            "singleOpJsonPut", ct, _dbId, storeName, json, IndexedDbWireFormat.ToKeyEnvelope(key))
            .ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IndexedDbKey, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        try { return IndexedDbWireFormat.FromKeyEnvelope(element); }
        catch (Exception ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to parse effective key from singleOpJsonPut on store '{storeName}': {ex.Message}");
        }
    }

    public async ValueTask<ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>> JsonPutBatchAsync(
        IReadOnlyList<JsonPutItem> items,
        CancellationToken ct = default)
    {
        if (items is null || items.Count == 0)
            return ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>.FromValue(Array.Empty<IndexedDbKey>());

        // Build the JS-side envelope array. Each item carries its own
        // storeName so the JS module's transaction spans every distinct
        // store in the batch. Values serialize via the database's
        // configured JsonSerializerOptions (same code path as
        // JsonPutSingleAsync), so callers don't need to pre-serialize.
        var payload = new object?[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var json = JsonSerializer.SerializeToElement(item.Value, item.Value?.GetType() ?? typeof(object), _jsonOptions);
            payload[i] = new
            {
                storeName = item.StoreName,
                value = json,
                keyEnv = IndexedDbWireFormat.ToKeyEnvelope(item.Key),
            };
        }

        var raw = await _interop.InvokeRawAsync(
            "batchOpJsonPut", ct, _dbId, payload)
            .ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        try
        {
            if (element.ValueKind != JsonValueKind.Array)
                return new IndexedDbError(IndexedDbErrorKind.Data,
                    $"Expected JSON array from batchOpJsonPut, got {element.ValueKind}.");

            var keys = new IndexedDbKey[element.GetArrayLength()];
            var i = 0;
            foreach (var keyElement in element.EnumerateArray())
            {
                keys[i++] = IndexedDbWireFormat.FromKeyEnvelope(keyElement);
            }
            return ValueResult<IReadOnlyList<IndexedDbKey>, IndexedDbError>.FromValue(keys);
        }
        catch (Exception ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to parse effective keys from batchOpJsonPut: {ex.Message}");
        }
    }

    public async ValueTask<ValueResult<IndexedDbBlob?, IndexedDbError>> BlobGetSingleAsync(
        string storeName, IndexedDbKey key, CancellationToken ct = default)
    {
        var raw = await _interop.InvokeRawAsync(
            "singleOpBlobGet", ct, _dbId, storeName, IndexedDbWireFormat.ToKeyEnvelope(key))
            .ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IndexedDbBlob?, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueResult<IndexedDbBlob?, IndexedDbError>.FromValue(null);
        var blobId = element.GetProperty("blobId").GetInt32();
        var contentType = element.GetProperty("contentType").GetString() ?? "application/octet-stream";
        var length = element.GetProperty("length").GetInt64();
        var blob = new IndexedDbBlobImpl(
            _interop,
            _loggerFactory.CreateLogger<IndexedDbBlobImpl>(),
            _shareRegistry,
            blobId, contentType, length);
        return ValueResult<IndexedDbBlob?, IndexedDbError>.FromValue(blob);
    }

    public async ValueTask<ValueResult<IndexedDbKey, IndexedDbError>> BlobPutSingleAsync(
        string storeName, IndexedDbBlob blob, IndexedDbKey? key = null, CancellationToken ct = default)
    {
        if (blob is not IndexedDbBlobImpl impl)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                "Blob must be one constructed via IIndexedDbService.CreateBlobAsync or read from a blob store.");
        }
        var raw = await _interop.InvokeRawAsync(
            "singleOpBlobPut", ct, _dbId, storeName, impl.BlobId, IndexedDbWireFormat.ToKeyEnvelope(key))
            .ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<IndexedDbKey, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;
        try { return IndexedDbWireFormat.FromKeyEnvelope(element); }
        catch (Exception ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to parse effective key from singleOpBlobPut on store '{storeName}': {ex.Message}");
        }
    }

    public async ValueTask<Result<IndexedDbError>> DeleteSingleAsync(
        string storeName, IndexedDbKey key, CancellationToken ct = default)
    {
        return await _interop.InvokeVoidAsync(
            "singleOpDelete", ct, _dbId, storeName, IndexedDbWireFormat.ToKeyEnvelope(key))
            .ConfigureAwait(false);
    }

    public async ValueTask<Result<IndexedDbError>> ClearStoresAsync(
        IReadOnlyList<string> storeNames, CancellationToken ct = default)
    {
        if (storeNames.Count == 0) return Result<IndexedDbError>.Success;
        return await _interop.InvokeVoidAsync(
            "clearStoresAtomic", ct, _dbId, storeNames.ToArray())
            .ConfigureAwait(false);
    }

    public async ValueTask<ValueResult<IReadOnlyList<AdoptedInputFile>, IndexedDbError>>
        AdoptInputElementFilesAsync(
            ElementReference inputElement,
            string storeName,
            AdoptInputFilesOptions options,
            CancellationToken ct = default)
    {
        var jsOptions = new
        {
            acceptedTypes = options.AcceptedTypes,
            maxBytes = options.MaxBytes,
        };

        var raw = await _interop.InvokeRawAsync(
            "adoptInputElementFiles", ct, inputElement, _dbId, storeName, jsOptions)
            .ConfigureAwait(false);
        if (raw.IsCanceled)
            return ValueResult<IReadOnlyList<AdoptedInputFile>, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element))
            return raw.Error.Error;

        AdoptInputFilesPayload? payload;
        try
        {
            payload = element.Deserialize<AdoptInputFilesPayload>(IndexedDbWireFormat.DefaultJsonOptions);
        }
        catch (JsonException ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                $"Failed to parse adoptInputElementFiles result: {ex.Message}");
        }
        if (payload?.Items is null)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                "adoptInputElementFiles returned no items array.");
        }

        var results = new List<AdoptedInputFile>(payload.Items.Count);
        foreach (var item in payload.Items)
        {
            IndexedDbBlob? blob = null;
            Guid? key = null;
            if (item.Error is null && item.BlobId is int blobId && Guid.TryParse(item.Key, out var parsed))
            {
                key = parsed;
                blob = new IndexedDbBlobImpl(
                    _interop,
                    _loggerFactory.CreateLogger<IndexedDbBlobImpl>(),
                    _shareRegistry,
                    blobId,
                    item.ContentType ?? "application/octet-stream",
                    item.Length);
            }
            results.Add(new AdoptedInputFile(
                Filename: item.Filename ?? string.Empty,
                ContentType: item.ContentType ?? string.Empty,
                Length: item.Length,
                Key: key,
                Blob: blob,
                Error: item.Error));
        }
        return ValueResult<IReadOnlyList<AdoptedInputFile>, IndexedDbError>.FromValue(results);
    }

    // Mirror of the JS envelope for adoptInputElementFiles. Per-file outcome
    // carries either (BlobId + Key) on success or (Error) on failure.
    private sealed record AdoptInputFilesPayload(
        [property: JsonPropertyName("items")] List<AdoptInputFileItem> Items);

    private sealed record AdoptInputFileItem(
        [property: JsonPropertyName("filename")] string? Filename,
        [property: JsonPropertyName("contentType")] string? ContentType,
        [property: JsonPropertyName("length")] long Length,
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("blobId")] int? BlobId,
        [property: JsonPropertyName("error")] string? Error);

    internal async ValueTask RaiseVersionChangeRequestedAsync()
    {
        var handler = VersionChangeRequested;
        if (handler is null) return;

        foreach (var subscriber in handler.GetInvocationList().Cast<Func<ValueTask>>())
        {
            try
            {
                await subscriber.Invoke().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A failing subscriber must not break the others; log and continue.
                _logger.LogError(ex,
                    "VersionChangeRequested subscriber for database '{DatabaseName}' threw.",
                    Name);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        var close = await _interop
            .InvokeVoidAsync("closeDatabase", CancellationToken.None, _dbId)
            .ConfigureAwait(false);
        if (close.TryGetFailure(out var error))
        {
            _logger.LogWarning(
                "closeDatabase for '{DatabaseName}' (handle {DbId}) returned [{Kind}] {Message}.",
                Name, _dbId, error.Kind, error.Message);
        }

        _bridgeRef.Dispose();
    }
}
