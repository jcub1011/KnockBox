using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle;
using KnockBox.Spardle.Contracts;
using KnockBox.Spardle.Models;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.SpardleTests.Unit.Projection;

/// <summary>
/// Projection security + serialization. The competitive secrets are the target word and every rival
/// player's guesses (their letters reveal solved positions). A competing player must receive only
/// their own board (MyBoard); rivals come through as count-only <see cref="RivalView"/>s. The
/// display-only host-observer sees every board. The answer is withheld until a RevealAnswer results
/// projection. The view must also round-trip through the hub's reflection serializer and the client's
/// source-gen context (the real WASM path).
/// </summary>
[TestClass]
public class SpardleStateProjectorTests
{
    private SpardleEngine _engine = default!;

    private static readonly JsonSerializerOptions WireOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [TestInitialize]
    public void Setup()
    {
        _engine = new SpardleEngine(
            new WordListService(NullLogger<WordListService>.Instance),
            new SequentialRng(),
            new NullLoggerFactory());
    }

    [TestMethod]
    public async Task ProjectFor_Playing_CompetingPlayer_SeesOwnBoard_RivalsAreCountsOnly()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var (state, players) = await StartPlayingAsync(host, count: 2, hostPlays: false, target: "bread");

        // players[0] guessed STAIR (own); players[1] (a rival) guessed FJORD.
        AddGuess(state, players[0].Id, "bread", "stair");
        AddGuess(state, players[1].Id, "bread", "fjord");

        var view = Project(state, players[0].Id);

        Assert.IsNotNull(view.MyBoard, "A competitor must see their own board.");
        Assert.AreEqual(1, view.MyBoard!.Guesses.Count);
        Assert.AreEqual("stair", view.MyBoard.Guesses[0].Word);
        Assert.AreEqual(0, view.AllBoards.Count, "A competitor must not receive every board.");

        var rival = view.Rivals.SingleOrDefault(r => r.UserId == players[1].Id);
        Assert.IsNotNull(rival, "Rivals surface as count-only entries.");
        Assert.AreEqual(1, rival!.GuessCount);

