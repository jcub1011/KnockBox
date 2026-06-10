using System.Collections.Immutable;
using System.Text.Json;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle;
using KnockBox.Spardle.Contracts;
using KnockBox.Spardle.Models;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.SpardleTests.Unit.Hub;

/// <summary>
/// The hub command surface (<see cref="IGameCommandHandler"/>): each command maps to the engine
/// method a Razor page used to call directly. The hub resolves a FRESH <see cref="User"/> per command
/// from the connection token, so host-gated commands must compare by <c>User.Id</c>, never by
/// reference — these tests pass a different User instance carrying the host's id to guard that footgun.
/// </summary>
[TestClass]
public class SpardleHubCommandTests
{
    private SpardleEngine _engine = default!;
    private IGameCommandHandler Hub => _engine;

    private static User Fresh(Guid id) => UserFactory.Create("reconnected", id);

    [TestInitialize]
    public void Setup()
    {
        _engine = new SpardleEngine(
            new WordListService(NullLogger<WordListService>.Instance),
            new SequentialRng(),
            new NullLoggerFactory());
    }

    [TestMethod]
    public async Task SubmitGuess_Command_AdvancesOwnBoard()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var (state, players) = await PlayingAsync(host, count: 2, target: "bread", "stair");

        var result = await Hub.HandleCommandAsync(Fresh(players[0].Id), state, SpardleCommands.SubmitGuess,
            JsonSerializer.Serialize(new SubmitGuessPayload("stair"), SpardleContractsJsonContext.Default.SubmitGuessPayload));

