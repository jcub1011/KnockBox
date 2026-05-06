using System.Collections.Immutable;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle;
using KnockBox.Spardle.Models;
using KnockBox.Spardle.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.SpardleTests.Unit;

[TestClass]
public class SpardleEngineTests
{
    private SpardleEngine _engine = default!;
    private SequentialRng _rng = default!;

    [TestInitialize]
    public void Setup()
    {
        _rng = new SequentialRng();
        _engine = new SpardleEngine(
            new WordListService(NullLogger<WordListService>.Instance),
            _rng,
            new NullLoggerFactory());
    }

    /// <summary>
    /// Deterministic RNG: returns the call counter modulo the requested range.
    /// For RandomNoRepeats (HashSet rejection-sample loop) this produces 0, 1, 2, ...
    /// guaranteeing distinct picks until the counter wraps.
    /// </summary>
    private sealed class SequentialRng : IRandomNumberService
    {
        private int _counter;
        public int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast)
            => exclusiveMax <= 0 ? 0 : _counter++ % exclusiveMax;
        public int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast)
        {
            int range = exclusiveMax - inclusiveMin;
            return range <= 0 ? inclusiveMin : inclusiveMin + (_counter++ % range);
        }
        public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast)
            => new byte[length];
    }

    // ───────────────────────────────────────────────────────────────────────
    // Dynamic guess limit
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(5, 2.0, 6)]
    [DataRow(10, 2.0, 8)]
    [DataRow(15, 2.0, 9)]
    [DataRow(3, 2.0, 6)]   // would be 5 without the floor
    [DataRow(3, 3.0, 6)]   // would be 4 without the floor
    [DataRow(4, 3.0, 6)]   // would be 5 without the floor
    public void CalculateMaxGuesses_ReturnsExpected(int length, double multiplier, int expected)
    {
        int result = SpardleEngine.CalculateMaxGuesses(length, multiplier);
        Assert.AreEqual(expected, result);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Scoring formulas
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(5, 1, 11)]   // floor(11.99)
    [DataRow(5, 2, 5)]    // floor(5.99)
    [DataRow(5, 3, 3)]    // floor(3.99)
    [DataRow(10, 1, 20)]  // floor(20.0)
    [DataRow(10, 2, 10)]  // floor(10.0)
    [DataRow(10, 4, 5)]   // floor(5.0)
    [DataRow(15, 1, 26)]  // floor(26.76)
    [DataRow(15, 2, 13)]  // floor(13.38)
    [DataRow(5, 20, 0)]   // floor(0.59) — large place collapses to 0
    [DataRow(0, 1, 0)]    // guard: invalid wordLength
    [DataRow(5, 0, 0)]    // guard: invalid placement
    [DataRow(-1, 1, 0)]   // guard: negative wordLength
    public void PointsForSolver_MatchesFormula(int wordLength, int placement, int expected)
    {
        Assert.AreEqual(expected, SpardleEngine.PointsForSolver(wordLength, placement));
    }

    [TestMethod]
    [DataRow(5, 4, 11, 8)]    // floor(0.8 × 11) = 8
    [DataRow(10, 7, 20, 14)]  // floor(0.7 × 20) = 14
    [DataRow(10, 9, 20, 18)]  // max correct (n-1) still strictly < anchor
    [DataRow(10, 0, 20, 0)]   // zero correct
    [DataRow(5, 3, 0, 0)]     // zero anchor
    [DataRow(10, 1, 2, 0)]    // floor(0.1 × 2) = 0 — small percent against small anchor
    [DataRow(0, 1, 11, 0)]    // guard: invalid wordLength
    [DataRow(5, -1, 11, 0)]   // guard: negative correctCount
    [DataRow(5, 3, -1, 0)]    // guard: negative anchor
    public void PointsForNonSolver_ScalesByPercent(int wordLength, int correctCount, int anchor, int expected)
    {
        Assert.AreEqual(expected, SpardleEngine.PointsForNonSolver(wordLength, correctCount, anchor));
    }

    [TestMethod]
    public void BestCorrectCount_ReturnsMaxAcrossGuesses()
    {
        var guesses = new List<GuessResult>
        {
            MakeGuess("PPPPP"),  // 0 correct
            MakeGuess("CCPAA"),  // 2 correct
            MakeGuess("CCCAA"),  // 3 correct
            MakeGuess("CACAC"),  // 3 correct (tie)
        };
        Assert.AreEqual(3, SpardleEngine.BestCorrectCount(guesses));
    }

    [TestMethod]
    public void BestCorrectCount_EmptyList_ReturnsZero()
    {
        Assert.AreEqual(0, SpardleEngine.BestCorrectCount(new List<GuessResult>()));
    }

    private static GuessResult MakeGuess(string statusCode) => new()
    {
        Word = new string('a', statusCode.Length),
        Statuses = statusCode.Select(c => c switch
        {
            'C' => LetterStatus.Correct,
            'P' => LetterStatus.Present,
            _ => LetterStatus.Absent
        }).ToArray(),
        IsCorrect = statusCode.All(c => c == 'C')
    };

    // ───────────────────────────────────────────────────────────────────────
    // Phase transitions
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StartAsync_EntersRoundIntroWithPhaseExpiry()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 2;
        state.TransitionDuration = TimeSpan.FromSeconds(5);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.CustomWordPool = ImmutableList.Create("apple", "brave");

        var start = DateTimeOffset.UtcNow;
        var result = await _engine.StartAsync(host, state);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(GamePhase.RoundIntro, state.Phase);
        Assert.IsFalse(state.IsJoinable);
        Assert.IsNotNull(state.PhaseExpiresAtUtc);
        var actualDelay = state.PhaseExpiresAtUtc!.Value - start;
        Assert.IsTrue(actualDelay.TotalMilliseconds > 4500 && actualDelay.TotalMilliseconds < 6000);
    }

    [TestMethod]
    public async Task StartAsync_NonHost_ReturnsError()
    {
        var (state, _) = await CreateStateAsync();
        state.CustomWordPool = ImmutableList.Create("apple");
        var nonHost = UserFactory.Create("NotHost", "nothost-id");

        var result = await _engine.StartAsync(nonHost, state);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task TransitionFromIntroToPlaying_FiresAfterDelay()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.CustomWordPool = ["apple"];

        await _engine.StartAsync(host, state);

        Assert.AreEqual(GamePhase.RoundIntro, state.Phase);

        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);
        Assert.AreEqual(GamePhase.Playing, state.Phase);
        Assert.AreEqual("apple", state.TargetWord);
        Assert.IsTrue(state.IsRoundActive);
        Assert.AreEqual(1, state.CurrentRound);
    }

    [TestMethod]
    public async Task SprinterWin_EntersRoundResultsThenNextRoundPlaying()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 2;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Sprinter;
        state.CustomWordPool = ImmutableList.Create("apple", "brave");
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var submitResult = _engine.SubmitGuess(state, host, "apple");
        Assert.IsTrue(submitResult.IsSuccess, "submit should succeed");
        Assert.AreEqual(GamePhase.RoundResults, state.Phase);
        Assert.HasCount(1, state.RoundHistory);

        var outcome = state.RoundHistory[0].Outcomes.Single(o => o.UserId == host.Id);
        Assert.AreEqual(1, outcome.Placement);
        Assert.AreEqual(11, outcome.PointsAwarded);  // floor(10·log10(5)+5) = 11
        Assert.AreEqual(11, state.PlayerStates[host.Id].TotalScore);

        // After the results delay, skip the round-intro countdown and start the next round directly.
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);
        Assert.AreEqual(GamePhase.Playing, state.Phase);
        Assert.AreEqual(2, state.CurrentRound);
        Assert.AreEqual("brave", state.TargetWord);
    }

    [TestMethod]
    public async Task RoundTimerExpiry_MarksAllUnfinishedAsDnf()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(60);
        state.RoundTimer = TimeSpan.FromMilliseconds(150);
        state.CustomWordPool = ["apple"];

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);
        // Don't submit anything — wait for timer to expire and push us into results.
        await WaitForPhaseAsync(state, GamePhase.RoundResults, timeoutMs: 2000);

        var outcome = state.RoundHistory[0].Outcomes.Single();
        Assert.IsTrue(outcome.Dnf);
        Assert.AreEqual(0, outcome.PointsAwarded);
        Assert.AreEqual(0, state.PlayerStates[host.Id].TotalScore);

        // After results, final round complete → GameOver.
        await WaitForPhaseAsync(state, GamePhase.GameOver, timeoutMs: 1500);
        Assert.IsTrue(state.IsGameOver);
    }

    [TestMethod]
    public async Task LastRound_TransitionsToGameOver()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(60);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Sprinter;
        state.CustomWordPool = ["apple"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        _engine.SubmitGuess(state, host, "apple");
        Assert.AreEqual(GamePhase.RoundResults, state.Phase);

        await WaitForPhaseAsync(state, GamePhase.GameOver, timeoutMs: 1500);
        Assert.IsTrue(state.IsGameOver);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Host-as-observer mode
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StartAsync_HostSolo_CreatesHostPlayerState()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.CustomWordPool = ["apple"];

        await _engine.StartAsync(host, state);

        Assert.IsTrue(state.HostIsParticipant);
        Assert.IsTrue(state.PlayerStates.ContainsKey(host.Id));
    }

    [TestMethod]
    public async Task StartAsync_WithOtherPlayers_HostBecomesObserver()
    {
        var (state, host, players) = await CreateStateWithPlayersAsync(1);
        state.TotalRounds = 1;
        state.CustomWordPool = ["apple"];

        await _engine.StartAsync(host, state);

        Assert.IsFalse(state.HostIsParticipant);
        Assert.IsFalse(state.PlayerStates.ContainsKey(host.Id));
        Assert.IsTrue(state.PlayerStates.ContainsKey(players[0].Id));
    }

    [TestMethod]
    public async Task SubmitGuess_ObserverHost_ReturnsError()
    {
        var (state, host, _) = await CreateStateWithPlayersAsync(1);
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.CustomWordPool = ["apple"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "apple");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.TryGetFailure(out var failure));
        Assert.Contains("observing", failure.PublicMessage);
        Assert.IsFalse(state.PlayerStates.ContainsKey(host.Id), "host PlayerState must not be materialized by a rejected guess");
    }

    [TestMethod]
    public async Task SubmitGuess_SoloHost_PlaysNormally()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Sprinter;
        state.CustomWordPool = ["apple"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "apple");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(GamePhase.RoundResults, state.Phase);
        var outcome = state.RoundHistory[0].Outcomes.Single(o => o.UserId == host.Id);
        Assert.AreEqual(1, outcome.Placement);
        Assert.AreEqual(11, outcome.PointsAwarded);  // floor(10·log10(5)+5) = 11
    }

    [TestMethod]
    public async Task BuildOutcomes_ObserverMode_ExcludesHost()
    {
        var (state, host, players) = await CreateStateWithPlayersAsync(1);
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Sprinter;
        state.CustomWordPool = ["apple"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var submitResult = _engine.SubmitGuess(state, players[0], "apple");
        Assert.IsTrue(submitResult.IsSuccess);

        Assert.HasCount(1, state.RoundHistory[0].Outcomes);
        Assert.AreEqual(players[0].Id, state.RoundHistory[0].Outcomes[0].UserId);
        Assert.IsFalse(state.RoundHistory[0].Outcomes.Any(o => o.UserId == host.Id));
    }

    [TestMethod]
    public async Task CheckRoundEnd_ObserverMode_EndsOnParticipantCompletion()
    {
        var (state, host, players) = await CreateStateWithPlayersAsync(1);
        state.TotalRounds = 2;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Sprinter;
        state.CustomWordPool = ImmutableList.Create("apple", "brave");
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        _engine.SubmitGuess(state, players[0], "apple");

        Assert.AreEqual(GamePhase.RoundResults, state.Phase);
    }

    [TestMethod]
    public async Task RoundTimerExpiry_ObserverMode_DoesNotDnfHost()
    {
        var (state, host, players) = await CreateStateWithPlayersAsync(1);
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(60);
        state.RoundTimer = TimeSpan.FromMilliseconds(150);
        state.CustomWordPool = ["apple"];

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);
        await WaitForPhaseAsync(state, GamePhase.RoundResults, timeoutMs: 2000);

        Assert.HasCount(1, state.RoundHistory[0].Outcomes);
        Assert.AreEqual(players[0].Id, state.RoundHistory[0].Outcomes[0].UserId);
        Assert.IsFalse(state.RoundHistory[0].Outcomes.Any(o => o.UserId == host.Id));
    }

    // ───────────────────────────────────────────────────────────────────────
    // Round queue generation (real WordListService)
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StartAsync_NytStandard_FillsQueueWithFiveLetterWordsFromService()
    {
        var (state, host) = await CreateStateAsync();
        state.WordPoolMode = WordPoolMode.NytStandard;
        state.WordOrderMode = WordOrderMode.ListOrder;
        state.TotalRounds = 3;

        await _engine.StartAsync(host, state);

        Assert.HasCount(3, state.RoundQueue);
        foreach (var w in state.RoundQueue)
            Assert.AreEqual(5, w.Length);
        Assert.AreEqual("aback", state.RoundQueue[0]);
    }

    [TestMethod]
    public async Task StartAsync_FullDictionaryConstantLength_UsesTargetLength()
    {
        var (state, host) = await CreateStateAsync();
        state.WordPoolMode = WordPoolMode.FullDictionary;
        state.ConstantWordLength = true;
        state.TargetWordLength = 7;
        state.WordOrderMode = WordOrderMode.ListOrder;
        state.TotalRounds = 5;

        await _engine.StartAsync(host, state);

        Assert.HasCount(5, state.RoundQueue);
        foreach (var w in state.RoundQueue)
            Assert.AreEqual(7, w.Length);
    }

    [TestMethod]
    public async Task StartAsync_FullDictionaryRange_AllWordsWithinBounds()
    {
        var (state, host) = await CreateStateAsync();
        state.WordPoolMode = WordPoolMode.FullDictionary;
        state.ConstantWordLength = false;
        state.MinWordLength = 5;
        state.MaxWordLength = 7;
        state.WordOrderMode = WordOrderMode.RandomNoRepeats;
        state.TotalRounds = 20;

        await _engine.StartAsync(host, state);

        Assert.HasCount(20, state.RoundQueue);
        foreach (var w in state.RoundQueue)
            Assert.IsTrue(w.Length >= 5 && w.Length <= 7, $"'{w}' length {w.Length} outside [5,7]");
    }

    [TestMethod]
    public async Task StartAsync_FullDictionaryRange_ExcludesLengthsOutsideBounds()
    {
        var (state, host) = await CreateStateAsync();
        state.WordPoolMode = WordPoolMode.FullDictionary;
        state.ConstantWordLength = false;
        state.MinWordLength = 4;
        state.MaxWordLength = 4;
        state.WordOrderMode = WordOrderMode.ListOrder;
        state.TotalRounds = 10;

        await _engine.StartAsync(host, state);

        Assert.HasCount(10, state.RoundQueue);
        foreach (var w in state.RoundQueue)
            Assert.AreEqual(4, w.Length);
    }

    [TestMethod]
    public async Task StartAsync_FullDictionaryRange_InvertedBounds_ProducesEmptyQueue()
    {
        var (state, host) = await CreateStateAsync();
        state.WordPoolMode = WordPoolMode.FullDictionary;
        state.ConstantWordLength = false;
        state.MinWordLength = 10;
        state.MaxWordLength = 5;
        state.WordOrderMode = WordOrderMode.ListOrder;
        state.TotalRounds = 3;

        await _engine.StartAsync(host, state);

        Assert.IsEmpty(state.RoundQueue);
    }

    [TestMethod]
    public async Task StartAsync_CustomPool_OverridesPoolModeAndIgnoresLength()
    {
        var (state, host) = await CreateStateAsync();
        state.WordPoolMode = WordPoolMode.FullDictionary;
        state.ConstantWordLength = true;
        state.TargetWordLength = 7;
        state.CustomWordPool = ImmutableList.Create("alpha", "betas", "gamma");
        state.TotalRounds = 3;
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);

        Assert.HasCount(3, state.RoundQueue);
        Assert.AreEqual("alpha", state.RoundQueue[0]);
        Assert.AreEqual("betas", state.RoundQueue[1]);
        Assert.AreEqual("gamma", state.RoundQueue[2]);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────

    private async Task<(SpardleState state, User host)> CreateStateAsync()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid().ToString());
        var abstractResult = await _engine.CreateStateAsync(host);
        Assert.IsTrue(abstractResult.TryGetSuccess(out var abstractState));
        return ((SpardleState)abstractState, host);
    }

    private async Task<(SpardleState state, User host, List<User> players)> CreateStateWithPlayersAsync(int playerCount)
    {
        var (state, host) = await CreateStateAsync();
        var players = new List<User>();
        for (int i = 0; i < playerCount; i++)
        {
            var player = UserFactory.Create($"P{i + 1}", Guid.NewGuid().ToString());
            var reg = state.RegisterPlayer(player);
            Assert.IsTrue(reg.IsSuccess, $"RegisterPlayer failed: {reg}");
            players.Add(player);
        }
        return (state, host, players);
    }

    private static async Task WaitForPhaseAsync(SpardleState state, GamePhase target, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (state.Phase == target) return;
            await Task.Delay(20);
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Hard mode
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SubmitGuess_HardMode_FirstGuessHasNoConstraint()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.HardModeEnabled = true;
        state.AllowDictionaryFallback = true;
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.CustomWordPool = ["apple"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "crane");

        Assert.IsTrue(result.IsSuccess, $"First guess should be unconstrained in hard mode: {result}");
    }

    [TestMethod]
    public async Task SubmitGuess_HardMode_RequiresConfirmedLettersInPlace()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.HardModeEnabled = true;
        state.AllowDictionaryFallback = true;
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.CustomWordPool = ["apple"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        // "amply" locks 'a' at index 0 (correct); 'p' at index 2 (correct).
        var first = _engine.SubmitGuess(state, host, "amply");
        Assert.IsTrue(first.IsSuccess, $"first guess should succeed: {first}");

        // "bland" drops both locked letters — must be rejected.
        var second = _engine.SubmitGuess(state, host, "bland");
        Assert.IsFalse(second.IsSuccess);
        Assert.IsTrue(second.TryGetFailure(out var failure));
        Assert.Contains("Hard Mode", failure.PublicMessage);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Compound word decomposition
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SubmitGuess_CompoundWordsAllowed_AcceptsConcatenationOfThreePlusLetterWords()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.AllowDictionaryFallback = true;
        state.AllowCompoundWords = true;
        state.CustomWordPool = ["aaaaaa"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        // "cathat" = "cat" + "hat"; both are 3-letter dictionary words. "cathat" itself is
        // not in the dictionary, so the compound DP actually has to run.
        var result = _engine.SubmitGuess(state, host, "cathat");
        Assert.IsTrue(result.IsSuccess, $"valid compound should be accepted: {result}");
    }

    [TestMethod]
    public async Task SubmitGuess_CompoundWordsAllowed_RejectsShortFragmentDecomposition()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.AllowDictionaryFallback = true;
        state.AllowCompoundWords = true;
        state.CustomWordPool = ["aaa"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        // "aia" would decompose as "a"+"i"+"a" only if 1-char fragments were allowed.
        var result = _engine.SubmitGuess(state, host, "aia");
        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.TryGetFailure(out var failure));
        Assert.Contains("Not a valid", failure.PublicMessage);
    }

    [TestMethod]
    public async Task SubmitGuess_CompoundWordsAllowed_RejectsGarbage()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.AllowDictionaryFallback = true;
        state.AllowCompoundWords = true;
        // Pool word is the target. Submit a different 7-char garbage so the custom-pool
        // shortcut in ValidateGuess can't accept it — the test has to actually reach the
        // compound-word decomposition path.
        state.CustomWordPool = ["xzqwplm"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "qkpvtxz");
        Assert.IsFalse(result.IsSuccess);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Round-end conditions
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CheckRoundEnd_WaitForAll_HoldsRoundUntilAllFinish()
    {
        var (state, host, players) = await CreateStateWithPlayersAsync(2);
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WaitForAll = true;
        state.WinCondition = WinConditionMode.Sprinter;
        state.CustomWordPool = ["apple"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var first = _engine.SubmitGuess(state, players[0], "apple");
        Assert.IsTrue(first.IsSuccess);
        // With WaitForAll=true, the round must NOT advance even though a sprinter has solved.
        Assert.AreEqual(GamePhase.Playing, state.Phase);

        // Second player DNFs by exhausting their guesses (max guesses for length 5, k=2 → 6).
        for (int i = 0; i < 6; i++)
        {
            _ = _engine.SubmitGuess(state, players[1], "crane");
        }

        Assert.AreEqual(GamePhase.RoundResults, state.Phase);
    }

    [TestMethod]
    public async Task BuildOutcomes_Tactician_RanksByFewestGuesses()
    {
        var (state, host, players) = await CreateStateWithPlayersAsync(2);
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Tactician;
        state.WaitForAll = true;
        state.AllowDictionaryFallback = true;
        state.CustomWordPool = ["apple"];
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        // P1 needs two guesses; P2 solves on the first.
        Assert.IsTrue(_engine.SubmitGuess(state, players[0], "crane").IsSuccess);
        Assert.IsTrue(_engine.SubmitGuess(state, players[1], "apple").IsSuccess);
        Assert.IsTrue(_engine.SubmitGuess(state, players[0], "apple").IsSuccess);

        await WaitForPhaseAsync(state, GamePhase.RoundResults, timeoutMs: 1500);

        var p1Outcome = state.RoundHistory[0].Outcomes.Single(o => o.UserId == players[0].Id);
        var p2Outcome = state.RoundHistory[0].Outcomes.Single(o => o.UserId == players[1].Id);

        Assert.AreEqual(1, p2Outcome.Placement, "Tactician should rank fewest-guesses first");
        Assert.AreEqual(11, p2Outcome.PointsAwarded);  // floor(10·log10(5)+5) = 11
        Assert.AreEqual(2, p1Outcome.Placement);
        Assert.AreEqual(5, p1Outcome.PointsAwarded);   // floor(11.99/2) = 5
    }

    // ───────────────────────────────────────────────────────────────────────
    // Duplicate-letter evaluation
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("lilac", "llama", "CPPAA")] // second 'l' matches target's still-unconsumed 'l' at index 2; first 'a' matches target's 'a'; second 'a' absent
    [DataRow("apple", "paper", "PPCPA")] // p (pos 0) present; a present; p (pos 2) correct; e present; r absent
    [DataRow("apple", "aaaaa", "CAAAA")] // only one 'a' in target
    public void EvaluateGuess_DuplicateLetters_MatchWordleRules(string target, string guess, string expectedStatuses)
    {
        var result = SpardleEngine.EvaluateGuess(target, guess);
        var actual = string.Concat(result.Statuses.Select(StatusCode));
        Assert.AreEqual(expectedStatuses, actual, $"target={target}, guess={guess}");
    }

    private static char StatusCode(LetterStatus s) => s switch
    {
        LetterStatus.Correct => 'C',
        LetterStatus.Present => 'P',
        LetterStatus.Absent => 'A',
        _ => '?'
    };

    // ───────────────────────────────────────────────────────────────────────
    // Unique-index sampler (hybrid Fisher–Yates / rejection sampling)
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StartAsync_LargePoolSmallTake_ProducesUniqueIndicesViaRejectionPath()
    {
        // Full dictionary length 5 has ~10k entries — take=10 triggers rejection sampling.
        var (state, host) = await CreateStateAsync();
        state.WordPoolMode = WordPoolMode.FullDictionary;
        state.ConstantWordLength = true;
        state.TargetWordLength = 5;
        state.WordOrderMode = WordOrderMode.RandomNoRepeats;
        state.TotalRounds = 10;

        await _engine.StartAsync(host, state);

        Assert.HasCount(10, state.RoundQueue);
        Assert.AreEqual(10, state.RoundQueue.Distinct().Count(), "all sampled words must be unique");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Non-solver scoring (percent × lowest-solver anchor)
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildOutcomes_NonSolverPointsAnchorOnLowestSolver()
    {
        // Two solvers (A 1st, B 2nd) and two non-solvers on a 5-letter word.
        // n=5, place=1 → 11; place=2 → 5. Non-solver anchor = 5 (lowest solver).
        var (state, host, players) = await CreateStateWithPlayersAsync(4);
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Tactician;
        state.WaitForAll = true;
        state.AllowDictionaryFallback = true;
        state.CustomWordPool = ["apple"];
        state.CustomWordPoolLookup = ImmutableHashSet.Create("apple");
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        // A solves on guess 1 → 1st place. B solves on guess 2 → 2nd place.
        Assert.IsTrue(_engine.SubmitGuess(state, players[0], "apple").IsSuccess);
        Assert.IsTrue(_engine.SubmitGuess(state, players[1], "crane").IsSuccess);
        Assert.IsTrue(_engine.SubmitGuess(state, players[1], "apple").IsSuccess);

        // C exhausts 6 guesses with 4 correct letters in best guess ("apply" → CCCCA).
        for (int i = 0; i < 6; i++)
            _ = _engine.SubmitGuess(state, players[2], "apply");

        // D exhausts 6 guesses with 1 correct letter in best guess ("axxxx" → CAAAA).
        for (int i = 0; i < 6; i++)
            _ = _engine.SubmitGuess(state, players[3], "ample");

        await WaitForPhaseAsync(state, GamePhase.RoundResults, timeoutMs: 1500);

        var outcomes = state.RoundHistory[0].Outcomes;
        var a = outcomes.Single(o => o.UserId == players[0].Id);
        var b = outcomes.Single(o => o.UserId == players[1].Id);
        var c = outcomes.Single(o => o.UserId == players[2].Id);
        var d = outcomes.Single(o => o.UserId == players[3].Id);

        Assert.AreEqual(11, a.PointsAwarded);
        Assert.AreEqual(5, b.PointsAwarded);
        // C: best guess "apply" has 4 correct positions out of 5 → floor(0.8 × 5) = 4.
        Assert.AreEqual(4, c.PointsAwarded);
        Assert.IsLessThan(b.PointsAwarded, c.PointsAwarded, "non-solver must score below lowest solver");
        // D: "ample" → A:correct, m:absent, p:correct, l:correct, e:correct = 4 correct.
        // floor(0.8 × 5) = 4. Same as C.
        Assert.AreEqual(4, d.PointsAwarded);
    }

    [TestMethod]
    public async Task BuildOutcomes_AllDnf_AnchorsAtFirstPlaceFormula()
    {
        // No one solves: anchor falls back to PointsForSolver(5, 1) = 11.
        // Player has best-guess "apply" (4 correct out of 5) → floor(0.8 × 11) = 8.
        var (state, host, players) = await CreateStateWithPlayersAsync(1);
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(60);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.AllowDictionaryFallback = true;
        state.CustomWordPool = ["apple"];
        state.CustomWordPoolLookup = ImmutableHashSet.Create("apple");
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        // Burn 6 guesses, none correct, best has 4 correct positions.
        for (int i = 0; i < 6; i++)
            _ = _engine.SubmitGuess(state, players[0], "apply");

        await WaitForPhaseAsync(state, GamePhase.RoundResults, timeoutMs: 1500);

        var outcome = state.RoundHistory[0].Outcomes.Single();
        Assert.IsTrue(outcome.Dnf);
        Assert.AreEqual(8, outcome.PointsAwarded);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Custom-pool validation under dictionary fallback
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ValidateGuess_AcceptsCustomPoolWord_WhenFallbackEnabled()
    {
        // "zorks" is not in the dictionary but is in the custom pool. With fallback
        // enabled, the engine must still accept it as a valid guess.
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.AllowDictionaryFallback = true;
        state.CustomWordPool = ["apple", "zorks"];
        state.CustomWordPoolLookup = ImmutableHashSet.Create("apple", "zorks");
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "zorks");

        Assert.IsTrue(result.IsSuccess, $"custom-pool word should be accepted with fallback on: {result}");
    }

    [TestMethod]
    public async Task ValidateGuess_RejectsRandomGarbage_WhenFallbackEnabled()
    {
        // Sanity check: a non-dictionary, non-custom-pool word still fails.
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.AllowDictionaryFallback = true;
        state.CustomWordPool = ["apple"];
        state.CustomWordPoolLookup = ImmutableHashSet.Create("apple");
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "zxqwk");

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task ValidateGuess_AcceptsCustomPoolWord_WhenFallbackDisabled()
    {
        // No-fallback mode + custom pool: only words in the custom pool (and the built-in
        // pool for the selected mode) are accepted. A coined word like "zorks" must still
        // be accepted because it's in the custom pool.
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.AllowDictionaryFallback = false;
        state.CustomWordPool = ["zorks", "apple"];
        state.CustomWordPoolLookup = ImmutableHashSet.Create("zorks", "apple");
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "zorks");

        Assert.IsTrue(result.IsSuccess, $"custom-pool word should be accepted with fallback off: {result}");
    }

    [TestMethod]
    public async Task ValidateGuess_RejectsDictionaryWordNotInPool_WhenFallbackDisabled()
    {
        // No-fallback mode: a real word that ISN'T in the custom pool (and isn't in the
        // built-in NYT pool either, since CustomWordPool overrides the round queue but
        // WordPoolMode still drives IsInPool) should fail. We pick a guess of matching
        // length that we know isn't in the NYT-standard 5-letter list.
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.AllowDictionaryFallback = false;
        state.WordPoolMode = WordPoolMode.HostDefined;
        state.CustomWordPool = ["zorks"];
        state.CustomWordPoolLookup = ImmutableHashSet.Create("zorks");
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        // "crane" is a real word but not in the custom pool. HostDefined mode has no
        // backing pool in IsInPool, so the check returns false → rejection.
        var result = _engine.SubmitGuess(state, host, "crane");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.TryGetFailure(out var failure));
        Assert.Contains("not in list", failure.PublicMessage);
    }

    [TestMethod]
    public async Task StartAsyncCore_RebuildsCustomWordPoolLookup_WhenOutOfSync()
    {
        // If state is initialized programmatically with CustomWordPool but no Lookup,
        // StartAsyncCore's safety belt rebuilds the lookup.
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.CustomWordPool = ["apple", "zorks"];
        // Intentionally leave CustomWordPoolLookup empty.

        await _engine.StartAsync(host, state);

        Assert.HasCount(2, state.CustomWordPoolLookup);
        Assert.Contains("apple", state.CustomWordPoolLookup);
        Assert.Contains("zorks", state.CustomWordPoolLookup);
    }

    [TestMethod]
    public async Task StartAsync_SmallPoolExhaustiveTake_UsesShufflePath()
    {
        // NYT-standard length 5 is several thousand words. We request all of them — the
        // take/total ratio forces the Fisher–Yates branch and must terminate (the old
        // rejection-sampling implementation would stall near-completion).
        var (state, host) = await CreateStateAsync();
        state.WordPoolMode = WordPoolMode.NytStandard;
        state.WordOrderMode = WordOrderMode.RandomNoRepeats;
        state.TotalRounds = int.MaxValue; // engine clamps to total available

        await _engine.StartAsync(host, state);

        int total = new WordListService(NullLogger<WordListService>.Instance).GetWordCount(WordPoolMode.NytStandard, 5);
        Assert.HasCount(total, state.RoundQueue);
        Assert.AreEqual(total, state.RoundQueue.Distinct().Count());
    }
}
