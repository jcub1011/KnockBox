using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// Wraps the ES module at <c>/_content/KnockBox.Platform/js/indexedDbService.js</c>,
/// unwraps the <c>{ok, value, error}</c> envelope returned by every JS export,
/// and translates JS-side disconnections into <see cref="IndexedDbError"/>s
/// instead of leaked exceptions.
/// </summary>
internal class IndexedDbInterop : IAsyncDisposable
{
    private const string ModulePath = "/_content/KnockBox.Platform/js/indexedDbService.js";

    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<IndexedDbInterop> _logger;
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    private bool _disposed;

    public IndexedDbInterop(IJSRuntime jsRuntime, ILogger<IndexedDbInterop> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
        _moduleTask = new Lazy<Task<IJSObjectReference>>(() =>
            _jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask());
    }

    public virtual async ValueTask<ValueResult<JsonElement, IndexedDbError>> InvokeRawAsync(
        string method, CancellationToken ct, params object?[] args)
    {
        try
        {
            var module = await _moduleTask.Value.WaitAsync(ct).ConfigureAwait(false);
            var envelope = await module.InvokeAsync<JsonElement>(method, ct, args).ConfigureAwait(false);
            return UnpackEnvelope(envelope);
        }
        catch (OperationCanceledException)
        {
            return ValueResult<JsonElement, IndexedDbError>.Canceled;
        }
        catch (JSDisconnectedException ex)
        {
            _logger.LogWarning(ex,
                "IndexedDB JS interop call '{Method}' aborted: Blazor circuit disconnected.",
                method);
            return new IndexedDbError(IndexedDbErrorKind.Aborted, "JS runtime disconnected: " + ex.Message);
        }
        catch (JSException ex)
        {
            _logger.LogError(ex,
                "IndexedDB JS interop call '{Method}' threw a JS exception.",
                method);
            return new IndexedDbError(IndexedDbErrorKind.Unknown, "JS exception: " + ex.Message);
        }
    }

    public virtual async ValueTask<ValueResult<T, IndexedDbError>> InvokeAsync<T>(
        string method, CancellationToken ct, params object?[] args)
    {
        var raw = await InvokeRawAsync(method, ct, args).ConfigureAwait(false);
        if (raw.IsCanceled) return ValueResult<T, IndexedDbError>.Canceled;
        if (!raw.TryGetSuccess(out var element)) return raw.Error.Error;

        try
        {
            var value = element.ValueKind == JsonValueKind.Undefined
                ? default
                : element.Deserialize<T>(IndexedDbWireFormat.DefaultJsonOptions);
            return value!;
        }
        catch (JsonException ex)
        {
            return new IndexedDbError(IndexedDbErrorKind.Data,
                "JS payload deserialization failed: " + ex.Message);
        }
    }

    public virtual async ValueTask<Result<IndexedDbError>> InvokeVoidAsync(
        string method, CancellationToken ct, params object?[] args)
    {
        var raw = await InvokeRawAsync(method, ct, args).ConfigureAwait(false);
        if (raw.IsCanceled) return Result<IndexedDbError>.Canceled;
        if (raw.IsSuccess) return Result<IndexedDbError>.Success;
        return raw.Error.Error;
    }

    private static ValueResult<JsonElement, IndexedDbError> UnpackEnvelope(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty("ok", out var okProp))
        {
            return new IndexedDbError(IndexedDbErrorKind.Unknown,
                "Malformed JS envelope: missing 'ok' discriminator.");
        }

        if (okProp.GetBoolean())
        {
            return envelope.TryGetProperty("value", out var value)
                ? value
                : default;
        }

        if (!envelope.TryGetProperty("error", out var errorProp))
        {
            return new IndexedDbError(IndexedDbErrorKind.Unknown,
                "Malformed JS envelope: ok=false with no 'error'.");
        }

        var kind = errorProp.TryGetProperty("kind", out var k) ? k.GetString() : null;
        var msg = errorProp.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        var jsName = errorProp.TryGetProperty("jsName", out var j) ? j.GetString() : null;
        return new IndexedDbError(IndexedDbErrorMapper.ParseKind(kind), msg, jsName);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_moduleTask.IsValueCreated) return;

        try
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Expected when the circuit drops before disposal — debug only.
            _logger.LogDebug("IndexedDB JS module dispose: circuit already disconnected.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IndexedDB JS module disposal failed.");
        }
    }
}
