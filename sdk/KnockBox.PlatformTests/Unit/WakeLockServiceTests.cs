using KnockBox.Services.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;

namespace KnockBox.PlatformTests.Unit;

[TestClass]
public sealed class WakeLockServiceTests
{
    private const string ExpectedModulePath = "./_content/KnockBox.Platform/js/wakeLock.js";

    [TestMethod]
    public async Task ReleaseAsync_WithoutPriorAcquire_DoesNotTouchJsRuntime()
    {
        var jsRuntime = new Mock<IJSRuntime>();
        var service = new WakeLockService(jsRuntime.Object, NullLogger<WakeLockService>.Instance);

        await service.ReleaseAsync();

        jsRuntime.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AcquireAsync_SwallowsJSDisconnectedException()
    {
        var jsRuntime = new Mock<IJSRuntime>();
        jsRuntime
            .Setup(r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object?[]?>()))
            .ThrowsAsync(new JSDisconnectedException("circuit gone"));

        var service = new WakeLockService(jsRuntime.Object, NullLogger<WakeLockService>.Instance);

        // Must not throw — circuit-teardown races during navigation are common.
        var ok = await service.AcquireAsync();

        Assert.IsFalse(ok, "Swallowed JSDisconnect should surface as a false return so the caller can retry.");
    }

    [TestMethod]
    public async Task AcquireAsync_ImportsModuleOnlyOnce_AcrossMultipleCalls()
    {
        var module = new Mock<IJSObjectReference>();
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                "acquire",
                It.IsAny<CancellationToken>(),
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSVoidResult>(default(IJSVoidResult)!));

        var jsRuntime = new Mock<IJSRuntime>();
        jsRuntime
            .Setup(r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(module.Object));

        var service = new WakeLockService(jsRuntime.Object, NullLogger<WakeLockService>.Instance);

        Assert.IsTrue(await service.AcquireAsync());
        Assert.IsTrue(await service.AcquireAsync());
        Assert.IsTrue(await service.AcquireAsync());

        jsRuntime.Verify(
            r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object?[]?>()),
            Times.Once);

        module.Verify(
            m => m.InvokeAsync<IJSVoidResult>(
                "acquire",
                It.IsAny<CancellationToken>(),
                It.IsAny<object?[]?>()),
            Times.Exactly(3));
    }

    [TestMethod]
    public async Task AcquireAsync_UsesPlatformContentImportPath()
    {
        var module = new Mock<IJSObjectReference>();
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSVoidResult>(default(IJSVoidResult)!));

        var jsRuntime = new Mock<IJSRuntime>();
        jsRuntime
            .Setup(r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(module.Object));

        var service = new WakeLockService(jsRuntime.Object, NullLogger<WakeLockService>.Instance);
        Assert.IsTrue(await service.AcquireAsync());

        // Guards the "JS lives in KnockBox.Platform RCL, not the host" invariant.
        jsRuntime.Verify(
            r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.Is<object?[]?>(args => args != null && args.Length == 1 && (string)args[0]! == ExpectedModulePath)),
            Times.Once);
    }

    [TestMethod]
    public async Task ReleaseAsync_AfterAcquire_InvokesReleaseOnModule()
    {
        var module = new Mock<IJSObjectReference>();
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSVoidResult>(default(IJSVoidResult)!));
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                It.IsAny<string>(),
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSVoidResult>(default(IJSVoidResult)!));

        var jsRuntime = new Mock<IJSRuntime>();
        jsRuntime
            .Setup(r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(module.Object));

        var service = new WakeLockService(jsRuntime.Object, NullLogger<WakeLockService>.Instance);

        Assert.IsTrue(await service.AcquireAsync());
        await service.ReleaseAsync();

        module.Verify(
            m => m.InvokeAsync<IJSVoidResult>("release", It.IsAny<object?[]?>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ReleaseAsync_SwallowsJSDisconnectedException()
    {
        var module = new Mock<IJSObjectReference>();
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                "acquire",
                It.IsAny<CancellationToken>(),
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSVoidResult>(default(IJSVoidResult)!));
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                "release",
                It.IsAny<object?[]?>()))
            .ThrowsAsync(new JSDisconnectedException("circuit gone"));

        var jsRuntime = new Mock<IJSRuntime>();
        jsRuntime
            .Setup(r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(module.Object));

        var service = new WakeLockService(jsRuntime.Object, NullLogger<WakeLockService>.Instance);
        Assert.IsTrue(await service.AcquireAsync());

        // Must not throw — Dispose paths fire-and-forget into ReleaseAsync.
        await service.ReleaseAsync();
    }

    [TestMethod]
    public async Task AcquireAsync_SwallowsUnexpectedException_AndLogsWarning()
    {
        var module = new Mock<IJSObjectReference>();
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                "acquire",
                It.IsAny<CancellationToken>(),
                It.IsAny<object?[]?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var jsRuntime = new Mock<IJSRuntime>();
        jsRuntime
            .Setup(r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(module.Object));

        var logger = new CountingLogger();
        var service = new WakeLockService(jsRuntime.Object, logger);

        var ok = await service.AcquireAsync();  // must not throw

        Assert.IsFalse(ok, "Swallowed unexpected exception should surface as a false return.");
        Assert.AreEqual(1, logger.WarningCount);
    }

    [TestMethod]
    public async Task ReleaseAsync_SwallowsUnexpectedException_AndLogsWarning()
    {
        var module = new Mock<IJSObjectReference>();
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                "acquire",
                It.IsAny<CancellationToken>(),
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSVoidResult>(default(IJSVoidResult)!));
        module
            .Setup(m => m.InvokeAsync<IJSVoidResult>(
                "release",
                It.IsAny<object?[]?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var jsRuntime = new Mock<IJSRuntime>();
        jsRuntime
            .Setup(r => r.InvokeAsync<IJSObjectReference>(
                "import",
                It.IsAny<object?[]?>()))
            .Returns(new ValueTask<IJSObjectReference>(module.Object));

        var logger = new CountingLogger();
        var service = new WakeLockService(jsRuntime.Object, logger);

        Assert.IsTrue(await service.AcquireAsync());
        await service.ReleaseAsync();  // must not throw

        Assert.AreEqual(1, logger.WarningCount);
    }

    // Hand-rolled because Moq cannot generate a Castle proxy for ILogger<WakeLockService>:
    // WakeLockService is internal and the abstractions assembly is strong-named.
    private sealed class CountingLogger : ILogger<WakeLockService>
    {
        public int WarningCount { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) WarningCount++;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
