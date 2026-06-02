using System.Text.Json;
using KnockBox.Core.Services.State.PlayLog;
using KnockBox.Core.Services.Storage.ClientStorage;
using KnockBox.Services.State.PlayLog;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.PlatformTests.Unit.Services.State.PlayLog;

/// <summary>
/// Verifies the play-log persistence contract: appends are newest-first and
/// time-stamped, the history is capped at <see cref="PlayLogService.MaxEntries"/>,
/// reads filter by game, clear empties the log, and string metadata survives the
/// JSON round-trip the browser storage performs.
/// </summary>
[TestClass]
public sealed class PlayLogServiceTests
{
    [TestMethod]
    public async Task StoreLogAsync_StampsPlayedAt_AndPrependsNewestFirst()
    {
        var service = NewService(out _);

        await service.StoreLogAsync(GameLog.Create("card-counter"));
        await service.StoreLogAsync(GameLog.Create("codeword"));

        var logs = await service.GetLogsAsync();

        Assert.AreEqual(2, logs.Count);
        Assert.AreEqual("codeword", logs[0].GameIdentifier, "Most recent store must be first.");
        Assert.AreEqual("card-counter", logs[1].GameIdentifier);
        Assert.AreNotEqual(default, logs[0].PlayedAt, "PlayedAt must be stamped by the service.");
    }

    [TestMethod]
    public async Task StoreLogAsync_CapsAtFifty_DroppingOldest()
    {
        var service = NewService(out _);

        for (int i = 0; i < PlayLogService.MaxEntries + 10; i++)
            await service.StoreLogAsync(GameLog.Create($"game-{i}"));

        var logs = await service.GetLogsAsync();

        Assert.AreEqual(PlayLogService.MaxEntries, logs.Count);
        // Newest retained, oldest (game-0..game-9) dropped.
        Assert.AreEqual($"game-{PlayLogService.MaxEntries + 9}", logs[0].GameIdentifier);
        Assert.IsFalse(logs.Any(l => l.GameIdentifier == "game-0"));
    }

    [TestMethod]
    public async Task GetLogsAsync_ReturnsEmpty_WhenNothingStored()
    {
        var service = NewService(out _);

        var logs = await service.GetLogsAsync();

        Assert.AreEqual(0, logs.Count);
    }

    [TestMethod]
    public async Task GetLogsAsync_ByGameId_FiltersToOneGame()
    {
        var service = NewService(out _);
        await service.StoreLogAsync(GameLog.Create("card-counter"));
        await service.StoreLogAsync(GameLog.Create("codeword"));
        await service.StoreLogAsync(GameLog.Create("card-counter"));

        var logs = await service.GetLogsAsync("card-counter");

        Assert.AreEqual(2, logs.Count);
        Assert.IsTrue(logs.All(l => l.GameIdentifier == "card-counter"));
    }

    [TestMethod]
    public async Task ClearAsync_EmptiesLog()
    {
        var service = NewService(out _);
        await service.StoreLogAsync(GameLog.Create("card-counter"));

        await service.ClearAsync();

        var logs = await service.GetLogsAsync();
        Assert.AreEqual(0, logs.Count);
    }

    [TestMethod]
    public async Task StoreLogAsync_RoundTripsStringMetadata()
    {
        var service = NewService(out _);
        var metadata = new Dictionary<string, string>
        {
            ["place"] = "3",
            ["duration"] = "00:12:45",
        };

        await service.StoreLogAsync(GameLog.Create("card-counter", metadata));

        var stored = (await service.GetLogsAsync()).Single();
        Assert.AreEqual("3", stored.GetMetadata("place"));
        Assert.AreEqual("00:12:45", stored.GetMetadata("duration"));
        Assert.IsNull(stored.GetMetadata("missing"));
    }

    [TestMethod]
    public async Task GetLogsAsync_SelfHeals_OnCorruptStoredJson()
    {
        var service = NewService(out var storage);
        storage.SeedRaw("play-log", "history", "{ not a valid json array ]");

        var logs = await service.GetLogsAsync();

        Assert.AreEqual(0, logs.Count, "Corrupt history must read as empty.");

        // Self-heal: the corrupt key was cleared, so a subsequent append works cleanly.
        await service.StoreLogAsync(GameLog.Create("codeword"));
        var after = await service.GetLogsAsync();
        Assert.AreEqual(1, after.Count);
        Assert.AreEqual("codeword", after[0].GameIdentifier);
    }

