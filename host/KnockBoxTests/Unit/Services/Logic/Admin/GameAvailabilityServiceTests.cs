using KnockBox.Admin;
using KnockBox.Services.Logic.Games.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KnockBox.Tests.Unit.Services.Logic.Admin;

[TestClass]
public sealed class GameAvailabilityServiceTests
{
    private string _tempRoot = null!;
    private string _statePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "knockbox-availability-" + Guid.NewGuid().ToString("N"));
        _statePath = Path.Combine(_tempRoot, "games-state.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [TestMethod]
    public void UnknownRoute_DefaultsToEnabled()
    {
        var svc = CreateService();

        Assert.IsTrue(svc.IsEnabled("card-counter"));
        Assert.IsTrue(svc.IsEnabled("SOMETHING-ELSE"));
    }

    [TestMethod]
    public async Task SetEnabled_False_PersistsToDisk_AndReloadsDisabled()
    {
        var svc1 = CreateService();
        await svc1.SetEnabledAsync("card-counter", false);
        Assert.IsFalse(svc1.IsEnabled("card-counter"));

        // Fresh instance reads the file back.
        var svc2 = CreateService();
        Assert.IsFalse(svc2.IsEnabled("card-counter"));
        Assert.IsTrue(svc2.IsEnabled("dice-simulator"));
    }

    [TestMethod]
    public async Task SetEnabled_TrueAgain_RemovesFromPersistedList()
    {
        var svc = CreateService();
        await svc.SetEnabledAsync("card-counter", false);
        await svc.SetEnabledAsync("card-counter", true);

        Assert.IsTrue(svc.IsEnabled("card-counter"));

        // And on disk, the disabled list should no longer contain it.
        var json = File.ReadAllText(_statePath);
        Assert.IsFalse(json.Contains("card-counter", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task IsEnabled_CaseInsensitive()
    {
        var svc = CreateService();
        await svc.SetEnabledAsync("Card-Counter", false);
        Assert.IsFalse(svc.IsEnabled("card-counter"));
        Assert.IsFalse(svc.IsEnabled("CARD-COUNTER"));
    }

    [TestMethod]
    public async Task Changed_FiresOnToggle()
    {
        var svc = CreateService();
        var fired = 0;
        svc.Changed += () => fired++;

        await svc.SetEnabledAsync("card-counter", false);
        await svc.SetEnabledAsync("card-counter", true);

        Assert.AreEqual(2, fired);
    }

    [TestMethod]
    public void CorruptedFile_IsTolerated()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(_statePath, "{ not valid json");

        // Should not throw; should load as if nothing is disabled.
        var svc = CreateService();
        Assert.IsTrue(svc.IsEnabled("card-counter"));
    }

    [TestMethod]
    public void EmptyDisabledArray_IsTolerated()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(new { disabled = Array.Empty<string>() }));

        var svc = CreateService();
        Assert.IsTrue(svc.IsEnabled("card-counter"));
        Assert.AreEqual(0, svc.GetAll().Count);
    }

    [TestMethod]
    public async Task SetEnabled_EmptyOrNullRoute_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await svc.SetEnabledAsync("", false));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await svc.SetEnabledAsync("   ", false));
    }

    private IGameAvailabilityService CreateService()
    {
        var options = Options.Create(new AdminOptions { GameStatePath = _statePath });
        return new GameAvailabilityService(options, NullLogger<GameAvailabilityService>.Instance);
    }
}
