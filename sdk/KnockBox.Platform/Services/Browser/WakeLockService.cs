using KnockBox.Core.Services.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace KnockBox.Services.Browser
{
    /// <summary>
    /// Default <see cref="IWakeLockService"/>. Lazily imports the
    /// <c>./_content/KnockBox.Platform/js/wakeLock.js</c> ES module and forwards
    /// <c>acquire</c> / <c>release</c> through it. The JS module owns the
    /// sentinel and the visibility/bfcache re-acquire listeners, so this
    /// service stays a thin pass-through. Registered as scoped (per Blazor
    /// circuit). All public methods are guaranteed not to throw — unexpected
    /// JS errors are logged at warning level.
    /// </summary>
    internal sealed class WakeLockService : IWakeLockService, IAsyncDisposable
    {
        private const string ModulePath = "./_content/KnockBox.Platform/js/wakeLock.js";

        private readonly ILogger<WakeLockService> _logger;
        private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

        public WakeLockService(IJSRuntime jsRuntime, ILogger<WakeLockService> logger)
        {
            ArgumentNullException.ThrowIfNull(jsRuntime);
            _logger = logger;
            _moduleTask = new Lazy<Task<IJSObjectReference>>(() =>
                jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask());
        }

        public async ValueTask<bool> AcquireAsync(CancellationToken ct = default)
        {
            try
            {
                var module = await _moduleTask.Value.ConfigureAwait(false);
                await module.InvokeVoidAsync("acquire", ct).ConfigureAwait(false);
                return true;
            }
            catch (JSDisconnectedException) { return false; }
            catch (TaskCanceledException) { return false; }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Wake lock acquire failed.");
                return false;
            }
        }

        public async ValueTask ReleaseAsync()
        {
            if (!_moduleTask.IsValueCreated) return;

            try
            {
                var module = await _moduleTask.Value.ConfigureAwait(false);
                await module.InvokeVoidAsync("release").ConfigureAwait(false);
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Wake lock release failed.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_moduleTask.IsValueCreated) return;

            try
            {
                var module = await _moduleTask.Value.ConfigureAwait(false);
                await module.InvokeVoidAsync("release").ConfigureAwait(false);
                await module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Wake lock dispose failed.");
            }
        }
    }
}
