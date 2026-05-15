using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

/// <summary>
/// Shared helpers for IndexedDB unit tests. The Moq subclass of
/// <see cref="IndexedDbInterop"/> intercepts the three Invoke* virtuals so
/// tests don't have to stand up an <see cref="IJSRuntime"/>.
/// </summary>
internal static class IndexedDbTestHelpers
{
    public static Mock<IndexedDbInterop> NewInteropMock()
        => new(new Mock<IJSRuntime>().Object, NullLogger<IndexedDbInterop>.Instance) { CallBase = false };

    public static void SetupVoidSuccess(this Mock<IndexedDbInterop> mock, string method)
    {
        mock.Setup(x => x.InvokeVoidAsync(method, It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));
    }

    public static void SetupVoidFailure(this Mock<IndexedDbInterop> mock, string method, IndexedDbError error)
    {
        mock.Setup(x => x.InvokeVoidAsync(method, It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(error));
    }

    public static void SetupRawSuccess(this Mock<IndexedDbInterop> mock, string method, JsonElement element)
    {
        mock.Setup(x => x.InvokeRawAsync(method, It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<JsonElement, IndexedDbError>>(element));
    }

    public static void SetupRawSuccess(this Mock<IndexedDbInterop> mock, string method, string json)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        SetupRawSuccess(mock, method, element);
    }

    public static void SetupRawFailure(this Mock<IndexedDbInterop> mock, string method, IndexedDbError error)
    {
        mock.Setup(x => x.InvokeRawAsync(method, It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<JsonElement, IndexedDbError>>(error));
    }

    public static void SetupRawCanceled(this Mock<IndexedDbInterop> mock, string method)
    {
        mock.Setup(x => x.InvokeRawAsync(method, It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<JsonElement, IndexedDbError>>(
                ValueResult<JsonElement, IndexedDbError>.Canceled));
    }

    public static void SetupTypedSuccess<T>(this Mock<IndexedDbInterop> mock, string method, T value)
    {
        mock.Setup(x => x.InvokeAsync<T>(method, It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<T, IndexedDbError>>(
                ValueResult<T, IndexedDbError>.FromValue(value)));
    }

    public static void SetupTypedFailure<T>(this Mock<IndexedDbInterop> mock, string method, IndexedDbError error)
    {
        mock.Setup(x => x.InvokeAsync<T>(method, It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<ValueResult<T, IndexedDbError>>(error));
    }

    public static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    /// <summary>Wire-format key envelope for a string key.</summary>
    public static string StringKeyJson(string value)
        => $"{{\"kind\":\"string\",\"value\":{JsonSerializer.Serialize(value)}}}";

    /// <summary>Wire-format key envelope for a number key.</summary>
    public static string NumberKeyJson(double value)
        => $"{{\"kind\":\"number\",\"value\":{JsonSerializer.Serialize(value)}}}";

    public static BlobShareRegistry NewRegistry() => new(NullLogger<BlobShareRegistry>.Instance);

}
