namespace KnockBox.Platform.Games
{
    /// <summary>
    /// Tracks per-game enabled/disabled state so the home page can hide
    /// disabled tiles and <see cref="ILobbyService"/> can refuse new lobbies
    /// for disabled routes. State is persisted to a JSON file so toggles
    /// survive restart.
    /// </summary>
    public interface IGameAvailabilityService
    {
        /// <summary>
        /// Returns whether the game identified by <paramref name="routeIdentifier"/>
        /// is currently enabled. Route identifiers are matched case-insensitively.
        /// Unknown routes are treated as enabled (new plugins default to available).
        /// </summary>
        bool IsEnabled(string routeIdentifier);

        /// <summary>
        /// Flips the enabled state for the given route identifier and persists
        /// the change. Raises <see cref="Changed"/> on successful write.
        /// </summary>
        Task SetEnabledAsync(string routeIdentifier, bool enabled);

        /// <summary>
        /// Snapshot of every route identifier the service has observed, mapped
        /// to whether it is enabled. Includes every known identifier the admin
        /// has touched; callers should union with the live plugin list.
        /// </summary>
        IReadOnlyDictionary<string, bool> GetAll();

        /// <summary>
        /// Fires after a successful <see cref="SetEnabledAsync"/> so UIs (e.g. the
        /// home page) can refresh without polling.
        /// </summary>
        event Action? Changed;
    }
}
