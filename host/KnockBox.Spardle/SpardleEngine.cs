using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle.Models;
using KnockBox.Spardle.Services;
using Microsoft.Extensions.Logging;

namespace KnockBox.Spardle;

public class SpardleEngine(WordListService wordListService, ILoggerFactory loggerFactory) : AbstractGameEngine(1, 20)
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
            s.UpdateJoinableStatus(false);
            s.CurrentRound = 0;
            // Generate queue based on host settings
            GenerateRoundQueue(s);
            StartNextRound(s);
        }));
    }

    private void GenerateRoundQueue(SpardleState state)
    {
        state.RoundQueue.Clear();
        // In a real implementation, we would shuffle or pick from standard lists.
        // For simplicity of this milestone skeleton, we use the custom word pool or some defaults.
        var pool = state.CustomWordPool.Count > 0 ? state.CustomWordPool : new List<string> { "apple", "brave", "crane" };

        if (state.WordOrderMode == WordOrderMode.RandomNoRepeats || state.WordOrderMode == WordOrderMode.RandomWithRepeats)
        {
            pool = pool.OrderBy(x => Guid.NewGuid()).ToList();
        }
        else if (state.WordOrderMode == WordOrderMode.ReverseListOrder)
        {
            pool.Reverse();
        }

        // Apply TotalRounds limit
        int limit = state.TotalRounds > 0 ? state.TotalRounds : pool.Count;
        state.RoundQueue.AddRange(pool.Take(Math.Min(limit, pool.Count)));
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

        foreach (var p in state.PlayerStates.Values)
        {
            p.ResetRound();
        }
    }

    public Result SubmitGuess(SpardleState state, User player, string guess)
    {
        Result errorResult = Result.Success;
        var executeResult = state.Execute(() =>
        {
            if (!state.IsRoundActive) { errorResult = Result.FromError("Round is not active."); return; }
            if (!state.PlayerStates.TryGetValue(player.Id, out var pState)) { errorResult = Result.FromError("Player not found."); return; }
            if (pState.HasFinishedRound) { errorResult = Result.FromError("You have already finished this round."); return; }

            guess = guess.ToLowerInvariant();
            
            // 1. Validation
            var valResult = ValidateGuess(state, pState, guess);
            if (!valResult.IsSuccess) { errorResult = valResult; return; }

            // 2. Evaluate
            var result = EvaluateGuess(state.TargetWord, guess);
            pState.Guesses.Add(result);

            // 3. Check for win/loss
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
                pState.Dnf = true; // Exhausted guesses
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

        // Hard Mode Check
        if (state.HardModeEnabled && pState.Guesses.Count > 0)
        {
            var lastGuess = pState.Guesses.Last();
            for (int i = 0; i < lastGuess.Word.Length; i++)
            {
                if (lastGuess.Statuses[i] == LetterStatus.Correct && guess[i] != lastGuess.Word[i])
                    return Result.FromError("Hard Mode: Must use correct letters in the correct spot.");
            }
            // Real Hard Mode also checks for 'Present' letters, simplified here for brevity.
        }

        // Dictionary Check
        if (!state.AllowDictionaryFallback)
        {
            // Must be in the specific pool
            if (!state.CustomWordPool.Contains(guess) && !wordListService.IsValidWord(guess, state.WordPoolMode))
                return Result.FromError("Word not in list.");
        }
        else
        {
            if (!wordListService.IsValidWord(guess, WordPoolMode.FullDictionary))
            {
                if (state.AllowCompoundWords)
                {
                    if (!IsValidCompoundWord(guess, wordListService.GetFullDictionary()))
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

    // DP Algorithm for Compound Word Validation
    private bool IsValidCompoundWord(string word, IReadOnlySet<string> dictionary)
    {
        int n = word.Length;
        if (n == 0) return true;

        bool[] dp = new bool[n + 1];
        dp[0] = true;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 0; j < i; j++)
            {
                if (dp[j] && dictionary.Contains(word.Substring(j, i - j)))
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

        // Pass 1: Correct
        for (int i = 0; i < guess.Length; i++)
        {
            if (guess[i] == target[i])
            {
                statuses[i] = LetterStatus.Correct;
                targetChars[i] = '\0'; // Mark as used
            }
        }

        // Pass 2: Present/Absent
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
            IsCorrect = statuses.All(s => s == LetterStatus.Correct)
        };
    }

    // Dynamic Guess Limit: G = Round(6 + k * ln(L / 5))
    public static int CalculateMaxGuesses(int length, double multiplier)
    {
        if (length <= 0) return 6;
        double g = 6.0 + multiplier * Math.Log((double)length / 5.0);
        return (int)Math.Round(g, MidpointRounding.AwayFromZero);
    }

    private void CheckRoundEnd(SpardleState state)
    {
        if (state.WaitForAll)
        {
            if (state.PlayerStates.Values.All(p => p.HasFinishedRound))
            {
                state.IsRoundActive = false;
                StartNextRound(state);
            }
        }
        else if (state.WinCondition == WinConditionMode.Sprinter)
        {
            if (state.PlayerStates.Values.Any(p => p.HasFinishedRound && !p.Dnf))
            {
                state.IsRoundActive = false;
                StartNextRound(state);
            }
        }
        // Additional modes and timer expirations would be handled via callbacks in a full implementation.
    }
}
