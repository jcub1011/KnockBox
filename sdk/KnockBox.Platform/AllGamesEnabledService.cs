using System.Collections.Frozen;
using KnockBox.Platform.Games;

namespace KnockBox.Platform;

/// <summary>
/// Default no-op <see cref="IGameAvailabilityService"/> that treats every game
/// as enabled. Registered via <c>TryAddSingleton</c> so the production host can
/// override with its file-backed implementation.
/// </summary>
internal sealed class AllGamesEnabledService : IGameAvailabilityService
{
    public bool IsEnabled(string routeIdentifier) => true;

    public Task SetEnabledAsync(string routeIdentifier, bool enabled)
        => Task.CompletedTask;

    // GetAll is called on every home-page re-render; return the shared empty
    // FrozenDictionary instead of allocating a fresh read-only view each call.
    public IReadOnlyDictionary<string, bool> GetAll() => FrozenDictionary<string, bool>.Empty;

    // No-op: state never changes, so nothing to notify.
    public event Action? Changed
    {
        add { }
        remove { }
    }
}