        // The leak boundary: the rival's guessed letters and the target must NOT be on the wire.
        var json = JsonSerializer.Serialize(view, view.GetType(), WireOptions);
        StringAssert.Contains(json, "stair", "The recipient's own guess should be present.");
        Assert.IsFalse(json.Contains("fjord", StringComparison.OrdinalIgnoreCase),
            "A rival's guess letters must never be projected to a competitor.");
        Assert.IsFalse(json.Contains("bread", StringComparison.OrdinalIgnoreCase),
            "The secret target word must not be projected during play.");
        Assert.IsNull(view.Answer, "The answer is withheld during play.");
    }

    [TestMethod]
    public async Task ProjectFor_Playing_HostObserver_SeesEveryBoard()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var (state, players) = await StartPlayingAsync(host, count: 2, hostPlays: false, target: "bread");
        AddGuess(state, players[0].Id, "bread", "stair");

        var view = Project(state, host.Id);

        Assert.IsTrue(view.IsHostObserver);
        Assert.IsNull(view.MyBoard);
        Assert.AreEqual(2, view.AllBoards.Count, "The host-observer sees every player's board.");
        Assert.AreEqual(0, view.Rivals.Count);
    }

    [TestMethod]
    public async Task ProjectFor_RoundResults_RevealsAnswerOnlyWhenEnabled()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var (state, players) = await StartPlayingAsync(host, count: 2, hostPlays: false, target: "bread");
        EnterRoundResults(state, players, answer: "bread");

        var revealed = Project(state, players[0].Id);
        Assert.AreEqual("bread", revealed.Answer, "RevealAnswer (on) must surface the answer in results.");
        Assert.IsNotNull(revealed.LastRoundResult);
        Assert.AreEqual("bread", revealed.LastRoundResult!.Answer);

        state.UpdateSettings(s => s with { RevealAnswer = false });
        var hidden = Project(state, players[0].Id);
        Assert.IsNull(hidden.Answer, "RevealAnswer (off) must keep the answer hidden even in results.");
        Assert.IsNull(hidden.LastRoundResult!.Answer);
    }

    [TestMethod]
    public async Task ProjectFor_RoundTripsThroughHubAndSourceGen()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var (state, players) = await StartPlayingAsync(host, count: 2, hostPlays: false, target: "bread");
        AddGuess(state, players[0].Id, "bread", "stair");

        var view = Project(state, players[0].Id);

        var json = JsonSerializer.Serialize(view, view.GetType(), WireOptions);
        var roundTripped = JsonSerializer.Deserialize(json, SpardleContractsJsonContext.Default.SpardleView);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(GamePhase.Playing, roundTripped!.Phase);
        Assert.AreEqual(WinConditionMode.Sprinter, roundTripped.Settings.WinCondition);
        Assert.AreEqual(5, roundTripped.WordLength);
        Assert.IsNotNull(roundTripped.MyBoard);
        Assert.AreEqual("stair", roundTripped.MyBoard!.Guesses[0].Word);
        // The per-letter LetterStatus[] must survive the string-enum round-trip exactly.
        CollectionAssert.AreEqual(
            view.MyBoard!.Guesses[0].Statuses,
            roundTripped.MyBoard.Guesses[0].Statuses,
            "The LetterStatus[] must round-trip through hub-write → source-gen-read.");
    }

    [TestMethod]
    public async Task ProjectFor_GameOver_CarriesStandings()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var (state, players) = await StartPlayingAsync(host, count: 2, hostPlays: false, target: "bread");
        state.Execute(() => state.Phase = GamePhase.GameOver);

        var view = Project(state, host.Id);

        Assert.AreEqual(GamePhase.GameOver, view.Phase);
        Assert.AreEqual(2, view.Standings.Count, "Every match participant should appear in the standings.");
        CollectionAssert.AreEquivalent(
            players.Select(p => p.Id).ToList(),
            view.Standings.Select(s => s.UserId).ToList());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private SpardleView Project(SpardleState state, Guid recipientId)
        => (SpardleView)((IGameStateProjector)_engine).ProjectFor(state, recipientId)!;

    private async Task<(SpardleState state, List<User> players)> StartPlayingAsync(
        User host, int count, bool hostPlays, string target)
    {
        var created = await _engine.CreateStateAsync(host);
        var state = (SpardleState)created.Value!;

        var players = new List<User>();
        for (int i = 0; i < count; i++)
        {
            var u = UserFactory.Create($"P{i}", Guid.NewGuid());
            players.Add(u);
            Assert.IsTrue(state.RegisterPlayer(u).IsSuccess);
        }

        state.UpdateSettings(s => s with { HostPlaysAlong = hostPlays, RevealAnswer = true });
        Assert.IsTrue((await _engine.StartAsync(host, state)).IsSuccess);

        // The engine's RoundIntro→Playing transition fires on a ScheduleCallback timer; drive it
        // synchronously here so the projector can be tested deterministically.
        state.Execute(() =>
        {
            state.TargetWord = target;
            state.Phase = GamePhase.Playing;
            state.CurrentRound = 1;
            state.IsRoundActive = true;
            state.RoundStartTime = DateTime.UtcNow;
        });

        return (state, players);
    }

    private static void AddGuess(SpardleState state, Guid playerId, string target, string guess)
        => state.Execute(() =>
        {
            var ps = state.CreatePlayerState(playerId);
            ps.Guesses = ps.Guesses.Add(SpardleEngine.EvaluateGuess(target, guess));
        });

    private static void EnterRoundResults(SpardleState state, List<User> players, string answer)
        => state.Execute(() =>
        {
            state.Phase = GamePhase.RoundResults;
            state.IsRoundActive = false;
            state.LastCompletedAnswer = answer;
            state.RoundHistory = state.RoundHistory.Add(new RoundResult
            {
                RoundNumber = 1,
                Answer = answer,
                Outcomes = players.Select((p, i) => new PlayerRoundOutcome
                {
                    UserId = p.Id,
                    DisplayName = $"P{i}",
                    GuessCount = 1,
                    Dnf = false,
                    PointsAwarded = 10 - i,
                    Placement = i + 1,
                }).ToList()
            });
        });

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
        public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast) => new byte[length];
    }
}
