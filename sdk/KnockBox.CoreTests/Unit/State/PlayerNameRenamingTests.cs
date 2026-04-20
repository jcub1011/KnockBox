using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public sealed class PlayerNameRenamingTests
{
    private sealed class TestGameState(User host, ILogger logger) : AbstractGameState(host, logger)
    {
    }

    private static User MakeUser(string name, string id = "") =>
        new User(name, string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id);

    private static ILogger MakeLogger() => Mock.Of<ILogger>();

    private static TestGameState MakeState(User? host = null)
    {
        host ??= MakeUser("Host");
        return new TestGameState(host, MakeLogger());
    }

    [TestMethod]
    public void RegisterPlayer_WithSameNameAsHost_AppendsSuffix()
    {
        var host = MakeUser("Alice");
        using var state = MakeState(host);
        state.UpdateJoinableStatus(true);
        var player = MakeUser("Alice");

        state.RegisterPlayer(player);

        Assert.AreEqual("Alice (1)", player.Name);
    }

    [TestMethod]
    public void RegisterPlayer_WithSameNameAsExistingPlayer_AppendsSuffix()
    {
        var host = MakeUser("Host");
        using var state = MakeState(host);
        state.UpdateJoinableStatus(true);
        
        var player1 = MakeUser("Alice");
        state.RegisterPlayer(player1);

        var player2 = MakeUser("Alice");
        state.RegisterPlayer(player2);

        Assert.AreEqual("Alice", player1.Name);
        Assert.AreEqual("Alice (1)", player2.Name);
    }

    [TestMethod]
    public void RegisterPlayer_MultiplePlayersWithSameName_IncrementsSuffix()
    {
        var host = MakeUser("Alice");
        using var state = MakeState(host);
        state.UpdateJoinableStatus(true);

        var player1 = MakeUser("Alice");
        state.RegisterPlayer(player1);

        var player2 = MakeUser("Alice");
        state.RegisterPlayer(player2);

        Assert.AreEqual("Alice", host.Name);
        Assert.AreEqual("Alice (1)", player1.Name);
        Assert.AreEqual("Alice (2)", player2.Name);
    }

    [TestMethod]
    public void RegisterPlayer_LongName_TruncatesToFitSuffix()
    {
        var host = MakeUser("VeryLongName"); // 12 chars
        using var state = MakeState(host);
        state.UpdateJoinableStatus(true);

        var player = MakeUser("VeryLongName");
        state.RegisterPlayer(player);

        // "VeryLongName" (12) + " (1)" (4) = 16 -> too long
        // Should truncate original to 12 - 4 = 8 chars
        // "VeryLong" + " (1)" = "VeryLong (1)"
        Assert.AreEqual("VeryLong (1)", player.Name);
    }

    [TestMethod]
    public void RegisterPlayer_Rejoin_DoesNotRename()
    {
        var host = MakeUser("Host");
        using var state = MakeState(host);
        state.UpdateJoinableStatus(true);

        var userId = Guid.NewGuid().ToString();
        var player1 = MakeUser("Alice", userId);
        state.RegisterPlayer(player1);
        Assert.AreEqual("Alice", player1.Name);

        // Simulate refresh with same ID and name
        var player2 = MakeUser("Alice", userId);
        state.RegisterPlayer(player2);

        Assert.AreEqual("Alice", player2.Name);
    }

    [TestMethod]
    public void RegisterPlayer_CollisionWithTruncatedName_FindsNextAvailableCounter()
    {
        var host = MakeUser("VeryLongName"); // 12 chars
        using var state = MakeState(host);
        state.UpdateJoinableStatus(true);

        var player1 = MakeUser("VeryLong (1)");
        state.RegisterPlayer(player1);

        var player2 = MakeUser("VeryLongName");
        state.RegisterPlayer(player2);

        // Previous logic for player2 (VeryLongName) joined with "VeryLongName" existing:
        // Identical count = 1. New name = "VeryLong (1)".
        // BUT "VeryLong (1)" is already taken by player1.
        // It should skip " (1)" and find " (2)".
        Assert.AreEqual("VeryLong (2)", player2.Name);
    }
}
