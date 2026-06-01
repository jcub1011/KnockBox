using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.WordService.Contracts;
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
        IWordListService wordList,
        IRandomNumberService rng,
        ILogger logger)
    {
        /// <summary>The underlying game state for this game instance.</summary>
        public AlphaChainGameState State { get; } = state;

        /// <summary>The owning engine (singleton). States may call its helpers.</summary>
        public AlphaChainGameEngine Engine { get; } = engine;

        /// <summary>
        /// Dictionary validation, provided by the <c>KnockBox.WordService</c> library
        /// plugin. Forwarded from the engine so FSM states can validate words
        /// deterministically (mock in tests). <c>IsValidWord</c> resolves against the full
        /// dictionary (equivalent to <see cref="WordPoolMode.FullDictionary"/>).
        /// </summary>
        public IWordListService WordList { get; } = wordList;

        /// <summary>Randomness source (forwarded from the engine) for the banned-letter draw.</summary>
        public IRandomNumberService Rng { get; } = rng;

        /// <summary>
        /// The outcome of the most recent <see cref="SubmitWordCommand"/>. The FSM writes
        /// it inside the lock; the engine reads it back out to return to the page. Reset to
        /// null by the engine before each submission dispatch.
        /// </summary>
        public SubmitWordResult? LastSubmitResult { get; set; }

        /// <summary>Logger shared by all FSM states.</summary>
        public ILogger Logger { get; } = logger;

        /// <summary>The FSM that manages state transitions for this game.</summary>
        public IFiniteStateMachine<AlphaChainGameContext, AlphaChainCommand> Fsm { get; set; } = null!;

        /// <summary>Shortcut to <see cref="AlphaChainGameState.GamePlayers"/>.</summary>
        public ConcurrentDictionary<string, AlphaChainPlayerState> GamePlayers => State.GamePlayers;
    }
}
