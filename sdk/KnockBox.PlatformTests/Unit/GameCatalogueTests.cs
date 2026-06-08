using System.Text.Json;
using KnockBox.Core.Plugins;
using KnockBox.Platform;
using KnockBox.Platform.Games;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage for the <c>GET /api/games</c> catalogue: the testable
/// <see cref="GameCatalogueProjection"/> (gate + sort + HasClientUi mapping) and
/// the <c>HandleGamesCatalogue</c> endpoint body (JSON shape + content type).
/// </summary>
[TestClass]
public sealed class GameCatalogueProjectionTests
{
    [TestMethod]
    public void Build_ExcludesDisabledGames()
    {
        var modules = new[] { Module("alpha", "Alpha"), Module("beta", "Beta") };

        var result = GameCatalogueProjection.Build(modules, new FakeAvailability(disabled: "beta"));

        Assert.HasCount(1, result);
        Assert.AreEqual("alpha", result[0].RouteIdentifier);
    }

    [TestMethod]
    public void Build_IncludesEnabledGames()
    {
        var modules = new[] { Module("alpha", "Alpha"), Module("beta", "Beta") };

        var result = GameCatalogueProjection.Build(modules, new FakeAvailability());

        Assert.HasCount(2, result);
    }

    [TestMethod]
    public void Build_SortsByNameAscending()
    {
        var modules = new[] { Module("z", "Zebra"), Module("a", "Apple"), Module("m", "Mango") };

        var result = GameCatalogueProjection.Build(modules, new FakeAvailability());

        CollectionAssert.AreEqual(
            new[] { "Apple", "Mango", "Zebra" },
            result.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void Build_HasClientUi_TrueWhenClientAssemblyAndAssetsSet()
    {
        var modules = new[] { Module("a", "A", clientAssembly: "A.Client", clientAssets: 1) };

        var result = GameCatalogueProjection.Build(modules, new FakeAvailability());

        Assert.IsTrue(result[0].HasClientUi);
    }

    [TestMethod]
    public void Build_HasClientUi_FalseWhenClientAssemblyNull()
    {
        var modules = new[] { Module("a", "A", clientAssembly: null, clientAssets: 1) };

        var result = GameCatalogueProjection.Build(modules, new FakeAvailability());

        Assert.IsFalse(result[0].HasClientUi);
    }

    [TestMethod]
    public void Build_HasClientUi_FalseWhenAssetsEmpty()
    {
        var modules = new[] { Module("a", "A", clientAssembly: "A.Client", clientAssets: 0) };

        var result = GameCatalogueProjection.Build(modules, new FakeAvailability());

        Assert.IsFalse(result[0].HasClientUi);
    }

    [TestMethod]
    public void Build_MapsAllScalarFields()
    {
        var modules = new[] { Module("route-x", "Game X", wip: true, tile: "x.svg") };

        var entry = GameCatalogueProjection.Build(modules, new FakeAvailability())[0];

        Assert.AreEqual("Game X", entry.Name);
        Assert.AreEqual("Game X desc", entry.Description);
        Assert.AreEqual("route-x", entry.RouteIdentifier);
        Assert.AreEqual("Asm.route-x", entry.EntryAssembly);
        Assert.AreEqual("x.svg", entry.TileAsset);
        Assert.IsTrue(entry.WorkInProgress);
    }

    [TestMethod]
    public void Build_TileAssetNull_PassesThrough()
    {
        var modules = new[] { Module("a", "A", tile: null) };

        var result = GameCatalogueProjection.Build(modules, new FakeAvailability());

        Assert.IsNull(result[0].TileAsset);
    }

    internal static IGameModule Module(
        string route,
        string name,
        bool wip = false,
        string? tile = "tile.svg",
        string? clientAssembly = null,
        int clientAssets = 0)
    {
        var assets = Enumerable.Range(0, clientAssets)
            .Select(i => new ClientAssetEntry($"Asm.{route}.Client{i}", new string('a', 64)))
            .ToArray();

        var manifest = new PluginManifest(
            Name: name,
            Description: name + " desc",
            RouteIdentifier: route,
            Version: new Version(1, 0, 0),
            EntryAssembly: "Asm." + route,
            Capabilities: new HashSet<PluginCapability>(),
            TileAsset: tile,
            WorkInProgress: wip)
        {
            ClientAssembly = clientAssembly,
            ClientAssets = assets,
        };

        var mock = new Mock<IGameModule>();
        mock.SetupGet(m => m.Manifest).Returns(manifest);
        return mock.Object;
    }

    internal sealed class FakeAvailability(params string[] disabled) : IGameAvailabilityService
    {
        private readonly HashSet<string> _disabled = new(disabled, StringComparer.OrdinalIgnoreCase);

        public bool IsEnabled(string routeIdentifier) => !_disabled.Contains(routeIdentifier);
        public Task SetEnabledAsync(string routeIdentifier, bool enabled) => Task.CompletedTask;
        public IReadOnlyDictionary<string, bool> GetAll() => new Dictionary<string, bool>();
        public event Action? Changed { add { } remove { } }
    }
}

[TestClass]
public sealed class GameCatalogueEndpointTests
{
    [TestMethod]
    public async Task GamesCatalogue_ReturnsJsonArray_OfEnabledGames()
    {
        var modules = new[]
        {
            GameCatalogueProjectionTests.Module("alpha", "Alpha"),
            GameCatalogueProjectionTests.Module("beta", "Beta"),
        };

        var (status, _, entries) = await ExecuteAsync(modules, new GameCatalogueProjectionTests.FakeAvailability());

        Assert.AreEqual(StatusCodes.Status200OK, status);
        Assert.HasCount(2, entries);
    }

    [TestMethod]
    public async Task GamesCatalogue_HonorsAvailabilityGate()
    {
        var modules = new[]
        {
            GameCatalogueProjectionTests.Module("alpha", "Alpha"),
            GameCatalogueProjectionTests.Module("beta", "Beta"),
        };

        var (_, _, entries) = await ExecuteAsync(
            modules, new GameCatalogueProjectionTests.FakeAvailability(disabled: "alpha"));

        Assert.HasCount(1, entries);
        Assert.AreEqual("beta", entries[0].RouteIdentifier);
    }

    [TestMethod]
    public async Task GamesCatalogue_SerializesHasClientUiFlag()
    {
        var modules = new[]
        {
            GameCatalogueProjectionTests.Module("server-only", "Server Only"),
            GameCatalogueProjectionTests.Module("wasm-ready", "Wasm Ready", clientAssembly: "W.Client", clientAssets: 1),
        };

        var (_, _, entries) = await ExecuteAsync(modules, new GameCatalogueProjectionTests.FakeAvailability());

        Assert.IsFalse(entries.Single(e => e.RouteIdentifier == "server-only").HasClientUi);
        Assert.IsTrue(entries.Single(e => e.RouteIdentifier == "wasm-ready").HasClientUi);
    }

    [TestMethod]
    public async Task GamesCatalogue_ContentTypeIsJson()
    {
        var modules = new[] { GameCatalogueProjectionTests.Module("alpha", "Alpha") };

        var (_, contentType, _) = await ExecuteAsync(modules, new GameCatalogueProjectionTests.FakeAvailability());

        StringAssert.Contains(contentType ?? "", "application/json");
    }

    private static async Task<(int status, string? contentType, GameCatalogueEntry[] entries)> ExecuteAsync(
        IEnumerable<IGameModule> modules,
        IGameAvailabilityService availability)
    {
        var result = KnockBoxPlatformExtensions.HandleGamesCatalogue(modules, availability);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var body = new MemoryStream();
        ctx.Response.Body = body;

        await result.ExecuteAsync(ctx);

        body.Position = 0;
        using var reader = new StreamReader(body);
        var json = await reader.ReadToEndAsync();
        var entries = JsonSerializer.Deserialize<GameCatalogueEntry[]>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        return (ctx.Response.StatusCode, ctx.Response.ContentType, entries);
    }
}
