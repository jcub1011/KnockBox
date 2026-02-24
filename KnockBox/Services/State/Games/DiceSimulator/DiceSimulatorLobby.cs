using KnockBox.Services.Logic.Games.Lobbies;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Games.Lobbies;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.DiceSimulator
{
    public class DiceSimulatorLobby(ILogger<DiceSimulatorLobby> logger, ILobbyCodeService lobbyCodeService, IRandomNumberService randomNumberService)
                : GameLobby<DiceSimulatorLobby>(logger, lobbyCodeService)
    {
        private readonly ConcurrentDictionary<Guid, Stack<int>> _rollHistoryMap = [];
        private readonly ConcurrentDictionary<Guid, List<CustomizableDice>> _diceMap = [];

    }
}
