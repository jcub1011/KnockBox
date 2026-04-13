using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.Shared.Interfaces;

public interface IPlayerTrackedGameState<TPlayerState>
{
    ConcurrentDictionary<string, TPlayerState> GamePlayers { get; }
}
