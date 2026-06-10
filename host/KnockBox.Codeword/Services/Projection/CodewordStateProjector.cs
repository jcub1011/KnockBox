using KnockBox.Codeword.Contracts;
using KnockBox.Codeword.Services.Logic.Games.FSM;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared.Projection;

namespace KnockBox.Codeword.Services.Projection
{
    /// <summary>
    /// Builds the per-recipient <see cref="CodewordView"/>. The projection is
    /// <b>default-deny</b>: the hidden word pair (<see cref="CodewordGameState.CurrentWordPair"/>)
    /// is never put on the wire — it is reconstructable from any two players' secret words —
    /// and the only secret that crosses is the recipient's OWN role + secret word
    /// (<see cref="CodewordView.MyRole"/> / <see cref="CodewordView.MySecretWord"/>). Other
    /// players' roles surface only once publicly revealed: an eliminated player's role during
    /// the reveal cycle, and every player's role at game over (matching the old scoreboard).
    /// <para>
    /// Runs inside <c>AbstractGameState.WithExclusiveRead</c> (the host's
    /// <c>GameViewCoordinator</c> holds the read lock), so it observes a consistent snapshot.
    /// </para>
    /// </summary>
    public sealed class CodewordStateProjector
        : AbstractStateProjector<CodewordGameState, CodewordView>
    {
        public override CodewordView ProjectFor(CodewordGameState state, Guid recipientId)
        {
            var roster = state.RosterIncludingHost
                .Select(e => new RosterEntryView(e.User.Id, e.DisplayName, e.User.Id == state.Host.Id))
                .ToList();

            // In-game players, in turn order when one exists (so the UI lineup is stable).
            var turnOrder = state.TurnManager.TurnOrder;
            IEnumerable<CodewordPlayerState> ordered = turnOrder.Count > 0
                ? turnOrder.Where(state.GamePlayers.ContainsKey).Select(id => state.GamePlayers[id])
                : state.GamePlayers.Values;
            var players = ordered.Select(p => ToPlayerView(p, state.Phase)).ToList();

            // The recipient's own secret — the only secret that crosses the wire.
            Role? myRole = null;
            string? mySecretWord = null;
            if (state.GamePlayers.TryGetValue(recipientId, out var me))
            {
                myRole = me.Role;
                mySecretWord = me.SecretWord;
            }

            ComputePhaseTiming(state, out var phaseEndsAtUtc, out var phaseDurationSeconds);

            return new CodewordView(
                Phase: state.Phase,
                HostId: state.Host.Id,
                RecipientId: recipientId,
                HostIsParticipant: state.HostIsParticipant,
                IsJoinable: state.IsJoinable,
                CurrentGameNumber: state.CurrentGameNumber,
                CurrentEliminationCycle: state.CurrentEliminationCycle,
                CurrentPlayerId: state.TurnManager.CurrentPlayer,
                MyRole: myRole,
                MySecretWord: mySecretWord,
                Roster: roster,
                Players: players,
                CurrentRoundClues: state.CurrentRoundClues.ToList(),
                CurrentRoundVotes: state.CurrentRoundVotes.ToList(),
                LastElimination: state.LastElimination,
                LastInformantGuess: state.LastInformantGuess,
                AwaitingInformantGuess: state.AwaitingInformantGuess,
                WinResult: state.WinResult,
                EndGameVoteStatus: state.EndGameVoteStatus,
                SkipTimeVoteStatus: state.SkipTimeVoteStatus,
                UsedClues: new Dictionary<string, string>(state.UsedClues),
                GameScores: state.GameScores.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                Settings: state.Settings,
                PhaseEndsAtUtc: phaseEndsAtUtc,
                PhaseDurationSeconds: phaseDurationSeconds);
        }

        /// <summary>
        /// Default-deny per-player view: <b>no</b> role/secret-word field is carried here.
        /// <see cref="CodewordPlayerStateView.RevealedRole"/> is set only when the role is
        /// publicly known — every player at game over, an eliminated player during the reveal
        /// cycle — never for a living player mid-game.
        /// </summary>
        private static CodewordPlayerStateView ToPlayerView(CodewordPlayerState p, CodewordGamePhase phase)
        {
            bool roleIsPublic = phase == CodewordGamePhase.GameOver
                || (p.IsEliminated && phase is CodewordGamePhase.Reveal or CodewordGamePhase.ContinueOrEndRound);

            return new CodewordPlayerStateView(
                PlayerId: p.PlayerId,
                DisplayName: p.DisplayName,
                IsEliminated: p.IsEliminated,
                HasSubmittedClue: p.HasSubmittedClue,
                HasVoted: p.HasVoted,
                VoteTargetId: p.VoteTargetId,
                ContinueOrEndVote: p.ContinueOrEndVote,
                HasVotedToEndGame: p.HasVotedToEndGame,
                HasVotedToSkipTime: p.HasVotedToSkipTime,
                Score: p.Score,
                ClueHistory: p.ClueHistory.ToList(),
                RevealedRole: roleIsPublic ? p.Role : null);
        }

        /// <summary>
        /// Surfaces the current timed-state deadline as an absolute UTC timestamp the client
        /// renders a countdown from (replacing the server page's direct <c>GetRemainingTime</c>
        /// read), plus the phase's total duration for the clock's visual fill. The server stays
        /// authoritative on expiry — this is display only.
        /// </summary>
        private static void ComputePhaseTiming(
            CodewordGameState state, out DateTimeOffset? endsAtUtc, out int durationSeconds)
        {
            endsAtUtc = null;
            durationSeconds = 0;

            if (!state.Settings.EnableTimers
                || state.Context?.Fsm?.CurrentState is not ITimedCodewordGameState timed)
                return;

            var now = DateTimeOffset.UtcNow;
            var remaining = timed.GetRemainingTime(state.Context, now);
            if (!remaining.TryGetSuccess(out var rem) || rem.TotalSeconds < 0)
                return;

            endsAtUtc = now + rem;
            durationSeconds = (int)Math.Round(GetTotalDuration(state).TotalSeconds);
        }

        private static TimeSpan GetTotalDuration(CodewordGameState state)
        {
            var s = state.Settings;

            // The informant's final-guess timer runs inside the Reveal phase but uses its own
            // (longer) timeout, so it must be checked before the phase switch.
            if (state.AwaitingInformantGuess)
                return TimeSpan.FromMilliseconds(s.InformantGuessTimeoutMs);

            return state.Phase switch
            {
                CodewordGamePhase.Setup => TimeSpan.FromMilliseconds(s.SetupPhaseTimeoutMs),
                CodewordGamePhase.CluePhase => TimeSpan.FromMilliseconds(s.CluePhaseTimeoutMs),
                CodewordGamePhase.Discussion => TimeSpan.FromMilliseconds(s.DiscussionPhaseTimeoutMs),
                CodewordGamePhase.Voting => TimeSpan.FromMilliseconds(s.VotePhaseTimeoutMs),
                CodewordGamePhase.Reveal => TimeSpan.FromMilliseconds(s.RevealPhaseTimeoutMs),
                CodewordGamePhase.ContinueOrEndRound => TimeSpan.FromMilliseconds(s.ContinueOrEndRoundPhaseTimeoutMs),
                _ => TimeSpan.FromSeconds(30)
            };
        }
    }
}
