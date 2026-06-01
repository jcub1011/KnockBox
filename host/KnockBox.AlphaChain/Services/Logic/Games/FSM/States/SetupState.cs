using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Transient bootstrap state. Snapshots participants into <c>GamePlayers</c>,
    /// initializes the era/round counters, and immediately hands off to
    /// <see cref="RoundState"/> (the FSM chains the returned transition before any
    /// command is processed, so this state is never observed by the UI).
    /// </summary>
    public sealed class SetupState : IGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;

            // Snapshot every participant (host included when HostPlays is on — Participants,
            // not Players) into the per-player state dictionary.
            foreach (var entry in state.Participants)
            {
                state.GamePlayers[entry.User.Id] = new AlphaChainPlayerState
                {
                    UserId = entry.User.Id,
                    DisplayName = entry.DisplayName
                };
            }

            state.CurrentEra = 1;
            state.CurrentRound = 1;

            context.Logger.LogDebug("Alpha Chain FSM → SetupState ({count} participants)", state.GamePlayers.Count);

            return new RoundState();
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> HandleCommand(
            AlphaChainGameContext context, AlphaChainCommand command) => null;
    }
}
