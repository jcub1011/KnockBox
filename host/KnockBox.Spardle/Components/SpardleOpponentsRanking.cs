using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle.Models;

namespace KnockBox.Spardle.Components;

/// <summary>
/// Pure ranking and badge-formatting helpers for the in-round LEADERS panel.
/// Lives outside the Razor component so it can be unit-tested without
/// instantiating <see cref="SpardleOpponentsPanel"/>.
/// </summary>
internal static class SpardleOpponentsRanking
{
    internal sealed record RankedEntry(User User, string DisplayName, PlayerState PlayerState, int Rank);

    internal static List<RankedEntry> ComputeRanked(SpardleState state)
    {
        IEnumerable<PlayerEntry> roster =
            state.HostIsParticipant ? state.RosterIncludingHost : state.Players;

        var wordLength = state.TargetWord.Length;

        var entries = roster
            .Select(u => (u.User, u.DisplayName, Ps: state.TryGetPlayerState(u.User.Id, out var p) ? p : null))
            .Where(p => p.Ps is not null)
            .Select(p =>
            {
                var (correctPos, wrongPos) = ComputeLetterProgress(p.Ps!, wordLength);
                return (p.User, p.DisplayName, Ps: p.Ps!, CorrectPos: correctPos, WrongPos: wrongPos);
            })
            .ToList();

        IOrderedEnumerable<(User User, string DisplayName, PlayerState Ps, int CorrectPos, int WrongPos)> sorted;

        if (state.Settings.WinCondition == WinConditionMode.Sprinter)
        {
            // Three-state grouping: solved → still-guessing → DNF. DNF players (max-guess
            // exhaustion or voluntary give-up) carry a FinishedAt timestamp; without the
            // explicit DNF bucket they would tiebreak ahead of still-guessing players via
            // the FinishedAt key, surfacing forfeits above active competitors.
            // Within each group: solvers by finish time; non-solvers by letter progress.
            // Constant-within-group keys keep each ThenBy a no-op outside its target group.
            sorted = entries
                .OrderBy(p => RoundStateSortKey(p.Ps))
                .ThenBy(p => p.Ps.HasFinishedRound && !p.Ps.Dnf
                    ? (p.Ps.FinishedAt ?? DateTime.MaxValue).Ticks
                    : 0L)
                .ThenByDescending(p => p.Ps.HasFinishedRound && !p.Ps.Dnf ? 0 : p.CorrectPos)
                .ThenByDescending(p => p.Ps.HasFinishedRound && !p.Ps.Dnf ? 0 : p.WrongPos)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            // Tactician — "closest to finishing" first:
            //   solved > still guessing > DNF, then most-correct-letters, then fewest guesses,
            //   with absolute match standing (total score) as the next tiebreak.
            sorted = entries
                .OrderBy(p => RoundStateSortKey(p.Ps))
                .ThenByDescending(p => p.CorrectPos)
                .ThenBy(p => p.Ps.Guesses.Count)
                .ThenByDescending(p => p.Ps.TotalScore)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        return sorted
            .Select((e, i) => new RankedEntry(e.User, e.DisplayName, e.Ps, i + 1))
            .ToList();
    }

    internal static (int CorrectPos, int WrongPos) ComputeLetterProgress(PlayerState ps, int wordLength)
    {
        if (ps.Guesses.Count == 0 || wordLength <= 0) return (0, 0);
        Span<byte> best = wordLength <= 64 ? stackalloc byte[wordLength] : new byte[wordLength];
        foreach (var g in ps.Guesses)
        {
            for (int i = 0; i < g.Statuses.Length && i < wordLength; i++)
            {
                byte v = g.Statuses[i] switch
                {
                    LetterStatus.Correct => (byte)2,
                    LetterStatus.Present => (byte)1,
                    _ => (byte)0
                };
                if (v > best[i]) best[i] = v;
            }
        }
        int correct = 0, wrong = 0;
        foreach (var v in best)
        {
            if (v == 2) correct++;
            else if (v == 1) wrong++;
        }
        return (correct, wrong);
    }

    internal static string FormatBadge(SpardleState state, PlayerState ps)
    {
        if (ps.Dnf) return "DNF";
        if (!ps.HasFinishedRound) return ps.Guesses.Count.ToString();

        if (state.Settings.WinCondition == WinConditionMode.Sprinter
            && state.RoundStartTime is DateTime start
            && ps.FinishedAt is DateTime finish)
        {
            var elapsed = finish - start;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        }

        return "✓";
    }

    private static int RoundStateSortKey(PlayerState ps)
    {
        if (ps.HasFinishedRound && !ps.Dnf) return 0;
        if (ps.Dnf) return 2;
        return 1;
    }
}
