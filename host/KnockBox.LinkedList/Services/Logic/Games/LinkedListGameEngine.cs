using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.State.Games;

namespace KnockBox.LinkedList.Services.Logic.Games
{
    public class LinkedListGameEngine(
        WordPairSource wordPairSource,
        IRandomNumberService randomNumberService,
        ILogger<LinkedListGameEngine> logger,
        ILogger<LinkedListGameState> stateLogger)
        : AbstractGameEngine(minPlayerCount: 3, maxPlayerCount: 10)
    {
        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null."));

            var gameState = new LinkedListGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(gameState);
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not LinkedListGameState gameState)
                return Task.FromResult(Result.FromError("Error starting game.", $"Game state of type [{(state?.GetType().Name ?? "null")}] couldn't be cast to type [{nameof(LinkedListGameState)}]."));

            var executeResult = gameState.Execute(() =>
            {
                // Build the participant roster (respects HostIsParticipant).
                var participants = gameState.Participants;
                var participantIds = participants.Select(p => p.User.Id).ToList();

                gameState.GamePlayers.Clear();
                foreach (var entry in participants)
                {
                    gameState.GamePlayers[entry.User.Id] = new LinkedListPlayerState
                    {
                        PlayerId = entry.User.Id,
                        DisplayName = entry.DisplayName,
                    };
                }

                gameState.TurnManager.SetTurnOrder(participantIds);

                // Words: honor host-chosen values from the lobby, else pick a curated pair.
                if (string.IsNullOrWhiteSpace(gameState.StartWord) || string.IsNullOrWhiteSpace(gameState.DestinationWord))
                {
                    var pair = wordPairSource.Random(randomNumberService);
                    gameState.StartWord = pair.Start;
                    gameState.DestinationWord = pair.Destination;
                }

                gameState.CarriedWord = gameState.StartWord;
                gameState.DestinationReached = false;
                gameState.Chain.Clear();
                gameState.RejectionLog.Clear();
                gameState.RejectionsThisTurn = 0;

                // Assign the first Auditor: the host-chosen id if valid, else the first
                // participant who is not the current submitter. M2 enforces the
                // active-player ≠ Auditor rule; M1 only records the choice.
                var currentSubmitter = gameState.TurnManager.CurrentPlayer;
                bool hostChoiceValid = !string.IsNullOrEmpty(gameState.AuditorPlayerId)
                    && participantIds.Contains(gameState.AuditorPlayerId);
                if (!hostChoiceValid)
                {
                    gameState.AuditorPlayerId =
                        participantIds.FirstOrDefault(id => id != currentSubmitter) ?? "";
                }

                // Reset round scoring state and start the first submitter's clock.
                gameState.ElapsedThinkingTime = TimeSpan.Zero;
                gameState.ThinkingSegmentStartedUtc = null;
                gameState.LastRoundResult = null;
                gameState.PhaseExpiresAtUtc = null;

                gameState.SetJoinable(false);
                gameState.SetPhase(LinkedListGamePhase.Playing);

                BeginThinkingTurn(gameState, DateTimeOffset.UtcNow);
            });

            if (executeResult.TryGetFailure(out var error))
            {
                logger.LogError("Failed to start Linked List game: {Error}", error.InternalMessage);
                return Task.FromResult(Result.FromError(error));
            }

