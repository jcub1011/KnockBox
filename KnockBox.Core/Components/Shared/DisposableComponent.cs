using Microsoft.AspNetCore.Components;

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
