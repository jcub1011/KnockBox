using System.Collections.Immutable;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Tracery.Services.Logic.Games
{
    public class TraceryGameEngine(
        ILogger<TraceryGameEngine> logger,
        ILogger<TraceryGameState> stateLogger) : AbstractGameEngine(2, 8)
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new TraceryGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            logger.LogInformation("Created Tracery gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not TraceryGameState s)
                return Task.FromResult(Result.FromError("Error starting game.", $"Game state of type [{state?.GetType().Name ?? "null"}] couldn't be cast to type [{nameof(TraceryGameState)}]."));

            var execResult = s.Execute(() =>
            {
                // SetJoinable(false) closes the join race before we read Players.Length;
                // once the lobby is non-joinable, RegisterPlayer rejects new joins.
                s.SetJoinable(false);
                s.SetHostIsParticipant(s.Players.Length == 0 || s.Settings.HostPlaysAlong);
                // Freeze the participant roster now so the final-standings screen can show
                // everyone even after disconnects prune them from the live Players roster.
                s.SetParticipants(s.HostIsParticipant ? s.RosterIncludingHost : s.Players);

                s.CurrentRound = 0;
                s.RoundResults = s.RoundResults.Clear();

                foreach (var entry in Roster(s))
                {
                    var ps = s.CreatePlayerState(entry.User.Id);
                    ps.CumulativeScore = 0;
                    ps.LastRoundPoints = 0;
                    ps.ResetRound();
                }

                EnterRoundIntro(s);
                return Result.Success;
            });

            if (execResult.TryGetSuccess(out var inner)) return Task.FromResult(inner);
            if (execResult.TryGetFailure(out var err)) return Task.FromResult(Result.FromError(err));
            return Task.FromResult(Result.FromCancellation());
        }

        // The gameplay roster — registered players, plus the host when participating.
        private static IEnumerable<PlayerEntry> Roster(TraceryGameState s)
            => s.HostIsParticipant ? s.RosterIncludingHost : s.Players;

        // ═══════════════════════════════════════════════════════════════════════
        // Phase transitions — placeholder flow for Milestone 01. Real grid,
        // tracing, and scoring fill in later milestones. Every helper assumes it
        // is already inside the execute lock (either via Execute/ExecuteAsync
        // directly, or via ScheduleCallback which wraps its action in ExecuteAsync).
        // They are internal so unit tests can drive transitions without waiting on
        // wall-clock timers.
        // ═══════════════════════════════════════════════════════════════════════

        internal void EnterRoundIntro(TraceryGameState s)
        {
            if (s.CurrentRound >= s.Settings.TotalRounds)
            {
                EnterFinalStandings(s);
                return;
            }

            var duration = s.Settings.TransitionDuration;
            s.Phase = GamePhase.RoundIntro;
            s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + duration;

            s.ScheduleCallback(duration, () =>
            {
                EnterPlaying(s);
                return Task.CompletedTask;
            });
        }

        internal void EnterPlaying(TraceryGameState s)
        {
            s.CurrentRound++;
            foreach (var entry in Roster(s))
                s.CreatePlayerState(entry.User.Id).ResetRound();

            s.Phase = GamePhase.Playing;

            // TimeSpan.Zero = unlimited: no auto-advance. With a real game the round ends
            // when players finish; in this placeholder an unlimited round simply waits.
            if (s.Settings.RoundTimer > TimeSpan.Zero)
            {
                s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + s.Settings.RoundTimer;
                int capturedRound = s.CurrentRound;
                s.ScheduleCallback(s.Settings.RoundTimer, () =>
                {
                    // Guard against a stale timer firing after the phase already moved on.
                    if (s.Phase == GamePhase.Playing && s.CurrentRound == capturedRound)
                        CompleteRound(s);
                    return Task.CompletedTask;
                });
            }
            else
            {
                s.PhaseExpiresAtUtc = null;
            }
        }

        internal void CompleteRound(TraceryGameState s)
        {
            // Placeholder outcome: no scoring yet (Milestone 06). Record the round so the
            // results/standings screens have something to render and the history grows.
            var outcomes = Roster(s)
                .Select(entry => new TraceryPlayerRoundOutcome
                {
                    UserId = entry.User.Id,
                    DisplayName = entry.DisplayName,
                    PointsAwarded = 0
                })
                .ToImmutableArray();

            s.RoundResults = s.RoundResults.Add(new RoundResult
            {
                RoundNumber = s.CurrentRound,
                Outcomes = outcomes
            });

            foreach (var outcome in outcomes)
            {
                if (s.PlayerStates.TryGetValue(outcome.UserId, out var ps))
                {
                    ps.LastRoundPoints = outcome.PointsAwarded;
                    ps.CumulativeScore += outcome.PointsAwarded;
                }
            }

            EnterReveal(s);
        }

        internal void EnterReveal(TraceryGameState s)
        {
            var duration = s.Settings.TransitionDuration;
            s.Phase = GamePhase.Reveal;
            s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + duration;

            s.ScheduleCallback(duration, () =>
            {
                EnterRoundOver(s);
                return Task.CompletedTask;
            });
        }

        internal void EnterRoundOver(TraceryGameState s)
        {
            var duration = s.Settings.TransitionDuration;
            s.Phase = GamePhase.RoundOver;
            s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + duration;

            s.ScheduleCallback(duration, () =>
            {
                AdvanceAfterResults(s);
                return Task.CompletedTask;
            });
        }

        internal void AdvanceAfterResults(TraceryGameState s)
        {
            if (s.CurrentRound >= s.Settings.TotalRounds)
                EnterFinalStandings(s);
            else
                EnterRoundIntro(s);
        }

        internal void EnterFinalStandings(TraceryGameState s)
        {
            s.Phase = GamePhase.FinalStandings;
            s.PhaseExpiresAtUtc = null;
        }
    }
}