            return Task.FromResult(Result.Success);
        }

        // ── Core gameplay loop (§4) ──────────────────────────────────────────
        //
        // All three actions validate and mutate inside a single Execute block so
        // the read of the guard fields and the write that follows are serialized
        // with the rest of the room. The inner Result is captured from the
        // closure; an Execute-level failure (cancellation/exception) is surfaced
        // separately.

        /// <summary>
        /// The active submitter proposes a word that pairs with the carried word.
        /// No phase change — the Auditor's view reacts to the pending submission.
        /// </summary>
        public Result SubmitPair(User user, LinkedListGameState state, string word, DateTimeOffset? now = null)
        {
            if (user is null) return Result.FromError("Unknown player.");

            var ts = now ?? DateTimeOffset.UtcNow;
            Result inner = Result.Success;
            var exec = state.Execute(() =>
            {
                if (state.Phase != LinkedListGamePhase.Playing)
                {
                    inner = Result.FromError("The round isn't accepting submissions right now.");
                    return;
                }
                if (user.Id != state.TurnManager.CurrentPlayer)
                {
                    inner = Result.FromError("It isn't your turn.");
                    return;
                }
                if (user.Id == state.AuditorPlayerId)
                {
                    inner = Result.FromError("The Auditor can't submit pairs.");
                    return;
                }
                if (state.PendingSubmission is not null)
                {
                    inner = Result.FromError("A submission is already awaiting the Auditor.");
                    return;
                }

                var trimmed = (word ?? "").Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    inner = Result.FromError("Enter a word to submit.");
                    return;
                }

                // §7.4 optional rigor: block re-forming a pair that already exists
                // in the chain (a loop). Loops are allowed by default and only
                // flagged for display; this toggle is the stricter behavior.
                if (state.Settings.NoImmediateRepeat && IsLoopPair(state, trimmed))
                {
                    inner = Result.FromError("That pair already happened — no repeats allowed.");
                    return;
                }

                state.PendingSubmission = new Submission(user.Id, trimmed);

                // Submission heads to the Auditor — pause the clock. Deliberation
                // never counts (§5.2). The seconds spent thinking up to now are
                // banked, so a later rejection still charges for this attempt.
                PauseForAudit(state, ts);
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// Auditor accepts the pending submission: the chain advances, the carried
        /// word updates, and the turn passes — unless the proposed word is the
        /// destination, in which case the round ends.
        /// </summary>
        public Result Approve(User auditor, LinkedListGameState state, DateTimeOffset? now = null)
        {
            if (auditor is null) return Result.FromError("Unknown player.");

            var ts = now ?? DateTimeOffset.UtcNow;
            Result inner = Result.Success;
            var exec = state.Execute(() =>
            {
                if (auditor.Id != state.AuditorPlayerId)
                {
                    inner = Result.FromError("Only the Auditor can approve a submission.");
                    return;
                }
                if (state.PendingSubmission is not { } sub)
                {
                    inner = Result.FromError("There's no submission to approve.");
                    return;
                }

                var proposed = sub.ProposedWord;
                bool isLoop = IsLoopPair(state, proposed);
                var playerName = state.GamePlayers.TryGetValue(sub.PlayerId, out var ps)
                    ? ps.DisplayName : "Player";

                state.Chain.Add(new ChainLink(state.CarriedWord, proposed, sub.PlayerId, playerName, isLoop));
                if (ps is not null) ps.AcceptedPairs++;

                if (string.Equals(proposed, state.DestinationWord, StringComparison.OrdinalIgnoreCase))
                {
                    state.DestinationReached = true;
                    FinalizeRound(state, ts);
                }
                else
                {
                    state.CarriedWord = proposed;
                    state.RejectionsThisTurn = 0;
                    AdvanceToNextSubmitter(state);
                    // Next submitter is now thinking — (re)start the clock & timeout.
                    BeginThinkingTurn(state, ts);
                }

                state.PendingSubmission = null;
                state.LastRejectionReason = null;
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// Auditor rejects the pending submission with a required reason. The
        /// rejection counts toward the per-turn cap; at the cap the turn is
        /// forfeited (§7.3) and the chain stays put.
        /// </summary>
        public Result Reject(User auditor, LinkedListGameState state, string reason, DateTimeOffset? now = null)
        {
            if (auditor is null) return Result.FromError("Unknown player.");

            var ts = now ?? DateTimeOffset.UtcNow;
            Result inner = Result.Success;
            var exec = state.Execute(() =>
            {
                if (auditor.Id != state.AuditorPlayerId)
                {
                    inner = Result.FromError("Only the Auditor can reject a submission.");
                    return;
                }
                if (state.PendingSubmission is not { } sub)
                {
                    inner = Result.FromError("There's no submission to reject.");
                    return;
                }

                var trimmedReason = (reason ?? "").Trim();
                if (string.IsNullOrEmpty(trimmedReason))
                {
                    inner = Result.FromError("A rejection needs a reason.");
                    return;
                }

                state.RejectionLog.Add(new RejectionInfo(sub.PlayerId, sub.ProposedWord, trimmedReason));
                state.LastRejectionReason = trimmedReason;
                if (state.GamePlayers.TryGetValue(sub.PlayerId, out var ps)) ps.RejectionsReceived++;
                state.RejectionsThisTurn++;
                state.PendingSubmission = null;

                // §7.3: a positive cap forfeits the turn once reached. The partial
                // attempt is already discarded (PendingSubmission cleared); advance
                // the submitter and reset the counter. Cap of 0 = unlimited.
                if (state.Settings.RejectionCap > 0 && state.RejectionsThisTurn >= state.Settings.RejectionCap)
                {
                    state.RejectionsThisTurn = 0;
                    AdvanceToNextSubmitter(state);
                    // A fresh submitter is thinking now.
                    BeginThinkingTurn(state, ts);
                }
                else
                {
                    // Same submitter retries — resume their clock. The seconds spent
                    // on the rejected attempt were already banked at SubmitPair, so
                    // bad guesses cost the time spent thinking about them (§5.2).
                    BeginThinkingTurn(state, ts);
                }
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// True when <paramref name="proposed"/> would re-create a pair
        /// (<c>CarriedWord</c> → <paramref name="proposed"/>) that already exists
        /// somewhere in the chain — i.e. the submission closes a loop (§7.4).
        /// </summary>
        private static bool IsLoopPair(LinkedListGameState state, string proposed) =>
            state.Chain.Any(l =>
                string.Equals(l.FromWord, state.CarriedWord, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.ToWord, proposed, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Advances the turn to the next non-Auditor submitter. The Auditor is
        /// never made the active submitter (M2 rule; full rotation is M4). Guards
        /// against an infinite loop if the Auditor is somehow the only player.
        /// Must be called from inside <see cref="LinkedListGameState"/>'s execute lock.
        /// </summary>
        private static void AdvanceToNextSubmitter(LinkedListGameState state)
        {
            int count = state.TurnManager.TurnOrder.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                state.TurnManager.NextTurn();
                if (state.TurnManager.CurrentPlayer != state.AuditorPlayerId)
                    return;
            }
            // Only the Auditor remains in the turn order; leave the index put.
        }

        // ── Fastest Time clock & per-turn timeout (§5.2, M3) ─────────────────
        //
        // All of these run inside the state's execute lock (called from within an
        // Execute block, or from inside the scheduled callback which itself runs
        // through ExecuteAsync). They mutate state directly and never re-enter
        // Execute. ScheduleCallback only takes the state's scheduling lock, not the
        // execute lock, so calling it from here is deadlock-free.

        /// <summary>
        /// Begins a new thinking turn for the active submitter: bumps the turn
        /// token, (re)starts the accrual clock, and arms a per-turn timeout that
        /// auto-forfeits a turn that runs over. Any prior turn timeout is cancelled.
        /// </summary>
        private void BeginThinkingTurn(LinkedListGameState state, DateTimeOffset now)
        {
            state.TurnSequence++;
            state.StartClock(now); // gated internally on Fastest Time + EnableTimers

            state.TurnTimeoutHandle?.Cancel();
            state.TurnTimeoutHandle = null;
            state.PhaseExpiresAtUtc = null;

            // The per-turn timeout is part of the timed (Fastest Time) experience;
            // Fewest Guesses is a pure puzzle with no clock.
            if (state.Settings.ScoringMode != ScoringMode.FastestTime
                || !state.Settings.EnableTimers
                || state.Settings.PerTurnClock <= TimeSpan.Zero)
            {
                return;
            }

            state.PhaseExpiresAtUtc = now + state.Settings.PerTurnClock;
            int seq = state.TurnSequence;
            var scheduled = state.ScheduleCallback(state.Settings.PerTurnClock, () =>
            {
                ForfeitOnTimeout(state, seq);
                return Task.CompletedTask;
            });
            if (scheduled.TryGetSuccess(out var handle))
                state.TurnTimeoutHandle = handle;
        }

        /// <summary>
        /// Pauses the clock for the auditing window: banks the running thinking
        /// segment and disarms the per-turn timeout (the Auditor has no clock).
        /// </summary>
        private static void PauseForAudit(LinkedListGameState state, DateTimeOffset now)
        {
            state.BankClock(now);
            state.TurnTimeoutHandle?.Cancel();
            state.TurnTimeoutHandle = null;
            state.PhaseExpiresAtUtc = null;
        }

        /// <summary>
        /// Auto-forfeits the active turn when the per-turn clock expires. Ignored if
        /// the turn has already advanced (stale token) or the round is no longer in
        /// play. Banks any running segment, clears the pending submission, and moves
        /// to the next submitter.
        /// </summary>
        private void ForfeitOnTimeout(LinkedListGameState state, int forfeitSequence)
        {
            if (state.TurnSequence != forfeitSequence) return;          // superseded
            if (state.Phase != LinkedListGamePhase.Playing) return;     // round ended

            var now = DateTimeOffset.UtcNow;
            state.BankClock(now);
            state.PendingSubmission = null;
            state.RejectionsThisTurn = 0;
            AdvanceToNextSubmitter(state);
            BeginThinkingTurn(state, now);
        }

        /// <summary>
        /// Banks any running clock, disarms the per-turn timeout, and computes the
        /// <see cref="RoundResult"/> for the active scoring mode before the caller
        /// transitions to <see cref="LinkedListGamePhase.RoundOver"/>.
        /// </summary>
        private static void FinalizeRound(LinkedListGameState state, DateTimeOffset now)
        {
            state.BankClock(now);
            state.TurnTimeoutHandle?.Cancel();
            state.TurnTimeoutHandle = null;
            state.PhaseExpiresAtUtc = null;

            var mode = state.Settings.ScoringMode;
            int guesses = state.GuessCount;
            int? par = state.Settings.Par;
            bool beatPar = mode == ScoringMode.FewestGuesses && par is int p && guesses <= p;

            state.LastRoundResult = new RoundResult(
                mode, guesses, state.ElapsedThinkingTime, par, beatPar, state.DestinationReached);

            state.SetPhase(LinkedListGamePhase.RoundOver);
        }
    }
}
