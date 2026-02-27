using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Games.Shared;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.DiceSimulator
{
    public class DiceSimulatorGameState(
        ILogger<DiceSimulatorGameState> logger, 
        IRandomNumberService randomNumberService)
        : AbstractGameState(logger)
    {
        private readonly ConcurrentDictionary<Guid, Stack<int>> _rollHistoryMap = [];
        private readonly ConcurrentDictionary<Guid, List<CustomizableDice>> _diceMap = [];

    }
}
