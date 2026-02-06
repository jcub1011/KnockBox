using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Shared
{
    /// <summary>
    /// A component that provides a ComponentDetached cancellation token that cancels when the component is detached.
    /// </summary>
    public class DisposableComponent : ComponentBase, IDisposable
    {
        private CancellationTokenSource? _cts;

        /// <summary>
        /// Cancels when the user leaves this page.
        /// </summary>
        protected CancellationToken ComponentDetached
        {
            get
            {
                _cts ??= new();
                return _cts.Token;
            }
        }

        public virtual void Dispose()
        {
            try
            {
                if (_cts is null) return;

                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }
}
