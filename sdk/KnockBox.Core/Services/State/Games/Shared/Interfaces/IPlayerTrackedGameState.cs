using System.Collections.Concurrent;

namespace KnockBox.Core.Services.State.Games.Shared.Interfaces;

/// <summary>
/// Marker interface for game states that maintain per-player sub-state (hands,
/// score, active effects, etc.) keyed by player id. A
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> is used so background
/// handlers (e.g., tick-driven effects) can read player entries without taking
/// the state's top-level Execute lock.
/// </summary>
/// <typeparam name="TPlayerState">
/// The per-player sub-state record for this game.
/// </typeparam>
public interface IPlayerTrackedGameState<TPlayerState>
{
    /// <summary>
    /// Per-player state keyed by <see cref="Users.User.Id"/>. Entries are
    /// added when players register and removed when
    /// <c>PlayerUnregistered</c> fires on the owning
    /// <see cref="AbstractGameState"/>.
    /// </summary>
    ConcurrentDictionary<string, TPlayerState> GamePlayers { get; }
}
