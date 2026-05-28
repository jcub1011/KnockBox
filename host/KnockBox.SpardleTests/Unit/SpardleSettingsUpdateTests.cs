using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle;
using KnockBox.Spardle.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.SpardleTests.Unit;

[TestClass]
public class SpardleSettingsUpdateTests
{
    private SpardleState _state = default!;

    [TestInitialize]
    public void Setup()
    {
        var host = UserFactory.Create("Host", "host-id");
        _state = new SpardleState(host, NullLogger.Instance);
    }

    [TestMethod]
    public void UpdateSettings_ReplacesSettings_AndReturnsSuccess()
    {
        var result = _state.UpdateSettings(s => s with { TotalRounds = 8, HostPlaysAlong = true });

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(8, _state.Settings.TotalRounds);
        Assert.IsTrue(_state.Settings.HostPlaysAlong);
    }

    [TestMethod]
    public void UpdateSettings_FiresStateChangedNotification()
    {
        // Notification fires outside the Execute lock and may be dispatched
        // asynchronously, so signal + bounded wait rather than asserting synchronously.
        using var signal = new ManualResetEventSlim(false);
        using var sub = _state.StateChangedEventManager.Subscribe(() =>
        {
            signal.Set();
            return ValueTask.CompletedTask;
        });

        _state.UpdateSettings(s => s with { TotalRounds = 7 });

        Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)), "State change notification was expected.");
    }
}
