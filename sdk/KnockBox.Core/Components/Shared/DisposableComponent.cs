using Microsoft.AspNetCore.Components;
using System.Diagnostics;

namespace KnockBox.Core.Components.Shared
{
    /// <summary>
    /// A component that provides a ComponentDetached cancellation token that cancels when the component is detached.
    /// </summary>
    public class DisposableComponent : ComponentBase, IDisposable
    {
        private readonly Lock _lock = new();
        private bool _disposed;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource CTS
        {
            get
            {
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _cts ??= new();
                    return _cts;
                }
            }
        }

        /// <summary>
        /// Cancels when the user leaves this page.
        /// </summary>
        protected CancellationToken ComponentDetached => CTS.Token;

        /// <summary>
        /// Schedules <paramref name="onClear"/> to run on the renderer's
        /// SyncContext after <paramref name="delay"/>, then triggers a
        /// re-render. Cancelled if the component is disposed before the delay
        /// elapses.
        /// </summary>
        /// <remarks>
        /// Used to drive UI-dismiss animations from the server when the DOM
        /// <c>animationend</c> event isn't usable — notably, iPhone Safari
        /// rejects <c>setAttribute('@onanimationend', ...)</c> and tears the
        /// circuit.
        /// </remarks>
        protected void ScheduleClear(TimeSpan delay, Action onClear)
        {
            async Task ScheduleWithCatch(TimeSpan delay, Action onClear)
            {
                try
                {
                    await ScheduleClearAsync(delay, onClear);
                }
                catch (Exception ex)
                {
                    // TODO: Hook up to serilog file logging
                    Debug.WriteLine($"Error while calling ScheduleWithCatch: {ex}");
                }
            }

            _ = ScheduleWithCatch(delay, onClear);
        }

        private async Task ScheduleClearAsync(TimeSpan delay, Action onClear)
        {
            try
            {
                await Task.Delay(delay, ComponentDetached);
                await InvokeAsync(() =>
                {
                    onClear();
                    StateHasChanged();
                });
            }
            catch (OperationCanceledException) { }
        }

        public virtual void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;

                _disposed = true;

                if (_cts is not null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }
    }
}
