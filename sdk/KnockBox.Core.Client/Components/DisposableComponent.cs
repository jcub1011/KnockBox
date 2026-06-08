using Microsoft.AspNetCore.Components;

namespace KnockBox.Core.Client.Components;

/// <summary>
/// Browser-side mirror of the server <c>KnockBox.Core.Components.Shared.DisposableComponent</c>:
/// exposes a <see cref="ComponentDetached"/> token that cancels when the component
/// is disposed, so async work started in a page is cancelled when the user
/// navigates away. The server's <c>ScheduleClear</c> (a circuit-render-timer
/// affordance) is intentionally omitted — it has no meaning in the WASM client.
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

    /// <summary>Cancels when this component is disposed (the user leaves the page).</summary>
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

        GC.SuppressFinalize(this);
    }
}
