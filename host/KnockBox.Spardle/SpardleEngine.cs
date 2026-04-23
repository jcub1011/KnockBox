using System.Collections.Immutable;
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
        state.Execute(() => state.SetJoinable(true));
        return Task.FromResult<ValueResult<AbstractGameState>>(state);
    }

    protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
    {
        if (state is not SpardleState s) return Task.FromResult(Result.FromError("Invalid state"));

        var execResult = s.Execute(() =>
        {
            // SetJoinable(false) closes the join race before we read Players.Count;
            // once the lobby is non-joinable, RegisterPlayer rejects new joins.
            s.SetJoinable(false);
            s.SetHostIsParticipant(s.Players.Count == 0);
            s.CurrentRound = 0;
            s.IsGameOver = false;
            s.RoundHistory = s.RoundHistory.Clear();
            s.LastCompletedAnswer = null;
            GenerateRoundQueue(s);

            var playerUsers = s.Players.Select(p => p.User);
            var roster = s.HostIsParticipant ? playerUsers.Prepend(s.Host) : playerUsers;
            foreach (var user in roster)
            {
                var ps = s.CreatePlayerState(user.Id);
                ps.TotalScore = 0;
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

    private void GenerateRoundQueue(SpardleState state)
    {
        var queue = new List<string>();

        int requested = state.TotalRounds > 0 ? state.TotalRounds : int.MaxValue;

        if (state.CustomWordPool.Count > 0)
        {
            var pool = new List<string>(state.CustomWordPool);
            OrderInPlace(pool, state.WordOrderMode);
            queue.AddRange(pool.Take(Math.Min(requested, pool.Count)));
            state.RoundQueue = queue.ToImmutableList();
            return;
        }

        if (state.WordPoolMode == WordPoolMode.NytStandard)
        {
            FillFromSingleLength(queue, state.WordPoolMode, state.WordOrderMode, length: 5, requested);
            state.RoundQueue = queue.ToImmutableList();
            return;
        }

        if (state.WordPoolMode == WordPoolMode.FullDictionary)
        {
            if (state.ConstantWordLength)
                FillFromSingleLength(queue, state.WordPoolMode, state.WordOrderMode, state.TargetWordLength, requested);
            else
                FillFromLengthRange(queue, state.WordPoolMode, state.WordOrderMode, state.MinWordLength, state.MaxWordLength, requested);
            
            state.RoundQueue = queue.ToImmutableList();
            return;
        }

        state.RoundQueue = queue.ToImmutableList();
    }

    private void FillFromSingleLength(List<string> queue, WordPoolMode poolMode, WordOrderMode orderMode, int length, int requested)
    {
        int total = wordListService.GetWordCount(poolMode, length);
        if (total == 0) return;

        int take = Math.Min(requested, total);
        var indices = PickIndices(orderMode, total, take);
        foreach (var idx in indices)
            queue.Add(wordListService.GetWordAsString(poolMode, length, idx));
    }

    private void FillFromLengthRange(List<string> queue, WordPoolMode poolMode, WordOrderMode orderMode, int min, int max, int requested)
    {
        if (min > max) return;

        var lengths = wordListService.GetAvailableLengths(poolMode)
            .Where(L => L >= min && L <= max)
            .ToArray();
        if (lengths.Length == 0) return;

        var cumulative = new int[lengths.Length];
        int total = 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            total += wordListService.GetWordCount(poolMode, lengths[i]);
            cumulative[i] = total;
        }
        if (total == 0) return;

        int take = Math.Min(requested, total);
        var flatIndices = PickIndices(orderMode, total, take);
        foreach (var flat in flatIndices)
        {
            int bucket = LowerBound(cumulative, flat);
            int prev = bucket == 0 ? 0 : cumulative[bucket - 1];
            int idxInBucket = flat - prev;
            queue.Add(wordListService.GetWordAsString(poolMode, lengths[bucket], idxInBucket));
        }
    }

    private IEnumerable<int> PickIndices(WordOrderMode mode, int total, int take)
    {
        switch (mode)
        {
            case WordOrderMode.RandomNoRepeats:
                return SampleUniqueIndices(total, take);

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

    // Fisher–Yates when the sampling ratio is high (or the universe is small); rejection
    // sampling when we pick a small fraction of a large universe. The threshold caps the
    // worst-case rejection churn at ~2× the number of draws while avoiding a 10k-int
    // allocation when we only need a handful.
    private const int ShuffleThresholdTotal = 2048;
    private static bool ShouldShuffle(int total, int take)
        => total <= ShuffleThresholdTotal || take * 2 >= total;

    private int[] SampleUniqueIndices(int total, int take)
    {
        if (take <= 0 || total <= 0) return [];

        if (ShouldShuffle(total, take))
        {
            var pool = new int[total];
            for (int i = 0; i < total; i++) pool[i] = i;
            FisherYates(pool);
            if (take == total) return pool;
            var result = new int[take];
            Array.Copy(pool, result, take);
            return result;
        }

        var picked = new HashSet<int>(take);
        var order = new int[take];
        int filled = 0;
        while (filled < take)
        {
            int candidate = rng.GetRandomInt(total);
            if (picked.Add(candidate)) order[filled++] = candidate;
        }
        return order;
    }

    private void FisherYates<T>(IList<T> items)
    {
        for (int n = items.Count - 1; n > 0; n--)
        {
            int k = rng.GetRandomInt(n + 1);
            (items[n], items[k]) = (items[k], items[n]);
        }
    }

    private void OrderInPlace(List<string> pool, WordOrderMode mode)
    {
        if (mode is WordOrderMode.RandomNoRepeats or WordOrderMode.RandomWithRepeats)
        {
            FisherYates(pool);
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

        var playerUsers = state.Players.Select(p => p.User);
        var roster = state.HostIsParticipant ? playerUsers.Prepend(state.Host) : playerUsers;
        foreach (var user in roster)
        {
            var ps = state.CreatePlayerState(user.Id);
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
        s.RoundHistory = s.RoundHistory.Add(new RoundResult
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
            EnterPlaying(s);
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
        var participants = new List<(User User, string DisplayName, PlayerState Ps)>();
        var playerEntries = s.Players.Select(p => (p.User, p.DisplayName));
        var roster = s.HostIsParticipant
            ? playerEntries.Prepend((s.Host, s.Host.Name))
            : playerEntries;
        foreach (var (user, displayName) in roster)
        {
            if (s.PlayerStates.TryGetValue(user.Id, out var ps))
                participants.Add((user, displayName, ps));
        }

        var solvers = participants
            .Where(p => p.Ps.HasFinishedRound && !p.Ps.Dnf)
            .ToList();
        var dnfs = participants
            .Where(p => p.Ps.Dnf || !p.Ps.HasFinishedRound)
            .ToList();

        IEnumerable<IGrouping<(int, long), (User User, string DisplayName, PlayerState Ps)>> solverGroups;
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
                    DisplayName = member.DisplayName,
                    GuessCount = member.Ps.Guesses.Count,
                    FinishedAt = member.Ps.FinishedAt,
                    Dnf = false,
                    PointsAwarded = points,
                    Placement = placement
                });
            }
            placement += group.Count();
        }

        foreach (var (user, displayName, ps) in dnfs)
        {
            outcomes.Add(new PlayerRoundOutcome
            {
                UserId = user.Id,
                DisplayName = displayName,
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
        var executeResult = state.Execute<Result>(() =>
        {
            if (player.Id == state.Host.Id && !state.HostIsParticipant)
                return Result.FromError("Host is observing and cannot submit guesses.");

            if (state.Phase != GamePhase.Playing || !state.IsRoundActive)
                return Result.FromError("Round is not active.");

            // Reject strangers before materializing a PlayerState entry for them.
            if (!state.TryGetPlayerState(player.Id, out var pState))
                return Result.FromError("You are not a participant in this round.");

            if (pState.HasFinishedRound)
                return Result.FromError("You have already finished this round.");

            guess = (guess ?? string.Empty).ToLowerInvariant().Trim();

            var valResult = ValidateGuess(state, pState, guess);
            if (!valResult.IsSuccess) return valResult;

            var result = EvaluateGuess(state.TargetWord, guess);
            pState.Guesses = pState.Guesses.Add(result);

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
            return Result.Success;
        });

        if (executeResult.TryGetSuccess(out var inner)) return inner;
        if (executeResult.TryGetFailure(out var err)) return Result.FromError(err);
        return Result.FromCancellation();
    }

    private Result ValidateGuess(SpardleState state, PlayerState pState, string guess)
    {
        if (guess.Length != state.TargetWord.Length)
            return Result.FromError($"Guess must be {state.TargetWord.Length} characters.");

        if (state.HardModeEnabled && pState.Guesses.Count > 0)
        {
            var lastGuess = pState.Guesses.Last();
            
            // 1. Enforce Correct positions
            for (int i = 0; i < lastGuess.Word.Length; i++)
            {
                if (lastGuess.Statuses[i] == LetterStatus.Correct && guess[i] != lastGuess.Word[i])
                    return Result.FromError($"Hard Mode: {lastGuess.Word[i].ToString().ToUpper()} must be at position {i + 1}.");
            }

            // 2. Enforce Presence (yellow) letters
            var requiredCounts = new Dictionary<char, int>();
            for (int i = 0; i < lastGuess.Word.Length; i++)
            {
                if (lastGuess.Statuses[i] is LetterStatus.Correct or LetterStatus.Present)
                {
                    char c = lastGuess.Word[i];
                    requiredCounts[c] = requiredCounts.GetValueOrDefault(c) + 1;
                }
            }

            foreach (var (c, count) in requiredCounts)
            {
                int inGuess = guess.Count(x => x == c);
                if (inGuess < count)
                    return Result.FromError($"Hard Mode: Guess must contain at least {count} '{c.ToString().ToUpper()}'.");
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

    // Minimum fragment length that may participate in a compound decomposition. Without
    // this, any string of 1-char English words ("a", "i") composes — so "aia" would pass.
    private const int MinCompoundFragmentLength = 3;

    private static bool IsValidCompoundWord(string word, IWordListService service)
    {
        int n = word.Length;
        if (n == 0) return true;
        if (n < MinCompoundFragmentLength) return false;

        var span = word.AsSpan();
        bool[] dp = new bool[n + 1];
        dp[0] = true;

        for (int i = MinCompoundFragmentLength; i <= n; i++)
        {
            int maxStart = i - MinCompoundFragmentLength;
            for (int j = 0; j <= maxStart; j++)
            {
                if (dp[j] && service.IsValidWord(span[j..i]))
                {
                    dp[i] = true;
                    break;
                }
            }
        }
        return dp[n];
    }

    internal static GuessResult EvaluateGuess(string target, string guess)
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
        double g = 6.0 + multiplier * Math.Log2((double)length / 5.0);
        return Math.Max(1, (int)Math.Round(g, MidpointRounding.AwayFromZero));
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
