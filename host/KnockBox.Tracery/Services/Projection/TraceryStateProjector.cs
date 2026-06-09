using KnockBox.Tracery.Contracts;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;

namespace KnockBox.Tracery.Services.Projection
{
    /// <summary>
    /// Builds the per-recipient <see cref="TraceryView"/>. <b>Default-deny:</b> the view is
    /// constructed field-by-field, and the only banked-word data it carries is the recipient's own
    /// (<see cref="TraceryView.MyBankedWords"/>). Another player's in-progress banks and the server's
    /// full findable-word answer key (<c>FindableWords</c>/<c>BoardFindableWords</c>) NEVER cross the
    /// wire — only the curated <see cref="RevealData"/> the engine assembles at round close does.
    /// <para>
    /// Runs inside <c>AbstractGameState.WithExclusiveRead</c> (the host's <c>GameViewCoordinator</c>
    /// holds the read lock), so it observes a consistent snapshot and reads only snapshot-returning
    /// members.
    /// </para>
    /// </summary>
    public sealed class TraceryStateProjector(int minPlayerCount, int maxPlayerCount)
        : AbstractStateProjector<TraceryGameState, TraceryView>
    {
        public override TraceryView ProjectFor(TraceryGameState state, Guid recipientId)
        {
            var settings = state.Settings;
            bool recipientIsHost = recipientId == state.Host.Id;
            bool hasMyState = state.TryGetPlayerState(recipientId, out var myState);
            bool isHostObserver = !state.HostIsParticipant && recipientIsHost;

            // Roster: the live lobby roster while joinable; the frozen match roster afterwards so a
            // disconnected player still appears on the standings/final screens with their last score.
            var rosterSource = state.IsJoinable
                ? state.RosterIncludingHost
                : (state.Participants.IsDefaultOrEmpty ? state.RosterIncludingHost : state.Participants);

            var roster = rosterSource
                .Select(e => new TraceryRosterEntry(
                    e.User.Id,
                    e.DisplayName,
                    e.User.Id == state.Host.Id,
                    state.TryGetPlayerState(e.User.Id, out var ps) ? ps.CumulativeScore : 0))
                .ToList();

            // Per-recipient PRIVATE: only this recipient's own banks, scored provisionally (no
            // unique-find multiplier — that can't be resolved until round close), mirroring the old
            // page's ScoredBankedWords()/ProvisionalRoundScore.
            var myBanked = new List<TraceryBankedWord>();
            int myProvisional = 0;
            if (hasMyState)
            {
                foreach (var traced in myState.BankedInOrder)
                {
                    int points = TraceryScorer.Score(traced.Word, isUnique: false, settings).Points;
                    myBanked.Add(new TraceryBankedWord(traced.Word, points));
                    myProvisional += points;
                }
            }

            bool boardVisible = state.Phase is GamePhase.Playing or GamePhase.Reveal;

            // Host-observer standings: per-participant banked COUNTS (never the words). Default-deny —
            // only the observing host gets this; competing players must not see opponents' progress.
            IReadOnlyList<TraceryLiveStanding> hostStandings = [];
            if (isHostObserver && boardVisible && !state.Participants.IsDefaultOrEmpty)
            {
                hostStandings = state.Participants
                    .Select(entry =>
                    {
                        bool has = state.TryGetPlayerState(entry.User.Id, out var ps);
                        return new TraceryLiveStanding(
                            entry.User.Id,
                            entry.DisplayName,
                            has ? ps.BankedWords.Count : 0,
                            has ? ps.CumulativeScore : 0);
                    })
                    .ToList();
            }

            return new TraceryView
            {
                HostId = state.Host.Id,
                RecipientId = recipientId,
                IsJoinable = state.IsJoinable,
                RecipientIsHost = recipientIsHost,
                RecipientIsParticipant = hasMyState,
                HostIsParticipant = state.HostIsParticipant,
                IsHostObserver = isHostObserver,
                MinPlayerCount = minPlayerCount,
                MaxPlayerCount = maxPlayerCount,

                Roster = roster,

                Phase = state.Phase,
                CurrentRound = state.CurrentRound,
                TotalRounds = settings.TotalRounds,
                IsRoundActive = state.IsRoundActive,
                Settings = settings.ToView(),

                PhaseEndsAtUtc = state.PhaseExpiresAtUtc,
                PhaseDurationSeconds = PhaseDurationSeconds(state),

                // Board + shared search list are symmetric public state during a round.
                Grid = boardVisible ? state.CurrentGrid : null,
                SearchList = state.SearchList.IsDefaultOrEmpty
                    ? []
                    : state.SearchList,

                // Reveal beats are public, built by the engine; standings/round history are public.
                CurrentReveal = state.Phase == GamePhase.Reveal ? state.CurrentReveal : null,
                RoundResults = state.RoundResults,
                HostBoardStandings = hostStandings,
                RevealBoardWords = state.Phase == GamePhase.Reveal ? BuildRevealBoardWords(state) : [],

                MyBankedWords = myBanked,
                MyProvisionalRoundScore = myProvisional,
                MyCompletionRank = hasMyState ? myState.CompletionRank : null,
            };
        }

        // The reveal explorer's word set, pre-scored server-side (the client has no TraceryScorer and
        // never receives the full findable-word answer key). Mirrors the old TraceryBoardExplorer.Build:
        // the recognizable common-word set the board was built from, plus any exotic word a player
        // actually banked, filtered to the shared list in Search mode, scored as the round scored it.
        private static IReadOnlyList<RevealBoardWord> BuildRevealBoardWords(TraceryGameState state)
        {
            var settings = state.Settings;
            bool searchOnly = settings.Mode == GameMode.Search;
            var allowed = searchOnly ? state.SearchList.ToHashSet(StringComparer.Ordinal) : null;
            bool Include(string word) => allowed is null || allowed.Contains(word);
            int Points(string word) => searchOnly
                ? TraceryScorer.BaseScore(word)
                : TraceryScorer.WordScore(word, isUnique: false, settings);

            var display = new Dictionary<string, TracedWord>(StringComparer.Ordinal);
            foreach (var tw in state.BoardFindableWords.Values)
                if (Include(tw.Word))
                    display[tw.Word] = tw;

            var round = state.RoundResults.Count > 0 ? state.RoundResults[^1] : null;
            var findable = state.FindableWords;
            if (round is not null)
                foreach (var o in round.Outcomes)
                    foreach (var s in o.WordScores)
                        if (Include(s.Word) && !display.ContainsKey(s.Word) && findable.TryGetValue(s.Word, out var tw))
                            display[s.Word] = tw;

            return display.Values
                .Select(tw => new RevealBoardWord(tw.Word, Points(tw.Word), tw.Path))
                .OrderByDescending(w => w.Points)
                .ThenByDescending(w => w.Word.Length)
                .ThenBy(w => w.Word, StringComparer.Ordinal)
                .ToList();
        }

        // The active phase's configured total span (seconds), for the client's CountdownClock.
        private static int PhaseDurationSeconds(TraceryGameState state) => state.Phase switch
        {
            GamePhase.RoundIntro => (int)state.Settings.TransitionDuration.TotalSeconds,
            GamePhase.Playing => (int)state.Settings.RoundTimer.TotalSeconds,
            GamePhase.Reveal => (int)state.Settings.IntermissionDuration.TotalSeconds,
            _ => 0,
        };
    }
}
