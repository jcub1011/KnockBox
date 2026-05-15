using System.Text.Json;
using KnockBox.Core.Services.Storage.IndexedDb;
using KnockBox.Platform.Services.Storage.IndexedDb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;

namespace KnockBox.PlatformTests.Unit.Services.Storage.IndexedDb;

[TestClass]
public sealed class IndexedDbInteropTests
{
    private static (IndexedDbInterop interop, Mock<IJSRuntime> rt, Mock<IJSObjectReference> module) Build(
        bool moduleLoads = true)
    {
        var module = new Mock<IJSObjectReference>();
        var rt = new Mock<IJSRuntime>();
        if (moduleLoads)
        {
            rt.Setup(x => x.InvokeAsync<IJSObjectReference>(
                "import", It.IsAny<object?[]>()))
                .Returns(new ValueTask<IJSObjectReference>(module.Object));
        }
        else
        {
            rt.Setup(x => x.InvokeAsync<IJSObjectReference>(
                "import", It.IsAny<object?[]>()))
                .Throws(new JSException("module failed to load"));
        }
        var interop = new IndexedDbInterop(rt.Object, NullLogger<IndexedDbInterop>.Instance);
        return (interop, rt, module);
    }

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    private static void SetupModuleReturns(Mock<IJSObjectReference> module, string method, JsonElement envelope)
    {
        module.Setup(x => x.InvokeAsync<JsonElement>(
            method, It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<JsonElement>(envelope));
    }

    [TestMethod]
    public async Task InvokeRawAsync_SuccessfulEnvelope_ReturnsValue()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true,\"value\":42}"));

        var result = await interop.InvokeRawAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetSuccess(out var element));
        Assert.AreEqual(42, element.GetInt32());
    }

    [TestMethod]
    public async Task InvokeRawAsync_SuccessfulEnvelope_WithoutValueField_ReturnsDefaultElement()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true}"));

        var result = await interop.InvokeRawAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetSuccess(out var element));
        Assert.AreEqual(JsonValueKind.Undefined, element.ValueKind);
    }

    [TestMethod]
    public async Task InvokeRawAsync_ErrorEnvelope_MapsToIndexedDbError()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo",
            Json("{\"ok\":false,\"error\":{\"kind\":\"QuotaExceeded\",\"message\":\"full\",\"jsName\":\"QuotaExceededError\"}}"));

        var result = await interop.InvokeRawAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.QuotaExceeded, err.Kind);
        Assert.AreEqual("full", err.Message);
        Assert.AreEqual("QuotaExceededError", err.JsName);
    }

    [TestMethod]
    public async Task InvokeRawAsync_MalformedEnvelope_NoOkField_ReturnsUnknown()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"value\":42}"));

        var result = await interop.InvokeRawAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Unknown, err.Kind);
        StringAssert.Contains(err.Message, "ok");
    }

    [TestMethod]
    public async Task InvokeRawAsync_MalformedEnvelope_ErrorWithoutErrorField_ReturnsUnknown()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":false}"));

        var result = await interop.InvokeRawAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Unknown, err.Kind);
        StringAssert.Contains(err.Message, "error");
    }

    [TestMethod]
    public async Task InvokeRawAsync_JsDisconnectedException_MapsToAbortedError()
    {
        var (interop, _, module) = Build();
        module.Setup(x => x.InvokeAsync<JsonElement>(
            "foo", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Throws(new JSDisconnectedException("circuit gone"));

        var result = await interop.InvokeRawAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Aborted, err.Kind);
        StringAssert.Contains(err.Message, "circuit gone");
    }

    [TestMethod]
    public async Task InvokeRawAsync_JsException_MapsToUnknownError()
    {
        var (interop, _, module) = Build();
        module.Setup(x => x.InvokeAsync<JsonElement>(
            "foo", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Throws(new JSException("oops"));

        var result = await interop.InvokeRawAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Unknown, err.Kind);
        StringAssert.Contains(err.Message, "oops");
    }

    [TestMethod]
    public async Task InvokeRawAsync_Cancellation_ReturnsCanceledResult()
    {
        var (interop, _, module) = Build();
        module.Setup(x => x.InvokeAsync<JsonElement>(
            "foo", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Throws(new OperationCanceledException());

        var result = await interop.InvokeRawAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.IsCanceled);
    }

    [TestMethod]
    public async Task InvokeAsyncOfT_HappyPath_Deserializes()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true,\"value\":{\"name\":\"alice\",\"age\":33}}"));

        var result = await interop.InvokeAsync<Person>("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetSuccess(out var p));
        Assert.AreEqual("alice", p.Name);
        Assert.AreEqual(33, p.Age);
    }

    [TestMethod]
    public async Task InvokeAsyncOfT_JsonExceptionOnDeserialize_ReturnsDataError()
    {
        var (interop, _, module) = Build();
        // value is a string but T is int — Deserialize throws JsonException.
        SetupModuleReturns(module, "foo", Json("{\"ok\":true,\"value\":\"not-a-number\"}"));

        var result = await interop.InvokeAsync<int>("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Data, err.Kind);
    }

    [TestMethod]
    public async Task InvokeAsyncOfT_NullValue_ReturnsDefault()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true}"));

        var result = await interop.InvokeAsync<string?>("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetSuccess(out var v));
        Assert.IsNull(v);
    }

    [TestMethod]
    public async Task InvokeVoidAsync_SuccessfulEnvelope_ReturnsSuccess()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true}"));

        var result = await interop.InvokeVoidAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task InvokeVoidAsync_ErrorEnvelope_ReturnsFailure()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo",
            Json("{\"ok\":false,\"error\":{\"kind\":\"Constraint\",\"message\":\"dup\"}}"));

        var result = await interop.InvokeVoidAsync("foo", CancellationToken.None);

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(IndexedDbErrorKind.Constraint, err.Kind);
    }

    [TestMethod]
    public async Task DisposeAsync_BeforeModuleLoaded_DoesNotImportModule()
    {
        var (interop, rt, _) = Build();
        await interop.DisposeAsync();
        rt.Verify(x => x.InvokeAsync<IJSObjectReference>("import", It.IsAny<object?[]>()), Times.Never);
    }

    [TestMethod]
    public async Task DisposeAsync_DisposesModule_AfterUse()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true}"));
        await interop.InvokeVoidAsync("foo", CancellationToken.None);
        module.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        await interop.DisposeAsync();

        module.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [TestMethod]
    public async Task DisposeAsync_TolerantOfJsDisconnected()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true}"));
        await interop.InvokeVoidAsync("foo", CancellationToken.None);
        module.Setup(x => x.DisposeAsync()).Throws(new JSDisconnectedException("gone"));

        // Should not throw.
        await interop.DisposeAsync();
    }

    [TestMethod]
    public async Task DisposeAsync_TolerantOfUnexpectedException()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true}"));
        await interop.InvokeVoidAsync("foo", CancellationToken.None);
        module.Setup(x => x.DisposeAsync()).Throws(new InvalidOperationException("weird"));

        // Should not throw.
        await interop.DisposeAsync();
    }

    [TestMethod]
    public async Task DisposeAsync_IsIdempotent()
    {
        var (interop, _, module) = Build();
        SetupModuleReturns(module, "foo", Json("{\"ok\":true}"));
        await interop.InvokeVoidAsync("foo", CancellationToken.None);
        var disposeCount = 0;
        module.Setup(x => x.DisposeAsync()).Returns(() =>
        {
            disposeCount++;
            return ValueTask.CompletedTask;
        });

        await interop.DisposeAsync();
        await interop.DisposeAsync();

        Assert.AreEqual(1, disposeCount);
    }

    public sealed record Person(string Name, int Age);
}
