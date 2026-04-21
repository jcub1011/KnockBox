using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle.Models;
using KnockBox.Spardle.Services;
using Microsoft.Extensions.Logging;

namespace KnockBox.Spardle;

public class SpardleEngine(
    IWordListService wordListService,
    IRandomNumberService rng,
    ILoggerFactory loggerFactory) : AbstractGameEngine(1, 20)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<SpardleEngine>();

    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
    {
        var state = new SpardleState(host, _logger);
        state.UpdateJoinableStatus(true);
        return Task.FromResult<ValueResult<AbstractGameState>>(state);
    }

    public override Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
    {
        if (state is not SpardleState s) return Task.FromResult(Result.FromError("Invalid state"));

        return Task.FromResult(s.Execute(() =>
        {
            // UpdateJoinableStatus(false) closes the join race before we read Players.Count;
            // once the lobby is non-joinable, RegisterPlayer rejects new joins.
            s.UpdateJoinableStatus(false);
            s.SetHostIsParticipant(s.Players.Count == 0);
            s.CurrentRound = 0;
            s.IsGameOver = false;
            s.RoundHistory.Clear();
            s.LastCompletedAnswer = null;
            GenerateRoundQueue(s);

            var roster = s.HostIsParticipant ? s.Players.Prepend(s.Host) : s.Players;
            foreach (var user in roster)
            {
                var ps = s.GetOrCreatePlayerState(user.Id);
                ps.TotalScore = 0;
                ps.LastRoundPoints = 0;
                ps.ResetRound();
            }

            EnterRoundIntro(s);
        }));
    }

    private void GenerateRoundQueue(SpardleState state)
    {
        state.RoundQueue.Clear();

        int requested = state.TotalRounds > 0 ? state.TotalRounds : int.MaxValue;

        if (state.CustomWordPool.Count > 0)
        {
            var pool = new List<string>(state.CustomWordPool);
            OrderInPlace(pool, state.WordOrderMode);
            state.RoundQueue.AddRange(pool.Take(Math.Min(requested, pool.Count)));
            return;
        }

        if (state.WordPoolMode == WordPoolMode.NytStandard)
        {
            FillFromSingleLength(state, length: 5, requested);
            return;
        }

        if (state.WordPoolMode == WordPoolMode.FullDictionary)
        {
            if (state.ConstantWordLength)
                FillFromSingleLength(state, state.TargetWordLength, requested);
            else
                FillFromLengthRange(state, state.MinWordLength, state.MaxWordLength, requested);
            return;
        }

        // HostDefined / CsvUpload with empty CustomWordPool — CanStart prevents this in the lobby.
    }

    private void FillFromSingleLength(SpardleState state, int length, int requested)
    {
        int total = wordListService.GetWordCount(state.WordPoolMode, length);
        if (total == 0) return;

        int take = Math.Min(requested, total);
        var indices = PickIndices(state.WordOrderMode, total, take);
        foreach (var idx in indices)
            state.RoundQueue.Add(wordListService.GetWordAsString(state.WordPoolMode, length, idx));
    }

    private void FillFromLengthRange(SpardleState state, int min, int max, int requested)
    {
        if (min > max) return;

        var lengths = wordListService.GetAvailableLengths(state.WordPoolMode)
            .Where(L => L >= min && L <= max)
            .ToArray();
        if (lengths.Length == 0) return;

        var cumulative = new int[lengths.Length];
        int total = 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            total += wordListService.GetWordCount(state.WordPoolMode, lengths[i]);
            cumulative[i] = total;
        }
        if (total == 0) return;

        int take = Math.Min(requested, total);
        var flatIndices = PickIndices(state.WordOrderMode, total, take);
        foreach (var flat in flatIndices)
        {
            int bucket = LowerBound(cumulative, flat);
            int prev = bucket == 0 ? 0 : cumulative[bucket - 1];
            int idxInBucket = flat - prev;
            state.RoundQueue.Add(wordListService.GetWordAsString(state.WordPoolMode, lengths[bucket], idxInBucket));
        }
    }

    private IEnumerable<int> PickIndices(WordOrderMode mode, int total, int take)
    {
        switch (mode)
        {
            case WordOrderMode.RandomNoRepeats:
                var picked = new HashSet<int>(take);
                while (picked.Count < take)
                    picked.Add(rng.GetRandomInt(total));
                return picked;

            case WordOrderMode.RandomWithRepeats:
                var withRepeats = new int[take];
                for (int i = 0; i < take; i++) withRepeats[i] = rng.GetRandomInt(total);
                return withRepeats;

            case WordOrderMode.ReverseListOrder:
                var rev = new int[take];
                for (int i = 0; i < take; i++) rev[i] = total - 1 - i;
                return rev;

            case WordOrderMode.ListOrder:
            default:
                var asc = new int[take];
                for (int i = 0; i < take; i++) asc[i] = i;
                return asc;
        }
    }

    private static void OrderInPlace(List<string> pool, WordOrderMode mode)
    {
        if (mode is WordOrderMode.RandomNoRepeats or WordOrderMode.RandomWithRepeats)
        {
            for (int n = pool.Count - 1; n > 0; n--)
            {
                int k = Random.Shared.Next(n + 1);
                (pool[n], pool[k]) = (pool[k], pool[n]);
            }
        }
        else if (mode == WordOrderMode.ReverseListOrder)
        {
            pool.Reverse();
        }
    }

    private static int LowerBound(int[] cumulative, int target)
    {
        int lo = 0, hi = cumulative.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (cumulative[mid] <= target) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase transitions — all helpers assume they're already inside the
    // execute lock (either via Execute/ExecuteAsync directly, or via
    // ScheduleCallback which wraps its action in ExecuteAsync).
    // ═══════════════════════════════════════════════════════════════════════

    private void EnterRoundIntro(SpardleState s, TimeSpan? duration = null)
    {
        if (s.CurrentRound >= s.RoundQueue.Count)
        {
            EnterGameOver(s);
            return;
        }

        var introDuration = duration ?? s.TransitionDuration;

        s.Phase = GamePhase.RoundIntro;
        s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + introDuration;
        s.IsRoundActive = false;

        s.ScheduleCallback(introDuration, () =>
        {
            EnterPlaying(s);
            return Task.CompletedTask;
        });
    }

    private void EnterPlaying(SpardleState s)
    {
        StartNextRound(s);

        s.Phase = GamePhase.Playing;
        if (s.RoundTimer > TimeSpan.Zero)
        {
            s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + s.RoundTimer;
            int capturedRound = s.CurrentRound;
            s.ScheduleCallback(s.RoundTimer, () =>
            {
                EndRoundIfStillActive(s, capturedRound);
                return Task.CompletedTask;
            });
        }
        else
        {
            s.PhaseExpiresAtUtc = null;
        }
    }

    private void StartNextRound(SpardleState state)
    {
        if (state.CurrentRound >= state.RoundQueue.Count)
        {
            state.IsGameOver = true;
            state.IsRoundActive = false;
            return;
        }

        state.TargetWord = state.RoundQueue[state.CurrentRound];
        state.RoundStartTime = DateTime.UtcNow;
        state.IsRoundActive = true;
        state.CurrentRound++;

        var roster = state.HostIsParticipant ? state.Players.Prepend(state.Host) : state.Players;
        foreach (var user in roster)
        {
            var ps = state.GetOrCreatePlayerState(user.Id);
            ps.ResetRound();
        }
    }

    private void EndRoundIfStillActive(SpardleState s, int roundNum)
    {
        if (s.Phase != GamePhase.Playing || s.CurrentRound != roundNum) return;

        foreach (var ps in s.PlayerStates.Values)
        {
            if (!ps.HasFinishedRound)
            {
                ps.HasFinishedRound = true;
                ps.Dnf = true;
                ps.FinishedAt = DateTime.UtcNow;
            }
        }

        CompleteRound(s);
    }

    private void CompleteRound(SpardleState s)
    {
        s.IsRoundActive = false;
        s.LastCompletedAnswer = s.TargetWord;

        var outcomes = BuildOutcomes(s);
        s.RoundHistory.Add(new RoundResult
        {
            RoundNumber = s.CurrentRound,
            Answer = s.TargetWord,
            Outcomes = outcomes
        });

        foreach (var outcome in outcomes)
        {
            if (s.PlayerStates.TryGetValue(outcome.UserId, out var ps))
            {
                ps.LastRoundPoints = outcome.PointsAwarded;
                ps.TotalScore += outcome.PointsAwarded;
            }
        }

        s.Phase = GamePhase.RoundResults;
        s.PhaseExpiresAtUtc = DateTimeOffset.UtcNow + s.TransitionDuration;

        s.ScheduleCallback(s.TransitionDuration, () =>
        {
            AdvanceAfterResults(s);
            return Task.CompletedTask;
        });
    }

    private void AdvanceAfterResults(SpardleState s)
    {
        if (s.CurrentRound >= s.RoundQueue.Count)
        {
            EnterGameOver(s);
        }
        else
        {
            EnterRoundIntro(s, TimeSpan.FromSeconds(2));
        }
    }

    private void EnterGameOver(SpardleState s)
    {
        s.IsGameOver = true;
        s.IsRoundActive = false;
        s.Phase = GamePhase.GameOver;
        s.PhaseExpiresAtUtc = null;
    }

    private List<PlayerRoundOutcome> BuildOutcomes(SpardleState s)
    {
        var participants = new List<(User User, PlayerState Ps)>();
        var roster = s.HostIsParticipant ? s.Players.Prepend(s.Host) : s.Players;
        foreach (var user in roster)
        {
            if (s.PlayerStates.TryGetValue(user.Id, out var ps))
                participants.Add((user, ps));
        }

        var solvers = participants
            .Where(p => p.Ps.HasFinishedRound && !p.Ps.Dnf)
            .ToList();
        var dnfs = participants
            .Where(p => p.Ps.Dnf || !p.Ps.HasFinishedRound)
            .ToList();

        IEnumerable<IGrouping<(int, long), (User User, PlayerState Ps)>> solverGroups;
        if (s.WinCondition == WinConditionMode.Tactician)
        {
            solverGroups = solvers
                .OrderBy(p => p.Ps.Guesses.Count)
                .ThenBy(p => p.Ps.FinishedAt ?? DateTime.MaxValue)
                .GroupBy(p => (p.Ps.Guesses.Count, (p.Ps.FinishedAt ?? DateTime.MaxValue).Ticks));
        }
        else // Sprinter
        {
            solverGroups = solvers
                .OrderBy(p => p.Ps.FinishedAt ?? DateTime.MaxValue)
                .GroupBy(p => (0, (p.Ps.FinishedAt ?? DateTime.MaxValue).Ticks));
        }

        var outcomes = new List<PlayerRoundOutcome>();
        int placement = 1;
        foreach (var group in solverGroups)
        {
            int points = PointsForPlacement(placement, solved: true);
            foreach (var member in group)
            {
                outcomes.Add(new PlayerRoundOutcome
                {
                    UserId = member.User.Id,
                    DisplayName = member.User.Name,
                    GuessCount = member.Ps.Guesses.Count,
                    FinishedAt = member.Ps.FinishedAt,
                    Dnf = false,
                    PointsAwarded = points,
                    Placement = placement
                });
            }
            placement += group.Count();
        }

        foreach (var (user, ps) in dnfs)
        {
            outcomes.Add(new PlayerRoundOutcome
            {
                UserId = user.Id,
                DisplayName = user.Name,
                GuessCount = ps.Guesses.Count,
                FinishedAt = ps.FinishedAt,
                Dnf = true,
                PointsAwarded = 0,
                Placement = 0
            });
        }

        return outcomes;
    }

    public static int PointsForPlacement(int placement, bool solved)
    {
        if (!solved) return 0;
        return placement switch
        {
            1 => 10,
            2 => 5,
            3 => 2,
            _ => 1
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Guess handling (unchanged core logic)
    // ═══════════════════════════════════════════════════════════════════════

    public Result SubmitGuess(SpardleState state, User player, string guess)
    {
        Result errorResult = Result.Success;
        var executeResult = state.Execute(() =>
        {
            if (player.Id == state.Host.Id && !state.HostIsParticipant)
            { errorResult = Result.FromError("Host is observing and cannot submit guesses."); return; }

            if (state.Phase != GamePhase.Playing || !state.IsRoundActive)
            { errorResult = Result.FromError("Round is not active."); return; }

            var pState = state.GetOrCreatePlayerState(player.Id);
            if (pState.HasFinishedRound)
            { errorResult = Result.FromError("You have already finished this round."); return; }

            guess = (guess ?? string.Empty).ToLowerInvariant().Trim();

            var valResult = ValidateGuess(state, pState, guess);
            if (!valResult.IsSuccess) { errorResult = valResult; return; }

            var result = EvaluateGuess(state.TargetWord, guess);
            pState.Guesses.Add(result);

            int maxGuesses = CalculateMaxGuesses(state.TargetWord.Length, state.DifficultyMultiplier);

            if (result.IsCorrect)
            {
                pState.HasFinishedRound = true;
                pState.FinishedAt = DateTime.UtcNow;
            }
            else if (pState.Guesses.Count >= maxGuesses)
            {
                pState.HasFinishedRound = true;
                pState.FinishedAt = DateTime.UtcNow;
                pState.Dnf = true;
            }

            CheckRoundEnd(state);
        });

        if (!executeResult.IsSuccess) return executeResult;
        return errorResult;
    }

    private Result ValidateGuess(SpardleState state, PlayerState pState, string guess)
    {
        if (guess.Length != state.TargetWord.Length)
            return Result.FromError($"Guess must be {state.TargetWord.Length} characters.");

        if (state.HardModeEnabled && pState.Guesses.Count > 0)
        {
            var lastGuess = pState.Guesses.Last();
            for (int i = 0; i < lastGuess.Word.Length; i++)
            {
                if (lastGuess.Statuses[i] == LetterStatus.Correct && guess[i] != lastGuess.Word[i])
                    return Result.FromError("Hard Mode: Must use correct letters in the correct spot.");
            }
        }

        if (!state.AllowDictionaryFallback)
        {
            if (!state.CustomWordPool.Contains(guess) && !wordListService.IsInPool(state.WordPoolMode, guess))
                return Result.FromError("Word not in list.");
        }
        else
        {
            if (!wordListService.IsValidWord(guess))
            {
                if (state.AllowCompoundWords)
                {
                    if (!IsValidCompoundWord(guess, wordListService))
                        return Result.FromError("Not a valid word or compound word.");
                }
                else
                {
                    return Result.FromError("Not a valid word.");
                }
            }
        }

        return Result.Success;
    }

    private static bool IsValidCompoundWord(string word, IWordListService service)
    {
        int n = word.Length;
        if (n == 0) return true;

        var span = word.AsSpan();
        bool[] dp = new bool[n + 1];
        dp[0] = true;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 0; j < i; j++)
            {
                if (dp[j] && service.IsValidWord(span.Slice(j, i - j)))
                {
                    dp[i] = true;
                    break;
                }
            }
        }
        return dp[n];
    }

    private GuessResult EvaluateGuess(string target, string guess)
    {
        var statuses = new LetterStatus[target.Length];
        var targetChars = target.ToList();

        for (int i = 0; i < guess.Length; i++)
        {
            if (guess[i] == target[i])
            {
                statuses[i] = LetterStatus.Correct;
                targetChars[i] = '\0';
            }
        }

        for (int i = 0; i < guess.Length; i++)
        {
            if (statuses[i] != LetterStatus.Correct)
            {
                int index = targetChars.IndexOf(guess[i]);
                if (index != -1)
                {
                    statuses[i] = LetterStatus.Present;
                    targetChars[index] = '\0';
                }
                else
                {
                    statuses[i] = LetterStatus.Absent;
                }
            }
        }

        return new GuessResult
        {
            Word = guess,
            Statuses = statuses,
            IsCorrect = statuses.All(st => st == LetterStatus.Correct)
        };
    }

    public static int CalculateMaxGuesses(int length, double multiplier)
    {
        if (length <= 0) return 6;
        double g = 6.0 + multiplier * Math.Log((double)length / 5.0);
        return (int)Math.Round(g, MidpointRounding.AwayFromZero);
    }

    private void CheckRoundEnd(SpardleState state)
    {
        bool shouldEnd;
        if (state.WaitForAll)
        {
            shouldEnd = state.PlayerStates.Values.All(p => p.HasFinishedRound);
        }
        else if (state.WinCondition == WinConditionMode.Sprinter)
        {
            shouldEnd = state.PlayerStates.Values.Any(p => p.HasFinishedRound && !p.Dnf)
                        || state.PlayerStates.Values.All(p => p.HasFinishedRound);
        }
        else // Tactician
        {
            shouldEnd = state.PlayerStates.Values.All(p => p.HasFinishedRound);
        }

        if (shouldEnd) CompleteRound(state);
    }
}
