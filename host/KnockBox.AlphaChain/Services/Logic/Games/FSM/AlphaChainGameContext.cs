using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared;
using System.Collections.Concurrent;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Per-game context shared across FSM states. Created when the game starts and
    /// stored on <c>AlphaChainGameState.Context</c> (mirrors <c>CodewordGameContext</c>).
    /// </summary>
    public class AlphaChainGameContext(
        AlphaChainGameState state,
        AlphaChainGameEngine engine,
        ILogger logger)
    {
        /// <summary>The underlying game state for this game instance.</summary>
        public AlphaChainGameState State { get; } = state;

        /// <summary>The owning engine (singleton). States may call its helpers.</summary>
        public AlphaChainGameEngine Engine { get; } = engine;

        /// <summary>Logger shared by all FSM states.</summary>
        public ILogger Logger { get; } = logger;

        /// <summary>The FSM that manages state transitions for this game.</summary>
        public IFiniteStateMachine<AlphaChainGameContext, AlphaChainCommand> Fsm { get; set; } = null!;

        /// <summary>Shortcut to <see cref="AlphaChainGameState.GamePlayers"/>.</summary>
        public ConcurrentDictionary<string, AlphaChainPlayerState> GamePlayers => State.GamePlayers;
    }
}