        Assert.IsTrue(result.IsSuccess);
        var view = Project(state, players[0].Id);
        Assert.AreEqual(1, view.MyBoard!.Guesses.Count);
        Assert.AreEqual("stair", view.MyBoard.Guesses[0].Word);
    }

    [TestMethod]
    public async Task GiveUp_Command_MarksDnf()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var (state, players) = await PlayingAsync(host, count: 2, target: "bread");

        var result = await Hub.HandleCommandAsync(Fresh(players[0].Id), state, SpardleCommands.GiveUp, null);

        Assert.IsTrue(result.IsSuccess);
        var view = Project(state, players[0].Id);
        Assert.IsTrue(view.MyBoard!.Dnf);
        Assert.IsTrue(view.MyBoard.HasFinishedRound);
    }

    [TestMethod]
    public async Task Start_Command_IsHostGated_AndBeginsMatch()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host, 2);
        var payload = JsonSerializer.Serialize(new StartPayload(false), SpardleContractsJsonContext.Default.StartPayload);

        var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
        Assert.IsTrue((await Hub.HandleCommandAsync(stranger, state, SpardleCommands.Start, payload)).IsFailure);
        Assert.AreEqual(GamePhase.Lobby, state.Phase, "A non-host start must not begin the match.");

        Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, SpardleCommands.Start, payload)).IsSuccess);
        Assert.AreEqual(GamePhase.RoundIntro, state.Phase);
        Assert.IsFalse(state.IsJoinable);
        Assert.IsFalse(state.HostIsParticipant, "HostPlaysAlong=false with other players → host observes.");
    }

    [TestMethod]
    public async Task Start_Command_HostPlaysVariant_SeatsHost()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host, 1);
        var payload = JsonSerializer.Serialize(new StartPayload(true), SpardleContractsJsonContext.Default.StartPayload);

        Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, SpardleCommands.Start, payload)).IsSuccess);
        Assert.IsTrue(state.HostIsParticipant, "A host who plays along must be seated as a participant.");
        Assert.IsTrue(state.TryGetPlayerState(host.Id, out _));
    }

    [TestMethod]
    public async Task UpdateSettings_PreservesServerOnlyHostPlaysAlong_AndIsHostGated()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host, 2);
        state.UpdateSettings(s => s with { HostPlaysAlong = true });

        var view = new SpardleSettingsView { TotalRounds = 7 };
        var payload = JsonSerializer.Serialize(view, SpardleContractsJsonContext.Default.SpardleSettingsView);

        var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
        Assert.IsTrue((await Hub.HandleCommandAsync(stranger, state, SpardleCommands.UpdateSettings, payload)).IsFailure);

        Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, SpardleCommands.UpdateSettings, payload)).IsSuccess);
        Assert.AreEqual(7, state.Settings.TotalRounds);
        Assert.IsTrue(state.Settings.HostPlaysAlong, "The settings view omits HostPlaysAlong, so applying it must preserve the server value.");
    }

    [TestMethod]
    public async Task UpdateSettings_SwitchingToLibrarySource_ClearsCustomPool()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host, 2);
        state.Execute(() => state.CustomWordPool = ImmutableList.Create("alpha", "bravo"));

        var view = new SpardleSettingsView { WordPoolMode = SpardleWordSource.NytStandard };
        var payload = JsonSerializer.Serialize(view, SpardleContractsJsonContext.Default.SpardleSettingsView);

        Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, SpardleCommands.UpdateSettings, payload)).IsSuccess);
        Assert.AreEqual(0, state.CustomWordPool.Count,
            "Switching to a library word source must clear a leftover custom pool so it can't override it.");
    }

    [TestMethod]
    public async Task KickPlayer_Command_RemovesPlayer()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host, 2);
        var victim = state.Players[0].User;

        var result = await Hub.HandleCommandAsync(Fresh(host.Id), state, SpardleCommands.KickPlayer,
            JsonSerializer.Serialize(new KickPlayerPayload(victim.Id), SpardleContractsJsonContext.Default.KickPlayerPayload));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(state.Players.Any(p => p.User.Id == victim.Id));
    }

    [TestMethod]
    public async Task ReturnToLobby_HostGated_ReopensLobbyFromGameOver()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var (state, _) = await PlayingAsync(host, count: 2, target: "bread");
        state.Execute(() => state.Phase = GamePhase.GameOver);

        var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
        Assert.IsTrue((await Hub.HandleCommandAsync(stranger, state, SpardleCommands.ReturnToLobby, null)).IsFailure);

        Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, SpardleCommands.ReturnToLobby, null)).IsSuccess);
        Assert.IsTrue(state.IsJoinable);
        Assert.AreEqual(GamePhase.Lobby, state.Phase);
    }

    [TestMethod]
    public async Task UnknownCommand_ReturnsError()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host, 2);

        var result = await Hub.HandleCommandAsync(host, state, "no-such-command", null);

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(result.TryGetFailure(out var err));
        StringAssert.Contains(err.PublicMessage, "Unknown command");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private SpardleView Project(SpardleState state, Guid recipientId)
        => (SpardleView)((IGameStateProjector)_engine).ProjectFor(state, recipientId)!;

    private async Task<SpardleState> LobbyAsync(User host, int count)
    {
        var created = await _engine.CreateStateAsync(host);
        var state = (SpardleState)created.Value!;
        for (int i = 0; i < count; i++)
            Assert.IsTrue(state.RegisterPlayer(UserFactory.Create($"P{i}", Guid.NewGuid())).IsSuccess);
        return state;
    }

    private async Task<(SpardleState state, List<User> players)> PlayingAsync(
        User host, int count, string target, params string[] poolExtras)
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

        Assert.IsTrue((await _engine.StartAsync(host, state)).IsSuccess);

        var pool = new List<string> { target };
        pool.AddRange(poolExtras);
        state.Execute(() =>
        {
            // CustomWordPool seeds the lookup so SubmitGuess validation passes without the dictionary.
            state.CustomWordPool = pool.ToImmutableList();
            state.TargetWord = target;
            state.Phase = GamePhase.Playing;
            state.CurrentRound = 1;
            state.IsRoundActive = true;
            state.RoundStartTime = DateTime.UtcNow;
        });
        return (state, players);
    }

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
