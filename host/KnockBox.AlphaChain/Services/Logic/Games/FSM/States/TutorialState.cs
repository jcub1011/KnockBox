using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    using FsmState = IGameState<AlphaChainGameContext, AlphaChainCommand>;

    /// <summary>
    /// A full-screen scripted tutorial (Shiritori at game start, Engine at the first era boundary).
    /// It runs no gameplay — it sets the <c>Tutorial</c> phase, arms a fixed dwell, and hands off to
    /// <paramref name="next"/> when the dwell elapses (<see cref="Tick"/>) or the host skips it
    /// (<see cref="HandleCommand"/>). The in-Intermission Tax tutorial is handled separately as an
    /// <c>IntermissionSubPhase</c> so the Intermission's dealt cards aren't torn down.
    /// </summary>
    public sealed class TutorialState(TutorialKind kind, FsmState next)
        : ITimedGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        // Per-tutorial dwell lengths. Public so the page (intermission countdown) and the
        // Intermission's Tax sub-phase read the same source of truth.
        private static readonly TimeSpan ShiritoriDwell = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan EngineDwell = TimeSpan.FromSeconds(14);
        private static readonly TimeSpan TaxDwell = TimeSpan.FromSeconds(12);

        /// <summary>How long the given tutorial dwells before auto-advancing.</summary>
        public static TimeSpan DurationFor(TutorialKind kind) => kind switch
        {
            TutorialKind.Shiritori => ShiritoriDwell,
            TutorialKind.Engine => EngineDwell,
            TutorialKind.Tax => TaxDwell,
            _ => ShiritoriDwell
        };

        public ValueResult<FsmState?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            var now = DateTimeOffset.UtcNow;

            state.SetPhase(AlphaChainGamePhase.Tutorial);
            state.CurrentTutorial = kind;
            state.ShownTutorials.Add(kind);
            // Reuse the sub-phase timer field; no round/intermission countdown is consulted while
            // the Tutorial phase is active, and the next state overwrites it on entry.
            state.SubPhaseEndTime = now + DurationFor(kind);

            context.Logger.LogDebug("Alpha Chain FSM → TutorialState ({kind}, dwell {dwell}s)",
                kind, DurationFor(kind).TotalSeconds);

            // Return null so the UI observes the Tutorial phase (unlike SetupState, which chains).
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

        public ValueResult<FsmState?> HandleCommand(AlphaChainGameContext context, AlphaChainCommand command)
        {
            if (command is not SkipTutorialCommand cmd)
                return (ValueResult<FsmState?>)null!;

            if (cmd.ActorUserId != context.State.Host.Id)
                return new ResultError("Only the host can skip the tutorial.",
                    $"Non-host [{cmd.ActorUserId}] tried to skip the {kind} tutorial.");

            context.Logger.LogDebug("Alpha Chain {kind} tutorial skipped by host.", kind);
            return ValueResult<FsmState?>.FromValue(next);
        }
    }
}
