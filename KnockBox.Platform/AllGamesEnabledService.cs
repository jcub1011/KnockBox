using KnockBox.Services.Logic.Games.Shared;

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

    public IReadOnlyDictionary<string, bool> GetAll()
        => new Dictionary<string, bool>();

    public event Action? Changed
    {
        add { }
        remove { }
    }
}
