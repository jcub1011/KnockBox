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

                // Seed the rotation index from the chosen Auditor so M4's
                // RotateAuditorAndStartRound advances cleanly from here (§6).
                gameState.AuditorRotationIndex =
                    Math.Max(0, gameState.TurnManager.TurnOrder.IndexOf(gameState.AuditorPlayerId));

                // Reset round scoring + match state and start the first submitter's clock.
                gameState.ElapsedThinkingTime = TimeSpan.Zero;
                gameState.ThinkingSegmentStartedUtc = null;
                gameState.LastRoundResult = null;
                gameState.PhaseExpiresAtUtc = null;
                gameState.ContributionBaseline = TimeSpan.Zero;
                gameState.PendingSubmission = null;
                gameState.LastRejectionReason = null;
                gameState.RecentReactions.Clear();
                gameState.Persona = AuditorPersona.Neutral;
                gameState.Superlatives = [];
                gameState.RoundNumber = 1;

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
                if (ps is not null)
                {
                    ps.AcceptedPairs++;
                    if (isLoop) ps.LoopPairsMade++;

                    // Per-contribution thinking time (Fastest Time): the segment was
                    // banked at SubmitPair, so ElapsedThinkingTime is final for this
                    // attempt. Track the player's fastest landed pair for "Speed Demon".
                    var contribution = state.ElapsedThinkingTime - state.ContributionBaseline;
                    if (contribution > TimeSpan.Zero
                        && (ps.FastestContribution is null || contribution < ps.FastestContribution))
                    {
                        ps.FastestContribution = contribution;
                    }
                }

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
            // Snapshot banked time so Approve can charge only this attempt's thinking
            // toward the player's fastest-contribution stat (§10 "Speed Demon").
            state.ContributionBaseline = state.ElapsedThinkingTime;
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

        // ── Match flow (§10): rotation, persona, reactions, end-of-match ─────

        /// <summary>Emoji the table can react with (§9.1). The engine validates against
        /// this set so a client can't inject arbitrary content.</summary>
        public static readonly IReadOnlyList<string> AllowedReactions =
            ["😂", "🔥", "👏", "😱", "🤔", "💀"];

        /// <summary>The id of the Auditor for the <em>next</em> round, given the current
        /// rotation — used by the RoundOver screen to announce who's up. Returns empty
        /// if the turn order isn't set.</summary>
        public static string NextAuditorId(LinkedListGameState state)
        {
            int count = state.TurnManager.TurnOrder.Count;
            if (count == 0) return "";
            return state.TurnManager.TurnOrder[(state.AuditorRotationIndex + 1) % count];
        }

        /// <summary>
        /// Ends the current round's scoreboard and starts a fresh round: rotates the
        /// Auditor forward by one in turn order (§6), resets all round data, draws a new
        /// curated start/destination pair, seats the first non-Auditor as submitter,
        /// and arms the clock. If the match has already reached
        /// <see cref="LinkedListSettings.RoundsPerMatch"/>, ends the match instead
        /// (auto-end safety net for the host control).
        /// </summary>
        public Result RotateAuditorAndStartRound(LinkedListGameState state, DateTimeOffset? now = null)
        {
            if (state is null) return Result.FromError("No game state.");

            var ts = now ?? DateTimeOffset.UtcNow;
            Result inner = Result.Success;
            var exec = state.Execute(() =>
            {
                int count = state.TurnManager.TurnOrder.Count;
                if (count == 0)
                {
                    inner = Result.FromError("Can't start a round without players.");
                    return;
                }

                // Auto-end: the match has run its rounds — show the Results screen.
                if (state.RoundNumber >= state.Settings.RoundsPerMatch)
                {
                    EndMatchCore(state, ts);
                    return;
                }

                // Rotate the Auditor forward (wraps so everyone audits in turn).
                state.AuditorRotationIndex = (state.AuditorRotationIndex + 1) % count;
                state.AuditorPlayerId = state.TurnManager.TurnOrder[state.AuditorRotationIndex];

                // Reset round data (match accumulators on players are preserved).
                state.Chain.Clear();
                state.RejectionLog.Clear();
                state.RejectionsThisTurn = 0;
                state.DestinationReached = false;
                state.PendingSubmission = null;
                state.LastRejectionReason = null;
                state.RecentReactions.Clear();
                state.ElapsedThinkingTime = TimeSpan.Zero;
                state.ThinkingSegmentStartedUtc = null;
                state.ContributionBaseline = TimeSpan.Zero;
                state.PhaseExpiresAtUtc = null;
                state.LastRoundResult = null;
                state.Persona = AuditorPersona.Neutral;

                // New journey for the round.
                var pair = wordPairSource.Random(randomNumberService);
                state.StartWord = pair.Start;
                state.DestinationWord = pair.Destination;
                state.CarriedWord = state.StartWord;

                SeatFirstNonAuditorSubmitter(state);

                state.RoundNumber++;
                state.SetPhase(LinkedListGamePhase.Playing);
                BeginThinkingTurn(state, ts);
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// Ends the match: disarms timers, computes the end-of-match superlatives from
        /// the per-player accumulators, and transitions to
        /// <see cref="LinkedListGamePhase.GameOver"/>.
        /// </summary>
        public Result EndMatch(LinkedListGameState state, DateTimeOffset? now = null)
        {
            if (state is null) return Result.FromError("No game state.");

            var ts = now ?? DateTimeOffset.UtcNow;
            var exec = state.Execute(() => EndMatchCore(state, ts));
            return exec.TryGetFailure(out var error) ? Result.FromError(error) : Result.Success;
        }

        /// <summary>Shared end-of-match transition. Call from inside the execute lock.</summary>
        private static void EndMatchCore(LinkedListGameState state, DateTimeOffset now)
        {
            state.BankClock(now);
            state.TurnTimeoutHandle?.Cancel();
            state.TurnTimeoutHandle = null;
            state.PhaseExpiresAtUtc = null;
            state.PendingSubmission = null;

            state.Superlatives = ComputeSuperlatives(state);
            state.SetPhase(LinkedListGamePhase.GameOver);
        }

        /// <summary>
        /// The Auditor sets the round's cosmetic persona (§6). No rule effect — the
        /// outcome of <c>Approve</c>/<c>Reject</c> is identical regardless of persona.
        /// </summary>
        public Result SetPersona(User user, LinkedListGameState state, AuditorPersona persona)
        {
            if (user is null) return Result.FromError("Unknown player.");

            Result inner = Result.Success;
            var exec = state.Execute(() =>
            {
                if (user.Id != state.AuditorPlayerId)
                {
                    inner = Result.FromError("Only the Auditor can set the persona.");
                    return;
                }
                state.Persona = persona;
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// Broadcasts a transient emoji reaction (§9.1) from any player to the whole
        /// table and schedules its removal after <paramref name="ttl"/> (default ~2s).
        /// Heckle/cheer flavor only — never scored. Validated against
        /// <see cref="AllowedReactions"/>.
        /// </summary>
        public Result BroadcastReaction(User user, LinkedListGameState state, string emoji, TimeSpan? ttl = null)
        {
            if (user is null) return Result.FromError("Unknown player.");

            Result inner = Result.Success;
            var exec = state.Execute(() =>
            {
                if (state.Phase != LinkedListGamePhase.Playing)
                {
                    inner = Result.FromError("Reactions are only live during a round.");
                    return;
                }
                if (string.IsNullOrEmpty(emoji) || !AllowedReactions.Contains(emoji))
                {
                    inner = Result.FromError("That reaction isn't available.");
                    return;
                }

                long seq = ++state.ReactionSequence;
                state.RecentReactions.Add(new ReactionEvent(user.Id, emoji, seq));

                // Drop this exact reaction after a beat. Runs through ExecuteAsync, so
                // it mutates state under the lock and notifies subscribers.
                state.ScheduleCallback(ttl ?? TimeSpan.FromSeconds(2), () =>
                {
                    state.RecentReactions.RemoveAll(r => r.Seq == seq);
                    return Task.CompletedTask;
                });
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// Computes the fun end-of-match awards (§10) from per-player accumulators.
        /// Ties break deterministically by ascending player id so results are stable.
        /// Only players who contributed at least one accepted pair are eligible.
        /// </summary>
        private static IReadOnlyList<Superlative> ComputeSuperlatives(LinkedListGameState state)
        {
            var players = state.GamePlayers.Values
                .OrderBy(p => p.PlayerId, StringComparer.Ordinal)
                .ToList();
            if (players.Count == 0) return [];

            var awards = new List<Superlative>();

            // Most Rejected — the player who drew the most rejections (needs > 0).
            var mostRejected = players
                .Where(p => p.RejectionsReceived > 0)
                .OrderByDescending(p => p.RejectionsReceived)
                .FirstOrDefault();
            if (mostRejected is not null)
            {
                awards.Add(new Superlative(
                    "Most Rejected", "🙅", mostRejected.PlayerId, mostRejected.DisplayName,
                    $"{mostRejected.RejectionsReceived} rejection{(mostRejected.RejectionsReceived == 1 ? "" : "s")} survived."));
            }

            // Speed Demon — fastest landed pair (Fastest Time) or most accepted pairs.
            if (state.Settings.ScoringMode == ScoringMode.FastestTime)
            {
                var speed = players
                    .Where(p => p.FastestContribution is not null)
                    .OrderBy(p => p.FastestContribution!.Value)
                    .FirstOrDefault();
                if (speed is not null)
                {
                    awards.Add(new Superlative(
                        "Speed Demon", "⚡", speed.PlayerId, speed.DisplayName,
                        $"Fastest pair in {speed.FastestContribution!.Value.TotalSeconds:0.#}s."));
                }
            }
            else
            {
                var prolific = players
                    .Where(p => p.AcceptedPairs > 0)
                    .OrderByDescending(p => p.AcceptedPairs)
                    .FirstOrDefault();
                if (prolific is not null)
                {
                    awards.Add(new Superlative(
                        "Speed Demon", "⚡", prolific.PlayerId, prolific.DisplayName,
                        $"{prolific.AcceptedPairs} accepted pair{(prolific.AcceptedPairs == 1 ? "" : "s")}."));
                }
            }

            // Loop Lord — most loop pairs landed (needs > 0).
            var loopLord = players
                .Where(p => p.LoopPairsMade > 0)
                .OrderByDescending(p => p.LoopPairsMade)
                .FirstOrDefault();
            if (loopLord is not null)
            {
                awards.Add(new Superlative(
                    "Loop Lord", "🔁", loopLord.PlayerId, loopLord.DisplayName,
                    $"{loopLord.LoopPairsMade} loop pair{(loopLord.LoopPairsMade == 1 ? "" : "s")}."));
            }

            // Smooth Operator — landed pairs with zero rejections against them.
            var smooth = players
                .Where(p => p.AcceptedPairs > 0 && p.RejectionsReceived == 0)
                .OrderByDescending(p => p.AcceptedPairs)
                .FirstOrDefault();
            if (smooth is not null)
            {
                awards.Add(new Superlative(
                    "Smooth Operator", "😎", smooth.PlayerId, smooth.DisplayName,
                    "Not a single rejection — clean run."));
            }

            return awards;
        }

        /// <summary>
        /// Points <see cref="TurnManager"/> at the first player in turn order who isn't
        /// the Auditor, so the new round's first submitter is valid. Call inside the
        /// execute lock.
        /// </summary>
        private static void SeatFirstNonAuditorSubmitter(LinkedListGameState state)
        {
            var order = state.TurnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] != state.AuditorPlayerId)
                {
                    state.TurnManager.SetCurrentPlayerIndex(i);
                    return;
                }
            }
        }
    }
}
