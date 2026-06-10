using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.LinkedList.Contracts;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;

namespace KnockBox.LinkedList.Services.Projection
{
    /// <summary>
    /// Builds the per-recipient <see cref="LinkedListView"/>. <b>Default-deny:</b> the only type that
    /// carries chain contents (<see cref="GroupChainView"/>) is placed exclusively in
    /// <see cref="LinkedListView.MyGroup"/> / <see cref="LinkedListView.AuditingGroup"/> /
    /// <see cref="LinkedListView.AllGroups"/>. A competing player's rivals are projected as
    /// <see cref="RivalChip"/>s (counts + the live carried word only), so a rival's links and pending
    /// submission have no field to travel in.
    /// <list type="bullet">
    ///   <item><b>Collective:</b> one shared chain, public — projected as <c>MyGroup</c> for everyone.</item>
    ///   <item><b>Groups, participant:</b> own chain in <c>MyGroup</c>; rivals as chips.</item>
    ///   <item><b>Groups, Auditor:</b> only the front audit-queue group in <c>AuditingGroup</c>.</item>
    ///   <item><b>Groups, host-observer:</b> every group in <c>AllGroups</c>.</item>
    ///   <item><b>RoundOver / GameOver:</b> every chain is public — all in <c>AllGroups</c>.</item>
    /// </list>
    /// <para>Runs inside <c>AbstractGameState.WithExclusiveRead</c>, so it observes a consistent
    /// snapshot and reads only snapshot-returning members.</para>
    /// </summary>
    public sealed class LinkedListStateProjector(int minPlayerCount, int maxPlayerCount)
        : AbstractStateProjector<LinkedListGameState, LinkedListView>
    {
        public override LinkedListView ProjectFor(LinkedListGameState state, Guid recipientId)
        {
            bool recipientIsHost = recipientId == state.Host.Id;
            bool isHostObserver = !state.HostIsParticipant && recipientIsHost;
            bool isAuditor = state.AuditorPlayerId != Guid.Empty && recipientId == state.AuditorPlayerId;
            bool isGroups = state.Settings.PlayerStructure == PlayerStructure.Groups;
            var phase = state.Phase;

            // Roster: the live lobby roster while joinable; the frozen participant roster afterwards
            // so a disconnected player still appears on the standings/final screens.
            var rosterSource = state.IsJoinable
                ? state.RosterIncludingHost
                : (state.Participants.IsDefaultOrEmpty ? state.RosterIncludingHost : state.Participants);

            var roster = rosterSource
                .Select(e => new LinkedListRosterEntry(e.User.Id, e.DisplayName, e.User.Id == state.Host.Id))
                .ToList();

            GroupChainView? myGroup = null;
            GroupChainView? auditingGroup = null;
            int auditQueueLength = 0;
            List<GroupChainView> allGroups = [];
            List<RivalChip> rivals = [];

            bool resultsVisible = phase is LinkedListGamePhase.RoundOver or LinkedListGamePhase.GameOver;

            if (resultsVisible)
            {
                // Round/match over — every chain is public.
                allGroups = state.Groups.Select((g, i) => ToGroupChainView(state, g, i)).ToList();
            }
            else if (phase == LinkedListGamePhase.Playing)
            {
                if (!isGroups)
                {
                    // Collective: the single shared chain is public to everyone (participants,
                    // Auditor, and the host-observer all read it from MyGroup).
                    var g = state.PrimaryGroup;
                    if (g is not null)
                    {
                        myGroup = ToGroupChainView(state, g, 0);
                        if (isAuditor && g.PendingSubmission is not null)
                            auditingGroup = myGroup;
                    }
                }
                else if (isHostObserver)
                {
                    allGroups = state.Groups.Select((g, i) => ToGroupChainView(state, g, i)).ToList();
                }
                else if (isAuditor)
                {
                    auditQueueLength = state.AuditQueue.Count;
                    var ag = state.AuditingGroup;
                    if (ag is not null)
                        auditingGroup = ToGroupChainView(state, ag, state.Groups.IndexOf(ag));
                }
                else
                {
                    var g = state.TryGroupOf(recipientId);
                    if (g is not null)
                    {
                        myGroup = ToGroupChainView(state, g, state.Groups.IndexOf(g));
                        rivals = state.Groups
                            .Where(x => x.GroupId != g.GroupId)
                            .Select(x => new RivalChip(
                                x.GroupId, x.GroupName, state.Groups.IndexOf(x),
                                x.GuessCount, x.Finished, x.Finished ? null : x.CarriedWord))
                            .ToList();
                    }
                }
            }
            // Setup / lobby: no chain is projected.

            var nextAuditorId = LinkedListGameEngine.NextAuditorId(state);

            return new LinkedListView
            {
                HostId = state.Host.Id,
                RecipientId = recipientId,
                IsJoinable = state.IsJoinable,
                RecipientIsHost = recipientIsHost,
                RecipientIsParticipant = state.GamePlayers.ContainsKey(recipientId),
                HostIsParticipant = state.HostIsParticipant,
                IsHostObserver = isHostObserver,
                RecipientIsAuditor = isAuditor,
                MinPlayerCount = minPlayerCount,
                MaxPlayerCount = maxPlayerCount,

                Roster = roster,

                Phase = phase,
                RoundNumber = state.RoundNumber,
                Settings = state.Settings.ToView(),

                StartWord = state.StartWord,
                DestinationWord = state.DestinationWord,

                AuditorPlayerId = state.AuditorPlayerId,
                AuditorName = NameOf(state, state.AuditorPlayerId),
                NextAuditorId = nextAuditorId,
                NextAuditorName = NameOf(state, nextAuditorId),

                MyGroup = myGroup,
                AuditingGroup = auditingGroup,
                AuditQueueLength = auditQueueLength,
                AllGroups = allGroups,
                Rivals = rivals,

                LastRoundResult = state.LastRoundResult is { } r
                    ? new RoundResultView(r.Mode, r.Guesses, r.Elapsed, r.Par, r.BeatPar, r.DestinationReached)
                    : null,
                Standings = state.LastStandings,
                Superlatives = state.Superlatives,
                Scores = state.GamePlayers.Values
                    .Select(p => new LinkedListPlayerScore(
                        p.PlayerId, p.DisplayName, p.GroupId,
                        p.AcceptedPairs, p.RejectionsReceived, p.LoopPairsMade, p.FastestContribution))
                    .ToList(),
            };
        }

        // Copies one group's chain contents into the wire view. The Chain list is snapshotted so a
        // later mutation can't surface through the already-serialized payload.
        private static GroupChainView ToGroupChainView(LinkedListGameState state, ChainState g, int colorIndex)
        {
            var submitterId = g.TurnManager.CurrentPlayer ?? Guid.Empty;
            SubmissionView? pending = g.PendingSubmission is { } s
                ? new SubmissionView(s.PlayerId, NameOf(state, s.PlayerId), s.ProposedWord)
                : null;

            return new GroupChainView
            {
                GroupId = g.GroupId,
                GroupName = g.GroupName,
                ColorIndex = colorIndex < 0 ? 0 : colorIndex,
                CarriedWord = g.CarriedWord,
                Chain = g.Chain.ToList(),
                Pending = pending,
                DestinationReached = g.DestinationReached,
                Finished = g.Finished,
                GuessCount = g.GuessCount,
                CurrentSubmitterId = submitterId,
                CurrentSubmitterName = NameOf(state, submitterId),
                ElapsedThinkingTime = g.ElapsedThinkingTime,
                ClockRunning = g.ClockRunning,
                PhaseExpiresAtUtc = g.PhaseExpiresAtUtc,
            };
        }

        private static string NameOf(LinkedListGameState state, Guid id)
            => id != Guid.Empty && state.GamePlayers.TryGetValue(id, out var ps) ? ps.DisplayName : "—";
    }
}
