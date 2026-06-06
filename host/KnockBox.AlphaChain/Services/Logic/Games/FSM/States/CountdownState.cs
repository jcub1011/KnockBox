using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    using FsmState = IGameState<AlphaChainGameContext, AlphaChainCommand>;

    /// <summary>
    /// A short "Get Ready" dwell shown before a round begins — at game start (after any opening
    /// tutorial) and again after each era's letter ban — so players have a beat to prepare before
    /// the shot clock starts. It runs no gameplay: it sets the <c>Countdown</c> phase, arms a fixed
    /// dwell from <see cref="AlphaChainSettings.PreRoundCountdownSeconds"/>, and hands off to
    /// <paramref name="next"/> when the dwell elapses (<see cref="Tick"/>). Unlike the tutorials it
    /// is <b>not</b> skippable — guaranteed prep time is the whole point — so it ignores every command.
    /// </summary>
    public sealed class CountdownState(FsmState next) : ITimedGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<FsmState?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;

            state.SetPhase(AlphaChainGamePhase.Countdown);
            // Reuse the sub-phase timer field, exactly as TutorialState does: the round shot clock
            // (PhaseEndTime) is only armed later by RoundState.OnEnter, so the two never clash, and
            // the next state overwrites this on entry.
            state.SubPhaseEndTime = DateTimeOffset.UtcNow.AddSeconds(state.Settings.PreRoundCountdownSeconds);

            context.Logger.LogDebug(
                "Alpha Chain FSM → CountdownState (era {era}, dwell {dwell}s)",
                state.CurrentEra, state.Settings.PreRoundCountdownSeconds);

            // Return null so the UI observes the Countdown phase (unlike SetupState, which chains).
            return null;
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<TimeSpan> GetRemainingTime(AlphaChainGameContext context, DateTimeOffset now)
        {
            var remaining = context.State.SubPhaseEndTime - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        public ValueResult<FsmState?> Tick(AlphaChainGameContext context, DateTimeOffset now)
        {
            if (now >= context.State.SubPhaseEndTime)
                return ValueResult<FsmState?>.FromValue(next);
            return null;
        }

        // Not skippable: ignore every command (no SkipTutorialCommand handling).
        public ValueResult<FsmState?> HandleCommand(AlphaChainGameContext context, AlphaChainCommand command)
            => null;
    }
}
