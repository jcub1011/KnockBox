using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Transient bootstrap state. Snapshots participants into <c>GamePlayers</c>,
    /// initializes the era/round counters, and immediately hands off to the first
    /// real screen (the Shiritori tutorial when tutorials are enabled, else
    /// <see cref="RoundState"/>). The FSM chains the returned transition before any
    /// command is processed, so this state is never observed by the UI.
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
            state.StartedAt = DateTimeOffset.UtcNow;

            // The first era is ban-free: banned letters are only introduced from era 2 onward,
            // chosen by the last-place player's Sniper Ban at the first Intermission. This makes
            // IntermissionState the SOLE writer of BannedLetter — see the state's invariant.
            state.BannedLetter = null;

            // First player has a free choice — no required start letter yet.
            state.RequiredStartLetter = null;

            context.Logger.LogDebug(
                "Alpha Chain FSM → SetupState ({count} participants, era 1 is ban-free)",
                state.GamePlayers.Count);

            // A short "Get Ready" countdown always precedes the first turn so players aren't thrown
            // straight into the shot clock. When tutorials are enabled the Shiritori tutorial plays
            // first, then the countdown, then the round loop.
            return state.Settings.EnableTutorials
                ? new TutorialState(TutorialKind.Shiritori, new CountdownState(new RoundState()))
                : new CountdownState(new RoundState());
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> HandleCommand(
            AlphaChainGameContext context, AlphaChainCommand command) => null;
    }
}
