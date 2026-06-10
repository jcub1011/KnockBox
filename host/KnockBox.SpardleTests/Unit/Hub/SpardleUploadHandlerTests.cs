using System.Text;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle;
using KnockBox.Spardle.Contracts;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.SpardleTests.Unit.Hub;

/// <summary>
/// The file-upload surface (<see cref="IGameUploadHandler"/>): the host streams a CSV/typed word pool
/// to the generic <c>/api/games/upload</c> endpoint, which hands the engine the body stream. Like the
/// hub commands, host-gating compares by <c>User.Id</c> (a fresh User per request).
/// </summary>
[TestClass]
public class SpardleUploadHandlerTests
{
    private SpardleEngine _engine = default!;
    private IGameUploadHandler Upload => _engine;

    private static User Fresh(Guid id) => UserFactory.Create("reconnected", id);
    private static Stream Body(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [TestInitialize]
    public void Setup()
    {
        _engine = new SpardleEngine(
            new WordListService(NullLogger<WordListService>.Instance),
            new SequentialRng(),
            new NullLoggerFactory());
    }

    [TestMethod]
    public async Task HandleUpload_HostWordPool_PopulatesCustomPool()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host);

        var result = await Upload.HandleUploadAsync(
            Fresh(host.Id), state, SpardleCommands.WordPoolUploadKind, "words.csv",
            Body("Apple\nBREAD, cherry\n\n123notletters\n"));

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { "apple", "bread", "cherry" }, state.CustomWordPool.ToArray(),
            "Words are lowercased, comma/newline split, letters-only, de-duplicated in order.");
    }

    [TestMethod]
    public async Task HandleUpload_NonHost_IsRejected()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host);

        var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
        var result = await Upload.HandleUploadAsync(
            stranger, state, SpardleCommands.WordPoolUploadKind, "words.csv", Body("apple\nbread"));

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(0, state.CustomWordPool.Count);
    }

    [TestMethod]
    public async Task HandleUpload_UnknownKind_IsRejected()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host);

        var result = await Upload.HandleUploadAsync(
            Fresh(host.Id), state, "avatar", "pic.png", Body("apple"));

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(result.TryGetFailure(out var err));
        StringAssert.Contains(err.PublicMessage, "Unknown upload kind");
    }

    [TestMethod]
    public async Task HandleUpload_EmptyOrInvalidContent_IsRejected()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = await LobbyAsync(host);

        var result = await Upload.HandleUploadAsync(
            Fresh(host.Id), state, SpardleCommands.WordPoolUploadKind, "words.csv", Body("123\n!!!\n"));

        Assert.IsTrue(result.IsFailure, "A file with no valid words must be rejected.");
        Assert.AreEqual(0, state.CustomWordPool.Count);
    }

    private async Task<SpardleState> LobbyAsync(User host)
    {
        var created = await _engine.CreateStateAsync(host);
        return (SpardleState)created.Value!;
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
