using KnockBox.Admin;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Logic.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;

namespace KnockBox.Tests.Unit.Services.Logic.Admin;

[TestClass]
public sealed class GameAvailabilityServiceTests
{
    private string _tempRoot = null!;
    private string _stateFileName = "games-state.json";
    private Mock<IStoragePathService> _storagePathMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "knockbox-availability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        
        _storagePathMock = new Mock<IStoragePathService>();
        _storagePathMock.Setup(x => x.GetAdminDirectory()).Returns(_tempRoot);
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
        var statePath = Path.Combine(_tempRoot, _stateFileName);
        var json = File.ReadAllText(statePath);
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
        var statePath = Path.Combine(_tempRoot, _stateFileName);
        File.WriteAllText(statePath, "{ not valid json");

        // Should not throw; should load as if nothing is disabled.
        var svc = CreateService();
        Assert.IsTrue(svc.IsEnabled("card-counter"));
    }

    [TestMethod]
    public void EmptyDisabledArray_IsTolerated()
    {
        var statePath = Path.Combine(_tempRoot, _stateFileName);
        File.WriteAllText(statePath, JsonSerializer.Serialize(new { disabled = Array.Empty<string>() }));

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
        var options = Options.Create(new AdminOptions { GameStatePath = _stateFileName });
        return new GameAvailabilityService(_storagePathMock.Object, options, NullLogger<GameAvailabilityService>.Instance);
    }
}

