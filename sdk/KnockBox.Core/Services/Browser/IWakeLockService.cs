namespace KnockBox.Core.Services.Browser
{
    /// <summary>
    /// Requests a Screen Wake Lock from the browser so the device's display does
    /// not dim or sleep while the user is on a long-running page (e.g., a lobby
    /// or active game). Implementations are scoped per Blazor circuit.
    ///
    /// The C# layer is a thin pass-through and does not deduplicate calls;
    /// idempotency is enforced inside the JS module, which short-circuits when
    /// a sentinel is already held and coalesces concurrent in-flight requests.
    /// <see cref="ReleaseAsync"/> without a prior acquire is a no-op.
    /// </summary>
    public interface IWakeLockService
    {
        /// <summary>
        /// Requests a screen wake lock. Returns <see langword="true"/> if the
        /// underlying JS call completed without an error and <see langword="false"/>
        /// if a swallowed failure occurred (circuit disconnected, cancellation,
        /// or unexpected JS error). Callers may retry on <see langword="false"/>.
        /// </summary>
        ValueTask<bool> AcquireAsync(CancellationToken ct = default);
        ValueTask ReleaseAsync();
    }
}
