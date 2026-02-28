using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.DiceSimulator
{
    public class DiceSimulatorGameState(
        User host,
        ILogger<DiceSimulatorGameState> logger, 
        IRandomNumberService randomNumberService)
        : AbstractGameState(host, logger)
    {
        private readonly ConcurrentDictionary<Guid, Stack<int>> _rollHistoryMap = [];
        private readonly ConcurrentDictionary<Guid, List<CustomizableDice>> _diceMap = [];

    }
}