    [TestMethod]
    public async Task GetLogsAsync_TrimsToCap_WhenStorageHoldsMore()
    {
        var service = NewService(out var storage);
        var oversized = Enumerable.Range(0, PlayLogService.MaxEntries + 15)
            .Select(i => GameLog.Create($"game-{i}"))
            .ToList();
        await storage.SetAsync("play-log", "history", oversized);

        var logs = await service.GetLogsAsync();

        Assert.AreEqual(PlayLogService.MaxEntries, logs.Count, "Reads must honor the cap even if storage holds more.");
    }

    [TestMethod]
    public async Task StoreLogAsync_ConcurrentAppends_DoNotLoseEntries()
    {
        var service = NewService(out _);
        const int count = 25;

        await Task.WhenAll(Enumerable.Range(0, count)
            .Select(i => service.StoreLogAsync(GameLog.Create($"game-{i}")).AsTask()));

        var logs = await service.GetLogsAsync();
        Assert.AreEqual(count, logs.Count, "The write lock must prevent lost appends under concurrency.");
        for (int i = 0; i < count; i++)
            Assert.IsTrue(logs.Any(l => l.GameIdentifier == $"game-{i}"), $"game-{i} was lost.");
    }

    [TestMethod]
    public async Task GetLogsAsync_ReturnsEmpty_WhenCanceled()
    {
        var service = NewService(out _);
        await service.StoreLogAsync(GameLog.Create("codeword"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var logs = await service.GetLogsAsync(cts.Token);

        Assert.AreEqual(0, logs.Count, "A canceled read must return empty, not throw.");
    }

    [TestMethod]
    public async Task StoreLogAsync_DropsSilently_WhenCanceled()
    {
        var service = NewService(out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await service.StoreLogAsync(GameLog.Create("codeword"), cts.Token); // must not throw

        var logs = await service.GetLogsAsync();
        Assert.AreEqual(0, logs.Count, "A canceled store must not persist.");
    }

    [TestMethod]
    public async Task StoreLogAsync_Throws_OnNullLog()
    {
        var service = NewService(out _);

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.StoreLogAsync(null!).AsTask());
    }

    // ─── fixtures ───────────────────────────────────────────────────────────

    private static PlayLogService NewService(out JsonLocalStorage storage)
    {
        storage = new JsonLocalStorage();
        return new PlayLogService(storage, NullLogger<PlayLogService>.Instance);
    }

    /// <summary>
    /// In-memory <see cref="ILocalStorageService"/> that serializes values to
    /// JSON on set and deserializes on get — faithfully reproducing the
    /// round-trip the real browser-storage service performs, so tests catch
    /// serialization regressions (e.g. a non-round-trippable metadata type).
    /// </summary>
    private sealed class JsonLocalStorage : ILocalStorageService
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<(string, string), string> _store = new();

        /// <summary>Writes raw text under a key, bypassing serialization — to simulate
        /// corrupt/legacy JSON already sitting in browser storage.</summary>
        public void SeedRaw(string scope, string key, string rawJson) => _store[(scope, key)] = rawJson;

        public ValueTask<TType> GetAsync<TType>(string scope, string key, CancellationToken ct = default)
        {
            // The real (JS-interop) storage observes cancellation; mirror that so the service's
            // cancellation handling is exercised.
            ct.ThrowIfCancellationRequested();
            return new(_store.TryGetValue((scope, key), out var json)
                ? JsonSerializer.Deserialize<TType>(json, Options)!
                : default!);
        }

        public ValueTask SetAsync<TType>(string scope, string key, TType value, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _store[(scope, key)] = JsonSerializer.Serialize(value, Options);
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string scope, string key)
        {
            _store.Remove((scope, key));
            return ValueTask.CompletedTask;
        }

        public ValueTask<List<string>> GetKeysAsync(string scope, CancellationToken ct = default) => new([]);
        public ValueTask<List<string>> GetAllKeysAsync(CancellationToken ct = default) => new([]);
        public ValueTask RemoveAsync(string scope) { _store.Clear(); return ValueTask.CompletedTask; }
        public ValueTask ClearAsync() { _store.Clear(); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
