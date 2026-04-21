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
    [DataRow(10, 2.0, 7)]
    [DataRow(15, 2.0, 8)]
    public void CalculateMaxGuesses_ReturnsExpected(int length, double multiplier, int expected)
    {
        int result = SpardleEngine.CalculateMaxGuesses(length, multiplier);
        Assert.AreEqual(expected, result);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Scoring table
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(1, true, 10)]
    [DataRow(2, true, 5)]
    [DataRow(3, true, 2)]
    [DataRow(4, true, 1)]
    [DataRow(7, true, 1)]
    [DataRow(0, false, 0)]
    public void PointsForPlacement_MatchesGdd(int placement, bool solved, int expected)
    {
        Assert.AreEqual(expected, SpardleEngine.PointsForPlacement(placement, solved));
    }

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
        state.CustomWordPool = new List<string> { "apple", "brave" };

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
    public async Task TransitionFromIntroToPlaying_FiresAfterDelay()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.CustomWordPool = new List<string> { "apple" };

        await _engine.StartAsync(host, state);

        Assert.AreEqual(GamePhase.RoundIntro, state.Phase);

        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);
        Assert.AreEqual(GamePhase.Playing, state.Phase);
        Assert.AreEqual("apple", state.TargetWord);
        Assert.IsTrue(state.IsRoundActive);
        Assert.AreEqual(1, state.CurrentRound);
    }

    [TestMethod]
    public async Task SprinterWin_EntersRoundResultsThenNextIntro()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 2;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Sprinter;
        state.CustomWordPool = new List<string> { "apple", "brave" };
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var submitResult = _engine.SubmitGuess(state, host, "apple");
        Assert.IsTrue(submitResult.IsSuccess, "submit should succeed");
        Assert.AreEqual(GamePhase.RoundResults, state.Phase);
        Assert.AreEqual(1, state.RoundHistory.Count);

        var outcome = state.RoundHistory[0].Outcomes.Single(o => o.UserId == host.Id);
        Assert.AreEqual(1, outcome.Placement);
        Assert.AreEqual(10, outcome.PointsAwarded);
        Assert.AreEqual(10, state.PlayerStates[host.Id].TotalScore);

        // After the transition delay, next round's intro begins.
        await WaitForPhaseAsync(state, GamePhase.RoundIntro, timeoutMs: 1500);
        Assert.AreEqual(GamePhase.RoundIntro, state.Phase);
    }

    [TestMethod]
    public async Task RoundTimerExpiry_MarksAllUnfinishedAsDnf()
    {
        var (state, host) = await CreateStateAsync();
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(60);
        state.RoundTimer = TimeSpan.FromMilliseconds(150);
        state.CustomWordPool = new List<string> { "apple" };

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
        state.CustomWordPool = new List<string> { "apple" };
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
        state.CustomWordPool = new List<string> { "apple" };

        await _engine.StartAsync(host, state);

        Assert.IsTrue(state.HostIsParticipant);
        Assert.IsTrue(state.PlayerStates.ContainsKey(host.Id));
    }

    [TestMethod]
    public async Task StartAsync_WithOtherPlayers_HostBecomesObserver()
    {
        var (state, host, players) = await CreateStateWithPlayersAsync(1);
        state.TotalRounds = 1;
        state.CustomWordPool = new List<string> { "apple" };

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
        state.CustomWordPool = new List<string> { "apple" };
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "apple");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.TryGetFailure(out var failure));
        StringAssert.Contains(failure.PublicMessage, "observing");
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
        state.CustomWordPool = new List<string> { "apple" };
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var result = _engine.SubmitGuess(state, host, "apple");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(GamePhase.RoundResults, state.Phase);
        var outcome = state.RoundHistory[0].Outcomes.Single(o => o.UserId == host.Id);
        Assert.AreEqual(1, outcome.Placement);
        Assert.AreEqual(10, outcome.PointsAwarded);
    }

    [TestMethod]
    public async Task BuildOutcomes_ObserverMode_ExcludesHost()
    {
        var (state, host, players) = await CreateStateWithPlayersAsync(1);
        state.TotalRounds = 1;
        state.TransitionDuration = TimeSpan.FromMilliseconds(80);
        state.RoundTimer = TimeSpan.FromSeconds(30);
        state.WinCondition = WinConditionMode.Sprinter;
        state.CustomWordPool = new List<string> { "apple" };
        state.WordOrderMode = WordOrderMode.ListOrder;

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);

        var submitResult = _engine.SubmitGuess(state, players[0], "apple");
        Assert.IsTrue(submitResult.IsSuccess);

        Assert.AreEqual(1, state.RoundHistory[0].Outcomes.Count);
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
        state.CustomWordPool = new List<string> { "apple", "brave" };
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
        state.CustomWordPool = new List<string> { "apple" };

        await _engine.StartAsync(host, state);
        await WaitForPhaseAsync(state, GamePhase.Playing, timeoutMs: 1500);
        await WaitForPhaseAsync(state, GamePhase.RoundResults, timeoutMs: 2000);

        Assert.AreEqual(1, state.RoundHistory[0].Outcomes.Count);
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
        state.CustomWordPool = new List<string> { "alpha", "betas", "gamma" };
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
        var host = new User("Host", Guid.NewGuid().ToString());
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
            var player = new User($"P{i + 1}", Guid.NewGuid().ToString());
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
}
