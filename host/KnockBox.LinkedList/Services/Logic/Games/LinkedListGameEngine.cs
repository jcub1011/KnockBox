using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.State.Games;

namespace KnockBox.LinkedList.Services.Logic.Games
{
    public class LinkedListGameEngine(
        WordSource wordSource,
        IRandomNumberService randomNumberService,
        ILogger<LinkedListGameEngine> logger,
        ILogger<LinkedListGameState> stateLogger)
        : AbstractGameEngine<LinkedListGameState>(minPlayerCount: 3, maxPlayerCount: 10)
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

        protected override Task<Result> StartAsyncCore(LinkedListGameState gameState, CancellationToken ct = default)
        {
            Result inner = Result.Success;
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

                // Global Auditor-rotation order over all participants (§6). Per-group
                // submitter rotations live on each ChainState.
                gameState.ParticipantOrder.Clear();
                gameState.ParticipantOrder.AddRange(participantIds); // List<Guid>

                // Words: honor host-chosen values from the lobby, else pick two random words.
                if (string.IsNullOrWhiteSpace(gameState.StartWord) || string.IsNullOrWhiteSpace(gameState.DestinationWord))
                {
                    var pair = wordSource.RandomPair(randomNumberService);
                    gameState.StartWord = pair.Start;
                    gameState.DestinationWord = pair.Destination;
                }

                // Build this match's chains: one all-players group for Collective, or the
                // assigned teams for Groups (auto-balanced into 2 groups as a fallback).
                List<ChainState> groups;
                if (gameState.Settings.PlayerStructure == PlayerStructure.Groups)
                {
                    var assignments = gameState.GroupAssignments.Count > 0
                        ? gameState.GroupAssignments
                        : AutoBalanceGroups(participantIds, 2);
                    var (built, error) = BuildGroups(assignments, participantIds);
                    if (built is null)
                    {
                        inner = Result.FromError(error!);
                        return;
                    }
                    groups = built;
                }
                else
                {
                    groups = BuildCollectiveGroup(participantIds);
                }

                gameState.Groups.Clear();
                gameState.Groups.AddRange(groups);

                // Stamp each player's group id for the scoreboard.
                foreach (var g in groups)
                    foreach (var id in g.MemberIds)
                        if (gameState.GamePlayers.TryGetValue(id, out var ps))
                            ps.GroupId = g.GroupId;

                // Assign the first Auditor: the host-chosen id if valid, else the first
                // participant who isn't a group's opening submitter.
                bool hostChoiceValid = gameState.AuditorPlayerId != Guid.Empty
                    && participantIds.Contains(gameState.AuditorPlayerId);
                if (!hostChoiceValid)
                {
                    var openingSubmitter = groups[0].TurnManager.CurrentPlayer;
                    gameState.AuditorPlayerId =
                        participantIds.FirstOrDefault(id => id != openingSubmitter);
                }

                // Seed the rotation index from the chosen Auditor so the next round's
                // RotateAuditorAndStartRound advances cleanly from here (§6).
                gameState.AuditorRotationIndex =
                    Math.Max(0, gameState.ParticipantOrder.IndexOf(gameState.AuditorPlayerId));

                // Reset every group's round data and seat its first non-Auditor submitter.
                foreach (var g in groups)
                {
                    ResetGroupForRound(g, gameState.StartWord);
                    SeatFirstNonAuditorSubmitter(gameState, g);
                }

                // Reset shared round + match state.
                gameState.AuditQueue.Clear();
                gameState.LastRoundResult = null;
                gameState.LastStandings = [];
                gameState.Superlatives = [];
                gameState.RoundNumber = 1;

                gameState.SetJoinable(false);
                gameState.SetPhase(LinkedListGamePhase.Playing);

                // Every group starts thinking simultaneously.
                var now = DateTimeOffset.UtcNow;
                foreach (var g in groups)
                    BeginThinkingTurn(gameState, g, now);
            });

            if (executeResult.TryGetFailure(out var execError))
            {
                logger.LogError("Failed to start Linked List game: {Error}", execError.InternalMessage);
                return Task.FromResult(Result.FromError(execError));
            }
            if (inner.TryGetFailure(out var startError))
            {
                logger.LogError("Failed to start Linked List game: {Error}", startError.InternalMessage);
                return Task.FromResult(Result.FromError(startError));
            }

            return Task.FromResult(Result.Success);
        }

        /// <summary>
        /// Returns the game to the lobby (host-only, terminal-phase-only) via the base
        /// <see cref="AbstractGameEngine{TState}.ReturnToLobby"/>. Flipping the state back
        /// to joinable re-renders every player's page at the lobby — no navigation needed.
        /// </summary>
        protected override bool IsTerminalPhase(LinkedListGameState state) => state.Phase == LinkedListGamePhase.GameOver;

        /// <inheritdoc />
        protected override void ResetForLobby(LinkedListGameState state)
        {
            // Cancel any lingering per-group turn timers before discarding the groups.
            foreach (var g in state.Groups)
            {
                g.TurnTimeoutHandle?.Cancel();
                g.TurnTimeoutHandle = null;
            }

            state.Groups.Clear();
            state.GamePlayers.Clear();
            state.ParticipantOrder.Clear();
            state.GroupAssignments.Clear();
            state.AuditQueue.Clear();
            state.StartWord = "";
            state.DestinationWord = "";
            state.AuditorPlayerId = Guid.Empty;
            state.AuditorRotationIndex = 0;
            state.RoundNumber = 0;
            state.LastRoundResult = null;
            state.LastStandings = [];
            state.Superlatives = [];
            state.SetPhase(LinkedListGamePhase.Setup);
        }

        // ── Group construction (§8.2) ────────────────────────────────────────

        /// <summary>The single all-players chain for Collective play.</summary>
        private static List<ChainState> BuildCollectiveGroup(IReadOnlyList<Guid> participantIds)
        {
            var g = new ChainState { GroupId = "all", GroupName = "Everyone" };
            g.MemberIds.AddRange(participantIds);
            g.TurnManager.SetTurnOrder(participantIds);
            return [g];
        }

        /// <summary>
        /// Validates and materializes the team assignment into one <see cref="ChainState"/>
        /// per group. Enforces ≥ 2 groups, ≥ 2 members each, and that every participant
        /// is assigned. Returns <c>(null, error)</c> on a validation failure.
        /// </summary>
        private static (List<ChainState>? groups, string? error) BuildGroups(
            List<List<Guid>> assignments, IReadOnlyList<Guid> participantIds)
        {
            var valid = new HashSet<Guid>(participantIds);
            var seen = new HashSet<Guid>();
            var teams = new List<List<Guid>>();

            foreach (var team in assignments)
            {
                var members = new List<Guid>();
                foreach (var id in team)
                {
                    if (!valid.Contains(id) || !seen.Add(id)) continue; // drop unknowns / dupes
                    members.Add(id);
                }
                if (members.Count > 0) teams.Add(members);
            }

            if (teams.Count < 2)
                return (null, "Groups mode needs at least 2 groups.");
            if (teams.Any(t => t.Count < 2))
                return (null, "Each group needs at least 2 players.");
            if (seen.Count != participantIds.Count)
                return (null, "Every player must be assigned to a group.");

            var groups = new List<ChainState>();
            for (int i = 0; i < teams.Count; i++)
            {
                var g = new ChainState { GroupId = $"g{i}", GroupName = $"Group {(char)('A' + i)}" };
                g.MemberIds.AddRange(teams[i]);
                g.TurnManager.SetTurnOrder(teams[i]);
                groups.Add(g);
            }
            return (groups, null);
        }

        /// <summary>
        /// Round-robins <paramref name="playerIds"/> across <paramref name="groupCount"/>
        /// teams. Used by the lobby's auto-balance button and as the engine's fallback
        /// when Groups mode starts without an explicit assignment.
        /// </summary>
        public static List<List<Guid>> AutoBalanceGroups(IReadOnlyList<Guid> playerIds, int groupCount)
        {
            if (groupCount < 1) groupCount = 1;
            var teams = new List<List<Guid>>();
            for (int i = 0; i < groupCount; i++) teams.Add([]);
            for (int i = 0; i < playerIds.Count; i++) teams[i % groupCount].Add(playerIds[i]);
            return teams;
        }

        /// <summary>Clears a group's per-round chain data, points it at the start word,
        /// and seats its first non-Auditor submitter. Match accumulators are untouched.</summary>
        private static void ResetGroupForRound(ChainState g, string startWord)
        {
            g.Chain.Clear();
            g.RejectionLog.Clear();
            g.RejectionsThisTurn = 0;
            g.DestinationReached = false;
            g.PendingSubmission = null;
            g.CarriedWord = startWord;
            g.ElapsedThinkingTime = TimeSpan.Zero;
            g.ThinkingSegmentStartedUtc = null;
            g.ContributionBaseline = TimeSpan.Zero;
            g.PhaseExpiresAtUtc = null;
            g.TurnTimeoutHandle?.Cancel();
            g.TurnTimeoutHandle = null;
        }

        // ── Core gameplay loop (§4), group-scoped ────────────────────────────
        //
        // Each action resolves the relevant ChainState and validates + mutates it
        // inside a single Execute block. Collective resolves to the only group, so
        // its behavior is unchanged from M2–M4.

        /// <summary>
        /// The active submitter of their group proposes a word that pairs with the
        /// group's carried word, sending it to the Auditor's queue.
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
                if (user.Id == state.AuditorPlayerId)
                {
                    inner = Result.FromError("The Auditor can't submit pairs.");
                    return;
                }
                var g = state.TryGroupOf(user.Id);
                if (g is null)
                {
                    inner = Result.FromError("You're not part of this round.");
                    return;
                }
                if (g.Finished)
                {
                    inner = Result.FromError("Your group already reached the destination.");
                    return;
                }
                if (user.Id != g.TurnManager.CurrentPlayer)
                {
                    inner = Result.FromError("It isn't your turn.");
                    return;
                }
                if (g.PendingSubmission is not null)
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
                // in this group's chain (a loop).
                if (state.Settings.NoImmediateRepeat && IsLoopPair(g, trimmed))
                {
                    inner = Result.FromError("That pair already happened — no repeats allowed.");
                    return;
                }

                g.PendingSubmission = new Submission(user.Id, trimmed);

                // Submission heads to the Auditor — pause this group's clock and enqueue
                // it for the (single) Auditor. Deliberation never counts (§5.2).
                PauseForAudit(g, ts);
                EnqueueForAudit(state, g.GroupId);
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// Auditor accepts the front group's pending submission: that group's chain
        /// advances and its turn passes — unless the proposed word is the destination,
        /// in which case the group finishes (and the round ends once all groups have).
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
                var g = state.AuditingGroup;
                if (g?.PendingSubmission is not { } sub)
                {
                    inner = Result.FromError("There's no submission to approve.");
                    return;
                }

                var proposed = sub.ProposedWord;
                bool isLoop = IsLoopPair(g, proposed);
                var playerName = state.GamePlayers.TryGetValue(sub.PlayerId, out var ps)
                    ? ps.DisplayName : "Player";

                g.Chain.Add(new ChainLink(g.CarriedWord, proposed, sub.PlayerId, playerName, isLoop));
                if (ps is not null)
                {
                    ps.AcceptedPairs++;
                    if (isLoop) ps.LoopPairsMade++;

                    // Per-contribution thinking time (Fastest Time): the segment was
                    // banked at SubmitPair, so this group's ElapsedThinkingTime is final
                    // for this attempt. Track the player's fastest landed pair.
                    var contribution = g.ElapsedThinkingTime - g.ContributionBaseline;
                    if (contribution > TimeSpan.Zero
                        && (ps.FastestContribution is null || contribution < ps.FastestContribution))
                    {
                        ps.FastestContribution = contribution;
                    }
                }

                g.PendingSubmission = null;
                DequeueFromAudit(state, g.GroupId);

                if (string.Equals(proposed, state.DestinationWord, StringComparison.OrdinalIgnoreCase))
                {
                    // This group is done; stop its clock and timer.
                    g.DestinationReached = true;
                    g.BankClock(ts);
                    g.TurnTimeoutHandle?.Cancel();
                    g.TurnTimeoutHandle = null;
                    g.PhaseExpiresAtUtc = null;

                    if (state.Groups.All(x => x.Finished))
                        FinalizeRound(state, ts);
                }
                else
                {
                    g.CarriedWord = proposed;
                    g.RejectionsThisTurn = 0;
                    AdvanceToNextSubmitter(state, g);
                    BeginThinkingTurn(state, g, ts);
                }
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// Auditor rejects the front group's pending submission. The rejection counts
        /// toward that group's per-turn cap; at the cap the turn is forfeited (§7.3) and
        /// the group's chain stays put.
        /// </summary>
        public Result Reject(User auditor, LinkedListGameState state, DateTimeOffset? now = null)
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
                var g = state.AuditingGroup;
                if (g?.PendingSubmission is not { } sub)
                {
                    inner = Result.FromError("There's no submission to reject.");
                    return;
                }

                g.RejectionLog.Add(new RejectionInfo(sub.PlayerId, sub.ProposedWord));
                if (state.GamePlayers.TryGetValue(sub.PlayerId, out var ps)) ps.RejectionsReceived++;
                g.RejectionsThisTurn++;
                g.PendingSubmission = null;
                DequeueFromAudit(state, g.GroupId);

                // §7.3: a positive cap forfeits the turn once reached. The partial
                // attempt is already discarded; advance the submitter and reset the
                // counter. Cap of 0 = unlimited.
                if (state.Settings.RejectionCap > 0 && g.RejectionsThisTurn >= state.Settings.RejectionCap)
                {
                    g.RejectionsThisTurn = 0;
                    AdvanceToNextSubmitter(state, g);
                    BeginThinkingTurn(state, g, ts);
                }
                else
                {
                    // Same submitter retries — resume their clock. The rejected attempt's
                    // seconds were banked at SubmitPair, so bad guesses still cost (§5.2).
                    BeginThinkingTurn(state, g, ts);
                }
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// Lets the host end the current round immediately (§10.3 "...to the destination
        /// or a time/round limit") — the escape hatch for a stuck or AFK group when no one
        /// is going to reach the destination. Finalizes from wherever each group currently
        /// stands; groups that didn't reach the destination are scored on partial progress
        /// and rank below those that did. Only the host may call this, and only while a
        /// round is in play.
        /// </summary>
        public Result EndRound(User requester, LinkedListGameState state, DateTimeOffset? now = null)
        {
            if (requester is null) return Result.FromError("Unknown player.");
            if (state is null) return Result.FromError("No game state.");

            var ts = now ?? DateTimeOffset.UtcNow;
            Result inner = Result.Success;
            var exec = state.Execute(() =>
            {
                if (requester.Id != state.Host.Id)
                {
                    inner = Result.FromError("Only the host can end the round.");
                    return;
                }
                if (state.Phase != LinkedListGamePhase.Playing)
                {
                    inner = Result.FromError("There's no round in progress to end.");
                    return;
                }

                // Clear any in-flight submission so the finalized round carries no
                // dangling pending state into the scoreboard.
                foreach (var g in state.Groups)
                    g.PendingSubmission = null;

                FinalizeRound(state, ts);
            });

            return exec.TryGetFailure(out var error) ? Result.FromError(error) : inner;
        }

        /// <summary>
        /// True when <paramref name="proposed"/> would re-create a pair
        /// (<c>CarriedWord</c> → <paramref name="proposed"/>) that already exists
        /// somewhere in this group's chain — i.e. closes a loop (§7.4).
        /// </summary>
        private static bool IsLoopPair(ChainState g, string proposed) =>
            g.Chain.Any(l =>
                string.Equals(l.FromWord, g.CarriedWord, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.ToWord, proposed, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Advances a group's turn to its next non-Auditor submitter. The Auditor is
        /// never made the active submitter. Guards against an infinite loop if the
        /// Auditor is somehow the only candidate. Call inside the execute lock.
        /// </summary>
        private static void AdvanceToNextSubmitter(LinkedListGameState state, ChainState g)
        {
            int count = g.TurnManager.TurnOrder.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                g.TurnManager.NextTurn();
                if (g.TurnManager.CurrentPlayer != state.AuditorPlayerId)
                    return;
            }
            // Only the Auditor remains in this group's order; leave the index put.
        }

        // ── Audit queue (staggered/batch, §8.2) ──────────────────────────────

        /// <summary>Appends a group to the FIFO Auditor queue if it isn't already in it.</summary>
        private static void EnqueueForAudit(LinkedListGameState state, string groupId)
        {
            if (!state.AuditQueue.Contains(groupId))
                state.AuditQueue.Add(groupId);
        }

        /// <summary>Removes a resolved group from the queue; the next group (if any)
        /// becomes the front the Auditor judges.</summary>
        private static void DequeueFromAudit(LinkedListGameState state, string groupId)
            => state.AuditQueue.Remove(groupId);

        // ── Fastest Time clock & per-turn timeout (§5.2), per group ───────────

        /// <summary>
        /// Begins a new thinking turn for a group's active submitter: bumps the group's
        /// turn token, (re)starts its accrual clock, and arms a per-turn timeout that
        /// auto-forfeits a turn that runs over. Any prior turn timeout is cancelled.
        /// </summary>
        private void BeginThinkingTurn(LinkedListGameState state, ChainState g, DateTimeOffset now)
        {
            g.TurnSequence++;
            // Snapshot banked time so Approve can charge only this attempt's thinking
            // toward the player's fastest-contribution stat (§10 "Speed Demon").
            g.ContributionBaseline = g.ElapsedThinkingTime;
            g.StartClock(now, state.Settings); // gated internally on Fastest Time + EnableTimers

            g.TurnTimeoutHandle?.Cancel();
            g.TurnTimeoutHandle = null;
            g.PhaseExpiresAtUtc = null;

            // The per-turn timeout is part of the timed (Fastest Time) experience;
            // Fewest Guesses is a pure puzzle with no clock.
            if (state.Settings.ScoringMode != ScoringMode.FastestTime
                || !state.Settings.EnableTimers
                || state.Settings.PerTurnClock <= TimeSpan.Zero)
            {
                return;
            }

            g.PhaseExpiresAtUtc = now + state.Settings.PerTurnClock;
            int seq = g.TurnSequence;
            var scheduled = state.ScheduleCallback(state.Settings.PerTurnClock, () =>
            {
                ForfeitOnTimeout(state, g, seq);
                return Task.CompletedTask;
            });
            if (scheduled.TryGetSuccess(out var handle))
                g.TurnTimeoutHandle = handle;
        }

        /// <summary>
        /// Pauses a group's clock for the auditing window: banks the running thinking
        /// segment and disarms the per-turn timeout (the Auditor has no clock).
        /// </summary>
        private static void PauseForAudit(ChainState g, DateTimeOffset now)
        {
            g.BankClock(now);
            g.TurnTimeoutHandle?.Cancel();
            g.TurnTimeoutHandle = null;
            g.PhaseExpiresAtUtc = null;
        }

        /// <summary>
        /// Auto-forfeits a group's active turn when its per-turn clock expires. Ignored
        /// if the turn has already advanced (stale token), the group has finished, or the
        /// round is no longer in play. Banks any running segment and moves to the next
        /// submitter.
        /// </summary>
        private void ForfeitOnTimeout(LinkedListGameState state, ChainState g, int forfeitSequence)
        {
            if (g.TurnSequence != forfeitSequence) return;             // superseded
            if (state.Phase != LinkedListGamePhase.Playing) return;    // round ended
            if (g.Finished) return;                                    // group already done

            var now = DateTimeOffset.UtcNow;
            g.BankClock(now);
            g.PendingSubmission = null;
            DequeueFromAudit(state, g.GroupId);
            g.RejectionsThisTurn = 0;
            AdvanceToNextSubmitter(state, g);
            BeginThinkingTurn(state, g, now);
        }

        /// <summary>
        /// Banks every group's clock, disarms timers, computes the per-group standings
        /// (§8.2) and the back-compat single <see cref="RoundResult"/>, then transitions
        /// to <see cref="LinkedListGamePhase.RoundOver"/>.
        /// </summary>
        private static void FinalizeRound(LinkedListGameState state, DateTimeOffset now)
        {
            foreach (var g in state.Groups)
            {
                g.BankClock(now);
                g.TurnTimeoutHandle?.Cancel();
                g.TurnTimeoutHandle = null;
                g.PhaseExpiresAtUtc = null;
            }
            state.AuditQueue.Clear();

            state.LastStandings = ComputeStandings(state);

            // Back-compat single result: the winning group for Groups, else the only group.
            var winnerId = state.LastStandings.Count > 0
                ? state.LastStandings[0].GroupId
                : state.PrimaryGroup?.GroupId;
            var winner = state.GroupById(winnerId) ?? state.PrimaryGroup;
            if (winner is not null)
            {
                var mode = state.Settings.ScoringMode;
                int guesses = winner.GuessCount;
                int? par = state.Settings.Par;
                bool beatPar = mode == ScoringMode.FewestGuesses && par is int p && guesses <= p;
                state.LastRoundResult = new RoundResult(
                    mode, guesses, winner.ElapsedThinkingTime, par, beatPar, winner.DestinationReached);
            }

            state.SetPhase(LinkedListGamePhase.RoundOver);
        }

        /// <summary>
        /// Ranks the round's groups by the active mode's primary metric, breaking ties
        /// with the other metric (§8.2): for Fewest Guesses, fewer guesses win and time
        /// breaks ties; for Fastest Time, less time wins and guesses break ties. Groups
        /// that reached the destination always outrank those that didn't (unreached are
        /// ordered by partial progress). Ties on the primary metric are flagged so the
        /// scoreboard can highlight the tie-break winner.
        /// </summary>
        private static IReadOnlyList<GroupStanding> ComputeStandings(LinkedListGameState state)
        {
            var mode = state.Settings.ScoringMode;

            double Primary(ChainState g) => mode == ScoringMode.FastestTime
                ? g.ElapsedThinkingTime.TotalMilliseconds : g.GuessCount;
            double Secondary(ChainState g) => mode == ScoringMode.FastestTime
                ? g.GuessCount : g.ElapsedThinkingTime.TotalMilliseconds;

            // Compare the primary metric on its native type (exact integer guess count
            // or TimeSpan ticks) rather than via the projected double, so tie detection
            // can't be skewed by floating-point representation.
            bool SamePrimary(ChainState a, ChainState b) => mode == ScoringMode.FastestTime
                ? a.ElapsedThinkingTime == b.ElapsedThinkingTime
                : a.GuessCount == b.GuessCount;

            var finished = state.Groups.Where(g => g.DestinationReached)
                .OrderBy(Primary).ThenBy(Secondary).ThenBy(g => g.GroupId, StringComparer.Ordinal)
                .ToList();
            var unfinished = state.Groups.Where(g => !g.DestinationReached)
                .OrderByDescending(g => g.GuessCount).ThenBy(Secondary).ThenBy(g => g.GroupId, StringComparer.Ordinal)
                .ToList();
            var ordered = finished.Concat(unfinished).ToList();

            var standings = new List<GroupStanding>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                var g = ordered[i];
                // A finished group is a tie-break winner if another finished group shares
                // its primary metric but ranks below it (so the secondary metric decided).
                bool tieWinner = g.DestinationReached && ordered
                    .Skip(i + 1)
                    .Any(o => o.DestinationReached && SamePrimary(o, g));
                standings.Add(new GroupStanding(
                    g.GroupId, g.GroupName, i + 1,
                    g.GuessCount, g.ElapsedThinkingTime, g.DestinationReached, tieWinner));
            }
            return standings;
        }

        // ── Match flow (§10): rotation, end-of-match ─────────────────────────

        /// <summary>The id of the Auditor for the <em>next</em> round, given the current
        /// rotation — used by the RoundOver screen to announce who's up. Returns
        /// <see cref="Guid.Empty"/> if the participant order isn't set.</summary>
        public static Guid NextAuditorId(LinkedListGameState state)
        {
            int count = state.ParticipantOrder.Count;
            if (count == 0) return Guid.Empty;
            return state.ParticipantOrder[(state.AuditorRotationIndex + 1) % count];
        }

        /// <summary>
        /// Ends the current round's scoreboard and starts a fresh round: rotates the
        /// Auditor forward by one in participant order (§6), resets every group's round
        /// data, draws a new curated start/destination pair, seats each group's first
        /// non-Auditor submitter, and arms the clocks. If the match has already reached
        /// <see cref="LinkedListSettings.RoundsPerMatch"/>, ends the match instead.
        /// </summary>
        public Result RotateAuditorAndStartRound(LinkedListGameState state, DateTimeOffset? now = null)
        {
            if (state is null) return Result.FromError("No game state.");

            var ts = now ?? DateTimeOffset.UtcNow;
            Result inner = Result.Success;
            var exec = state.Execute(() =>
            {
                int count = state.ParticipantOrder.Count;
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
                state.AuditorPlayerId = state.ParticipantOrder[state.AuditorRotationIndex];

                // New journey for the round.
                var pair = wordSource.RandomPair(randomNumberService);
                state.StartWord = pair.Start;
                state.DestinationWord = pair.Destination;

                // Reset every group's round data (match accumulators on players persist).
                foreach (var g in state.Groups)
                {
                    ResetGroupForRound(g, state.StartWord);
                    SeatFirstNonAuditorSubmitter(state, g);
                }

                state.AuditQueue.Clear();
                state.LastRoundResult = null;
                state.LastStandings = [];

                state.RoundNumber++;
                state.SetPhase(LinkedListGamePhase.Playing);

                foreach (var g in state.Groups)
                    BeginThinkingTurn(state, g, ts);
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
            foreach (var g in state.Groups)
            {
                g.BankClock(now);
                g.TurnTimeoutHandle?.Cancel();
                g.TurnTimeoutHandle = null;
                g.PhaseExpiresAtUtc = null;
                g.PendingSubmission = null;
            }
            state.AuditQueue.Clear();

            // Final standings reflect wherever each group ended up.
            state.LastStandings = ComputeStandings(state);
            state.Superlatives = ComputeSuperlatives(state);
            state.SetPhase(LinkedListGamePhase.GameOver);
        }

        /// <summary>
        /// Computes the fun end-of-match awards (§10) from per-player accumulators.
        /// Ties break deterministically by ascending player id so results are stable.
        /// Only players who contributed at least one accepted pair are eligible.
        /// </summary>
        private static IReadOnlyList<Superlative> ComputeSuperlatives(LinkedListGameState state)
        {
            var players = state.GamePlayers.Values
                .OrderBy(p => p.PlayerId)
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
        /// Points a group's <see cref="TurnManager"/> at its first member who isn't the
        /// Auditor, so the new round's first submitter is valid. Call inside the execute lock.
        /// </summary>
        private static void SeatFirstNonAuditorSubmitter(LinkedListGameState state, ChainState g)
        {
            var order = g.TurnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] != state.AuditorPlayerId)
                {
                    g.TurnManager.SetCurrentPlayerIndex(i);
                    return;
                }
            }
        }
    }
}
