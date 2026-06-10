using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Spardle.Components;
using KnockBox.Spardle.Contracts;
using KnockBox.Spardle.Models;

namespace KnockBox.Spardle.Services.Projection;

/// <summary>
/// Builds the per-recipient <see cref="SpardleView"/>. <b>Default-deny:</b> the
/// secret answer and every rival's guess letters are withheld during play. A
/// competing player receives only their own <see cref="SpardleView.MyBoard"/> plus
/// count-only <see cref="RivalView"/>s (no guesses field exists on a rival, so a
/// rival's solved letters have nowhere to travel). The display-only host-observer
/// receives every board in <see cref="SpardleView.AllBoards"/>. Once a round or
/// the match ends, the answer + outcomes become public.
/// <para>Runs inside <c>AbstractGameState.WithExclusiveRead</c>, so it observes a
/// consistent snapshot and reads only snapshot-returning members.</para>
/// </summary>
public sealed class SpardleStateProjector(int minPlayerCount, int maxPlayerCount)
    : AbstractStateProjector<SpardleState, SpardleView>
{
    public override SpardleView ProjectFor(SpardleState state, Guid recipientId)
    {
        bool recipientIsHost = recipientId == state.Host.Id;
        bool isHostObserver = !state.HostIsParticipant && recipientIsHost;
        bool recipientIsParticipant = state.TryGetPlayerState(recipientId, out var myState);
        var phase = state.Phase;

        // Roster: the live lobby roster while joinable; the frozen match roster afterwards
        // so a disconnected player still appears on the standings/final screens.
        var rosterSource = state.IsJoinable || state.MatchParticipants.IsDefaultOrEmpty
            ? (IEnumerable<PlayerEntry>)state.RosterIncludingHost
            : state.MatchParticipants;

        var roster = rosterSource
            .Select(e => new SpardleRosterEntry(e.User.Id, e.DisplayName, e.User.Id == state.Host.Id))
            .ToList();

        int wordLength = phase switch
        {
            // The active/just-finished word's length.
            GamePhase.Playing or GamePhase.RoundResults => state.TargetWord.Length,
            // The intro screen previews the upcoming word's LENGTH only (never the word) — the
            // grid reveals length anyway once play starts, so this leaks nothing.
            GamePhase.RoundIntro => state.CurrentRound < state.RoundQueue.Count
                ? state.RoundQueue[state.CurrentRound].Length
                : 0,
            _ => 0
        };
        int maxGuesses = wordLength > 0
            ? SpardleEngine.CalculateMaxGuesses(wordLength, state.Settings.DifficultyMultiplier)
            : 0;

        // ── Per-recipient board projection (default-deny) ────────────────────
        MyBoardView? myBoard = null;
        List<RivalView> rivals = [];
        List<ObserverBoardView> allBoards = [];

        bool boardsActive = phase is GamePhase.Playing or GamePhase.RoundResults;
        if (boardsActive && (recipientIsParticipant || isHostObserver))
        {
            // One ranked pass over all participants — drives the recipient's rank, the
            // count-only rival leaderboard, and the host-observer's full-board gallery.
            var ranked = SpardleOpponentsRanking.ComputeRanked(state);

            if (isHostObserver)
            {
                // The shared display sees every player's full board.
                allBoards = ranked
                    .Select(r => BuildObserverBoard(state, r.User.Id, r.DisplayName, r.PlayerState, maxGuesses))
                    .ToList();
            }
            else // recipientIsParticipant
            {
                var mine = ranked.FirstOrDefault(r => r.User.Id == recipientId);
                myBoard = BuildMyBoard(state, mine, myState!);

                // Competitors surface as PROGRESS-only entries — never their guesses.
                rivals = ranked
                    .Where(r => r.User.Id != recipientId)
                    .Select(r => BuildRival(state, r, maxGuesses))
                    .ToList();
            }
        }

        // ── Results / answer reveal ──────────────────────────────────────────
        RoundResultView? lastRound = null;
        string? answer = null;
        if (phase == GamePhase.RoundResults && state.RoundHistory.Count > 0)
        {
            var r = state.RoundHistory[^1];
            answer = state.Settings.RevealAnswer ? state.LastCompletedAnswer : null;
            lastRound = new RoundResultView(
                r.RoundNumber,
                state.Settings.RevealAnswer ? r.Answer : null,
                r.Outcomes
                    .Select(o => new PlayerOutcomeView(
                        o.UserId, o.DisplayName, o.GuessCount, o.Dnf, o.PointsAwarded, o.Placement,
                        state.PlayerStates.TryGetValue(o.UserId, out var ps) ? ps.TotalScore : 0,
                        ElapsedMs(state, o.FinishedAt)))
                    .ToList());
        }

        var standings = phase == GamePhase.GameOver ? BuildStandings(state) : [];

        return new SpardleView
        {
            HostId = state.Host.Id,
            RecipientId = recipientId,
            IsJoinable = state.IsJoinable,
            RecipientIsHost = recipientIsHost,
            RecipientIsParticipant = recipientIsParticipant,
            HostIsParticipant = state.HostIsParticipant,
            IsHostObserver = isHostObserver,
            MinPlayerCount = minPlayerCount,
            MaxPlayerCount = maxPlayerCount,

            Roster = roster,

            Phase = phase,
            CurrentRound = state.CurrentRound,
            TotalRounds = state.RoundQueue.Count > 0 ? state.RoundQueue.Count : state.Settings.TotalRounds,
            Settings = state.Settings.ToView(),
            PhaseExpiresAtUtc = state.PhaseExpiresAtUtc,

            WordLength = wordLength,
            MaxGuesses = maxGuesses,
            IsRoundActive = state.IsRoundActive,

            HasCustomWordPool = state.CustomWordPool.Count > 0,
            CustomWordCount = state.CustomWordPool.Count,

            MyBoard = myBoard,
            Rivals = rivals,
            AllBoards = allBoards,

            LastRoundResult = lastRound,
            Answer = answer,
            Standings = standings,
        };
    }

    private static MyBoardView BuildMyBoard(SpardleState state, SpardleOpponentsRanking.RankedEntry? mine, PlayerState ps) => new()
    {
        DisplayName = mine?.DisplayName ?? string.Empty,
        Rank = mine?.Rank ?? 0,
        Guesses = ps.Guesses.ToList(),
        HasFinishedRound = ps.HasFinishedRound,
        Solved = ps.HasFinishedRound && !ps.Dnf,
        Dnf = ps.Dnf,
        TotalScore = ps.TotalScore,
        LastRoundPoints = ps.LastRoundPoints,
        FinishedAtElapsedMs = ElapsedMs(state, ps.FinishedAt),
    };

    private static RivalView BuildRival(SpardleState state, SpardleOpponentsRanking.RankedEntry r, int maxGuesses)
    {
        var ps = r.PlayerState;
        return new RivalView(
            r.Rank,
            r.User.Id,
            r.DisplayName,
            ps.Guesses.Count,
            maxGuesses,
            ps.HasFinishedRound,
            ps.HasFinishedRound && !ps.Dnf,
            ps.Dnf,
            ps.TotalScore,
            ElapsedMs(state, ps.FinishedAt));
    }

    private static ObserverBoardView BuildObserverBoard(
        SpardleState state, Guid userId, string displayName, PlayerState ps, int maxGuesses)
        => new(
            userId,
            displayName,
            ps.Guesses.ToList(),
            maxGuesses,
            ps.HasFinishedRound,
            ps.HasFinishedRound && !ps.Dnf,
            ps.Dnf,
            ElapsedMs(state, ps.FinishedAt));

    // Elapsed milliseconds since round start — leak-safe (relative, not a server
    // wall-clock time) and exactly what the client ranking/badges need.
    private static int? ElapsedMs(SpardleState state, DateTime? finishedAt)
    {
        if (state.RoundStartTime is DateTime start && finishedAt is DateTime finish)
            return (int)Math.Max(0, (finish - start).TotalMilliseconds);
        return null;
    }

    private static List<PlayerStandingView> BuildStandings(SpardleState state)
    {
        var ranked = state.MatchParticipants
            .Select(p =>
            {
                int score = state.PlayerStates.TryGetValue(p.User.Id, out var ps) ? ps.TotalScore : 0;
                int won = state.RoundHistory.Count(r => r.Outcomes.Any(o => o.UserId == p.User.Id && o.Placement == 1));
                return (p.User, p.DisplayName, Score: score, Won: won);
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Won)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var standings = new List<PlayerStandingView>(ranked.Count);
        for (int i = 0; i < ranked.Count; i++)
        {
            var x = ranked[i];
            standings.Add(new PlayerStandingView(x.User.Id, x.DisplayName, i + 1, x.Score, x.Won, x.User.Id == state.Host.Id));
        }
        return standings;
    }
}
