namespace KnockBox.Services.State
{
    /// <summary>
    /// A singleton service that wraps <see cref="IServiceProvider"/> and caches resolved services
    /// by a caller-supplied string identifier. Services are shared across all circuits that present
    /// the same id, and are only disposed 5 minutes after the last circuit for that id has closed.
    /// </summary>
    public interface IIDBackedServiceProvider
    {
        /// <summary>
        /// Returns the cached service of type <typeparamref name="T"/> for <paramref name="id"/>,
        /// creating a new instance via the underlying <see cref="IServiceProvider"/> on first access.
        /// Any pending disposal timer for <paramref name="id"/> is cancelled.
        /// </summary>
        /// <typeparam name="T">The service type to resolve.</typeparam>
        /// <param name="id">The caller-supplied identifier (e.g. the user session id).</param>
        /// <returns>The resolved service, or <see langword="null"/> if not registered.</returns>
        T? GetService<T>(string id) where T : class;

        /// <summary>
        /// Notifies that a circuit is now active for the given <paramref name="id"/>.
        /// Adds the circuit to the active-circuit set for that id and cancels any pending disposal.
        /// </summary>
        /// <param name="id">The caller-supplied identifier.</param>
        /// <param name="circuitId">The unique id of the circuit that is becoming active.</param>
        void NotifyCircuitActive(string id, string circuitId);

        /// <summary>
        /// Notifies that the circuit identified by <paramref name="circuitId"/> has permanently closed
        /// for the given <paramref name="id"/>. When the last active circuit for an id is removed a
        /// 5-minute disposal timer is started; if a new request arrives within that window the timer
        /// is cancelled and the services are retained.
        /// </summary>
        /// <param name="id">The caller-supplied identifier.</param>
        /// <param name="circuitId">The unique id of the circuit that has closed.</param>
        void NotifyCircuitClosed(string id, string circuitId);
    }
}
