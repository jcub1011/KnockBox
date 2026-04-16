using System.Collections.ObjectModel;
using KnockBox.Services.Logic.Games.Shared;

namespace KnockBox.Platform;

/// <summary>
/// Default no-op <see cref="IGameAvailabilityService"/> that treats every game
/// as enabled. Registered via <c>TryAddSingleton</c> so the production host can
/// override with its file-backed implementation.
/// </summary>
internal sealed class AllGamesEnabledService : IGameAvailabilityService
{
    // Shared immutable empty view; GetAll is a hot-ish lookup on the home page
    // (re-rendered on every module list query) so we don't want per-call allocs.
    private static readonly IReadOnlyDictionary<string, bool> EmptyMap =
        new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>());

    public bool IsEnabled(string routeIdentifier) => true;

    public Task SetEnabledAsync(string routeIdentifier, bool enabled)
        => Task.CompletedTask;

    public IReadOnlyDictionary<string, bool> GetAll() => EmptyMap;

    // No-op: state never changes, so nothing to notify.
    public event Action? Changed
    {
        add { }
        remove { }
    }
}
