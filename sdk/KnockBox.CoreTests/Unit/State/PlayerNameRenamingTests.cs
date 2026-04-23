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
        UserFactory.Create(name, string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id);

    private static ILogger MakeLogger() => Mock.Of<ILogger>();

    private static TestGameState MakeState(User? host = null)
    {
        host ??= MakeUser("Host");
        return new TestGameState(host, MakeLogger());
    }

    private static string DisplayNameFor(AbstractGameState state, User user) =>
        state.Players.First(e => ReferenceEquals(e.User, user)).DisplayName;

    [TestMethod]
    public void RegisterPlayer_WithSameNameAsHost_AppendsSuffixOnDisplayNameOnly()
    {
        var host = MakeUser("Alice");
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser("Alice");

        state.RegisterPlayer(player);

        Assert.AreEqual("Alice (1)", DisplayNameFor(state, player));
        Assert.AreEqual("Alice", player.Name, "User.Name must not be mutated by RegisterPlayer.");
    }

    [TestMethod]
    public void RegisterPlayer_WithSameNameAsExistingPlayer_AppendsSuffixOnDisplayNameOnly()
    {
        var host = MakeUser("Host");
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));

        var player1 = MakeUser("Alice");
        state.RegisterPlayer(player1);

        var player2 = MakeUser("Alice");
        state.RegisterPlayer(player2);

        Assert.AreEqual("Alice", DisplayNameFor(state, player1));
        Assert.AreEqual("Alice (1)", DisplayNameFor(state, player2));
        Assert.AreEqual("Alice", player1.Name);
        Assert.AreEqual("Alice", player2.Name);
    }

    [TestMethod]
    public void RegisterPlayer_MultiplePlayersWithSameName_IncrementsSuffix()
    {
        var host = MakeUser("Alice");
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));

        var player1 = MakeUser("Alice");
        state.RegisterPlayer(player1);

        var player2 = MakeUser("Alice");
        state.RegisterPlayer(player2);

        Assert.AreEqual("Alice", host.Name);
        Assert.AreEqual("Alice (1)", DisplayNameFor(state, player1));
        Assert.AreEqual("Alice (2)", DisplayNameFor(state, player2));
        Assert.AreEqual("Alice", player1.Name, "User.Name must not be mutated by RegisterPlayer.");
        Assert.AreEqual("Alice", player2.Name, "User.Name must not be mutated by RegisterPlayer.");
    }

    [TestMethod]
    public void RegisterPlayer_LongName_TruncatesToFitSuffix()
    {
        var host = MakeUser("VeryLongName"); // 12 chars
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));

        var player = MakeUser("VeryLongName");
        state.RegisterPlayer(player);

        // "VeryLongName" (12) + " (1)" (4) = 16 -> too long
        // Should truncate original to 12 - 4 = 8 chars
        // "VeryLong" + " (1)" = "VeryLong (1)"
        Assert.AreEqual("VeryLong (1)", DisplayNameFor(state, player));
        Assert.AreEqual("VeryLongName", player.Name, "User.Name must not be mutated by RegisterPlayer.");
    }

    [TestMethod]
    public void RegisterPlayer_Rejoin_KeepsOriginalDisplayName()
    {
        var host = MakeUser("Host");
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));

        var userId = Guid.NewGuid().ToString();
        var player1 = MakeUser("Alice", userId);
        state.RegisterPlayer(player1);
        Assert.AreEqual("Alice", DisplayNameFor(state, player1));

        // Simulate refresh with same ID and name — should keep the same DisplayName.
        var player2 = MakeUser("Alice", userId);
        state.RegisterPlayer(player2);

        Assert.AreEqual("Alice", DisplayNameFor(state, player2));
    }

    [TestMethod]
    public void RegisterPlayer_CollisionWithTruncatedName_FindsNextAvailableCounter()
    {
        var host = MakeUser("VeryLongName"); // 12 chars
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));

        var player1 = MakeUser("VeryLong (1)");
        state.RegisterPlayer(player1);

        var player2 = MakeUser("VeryLongName");
        state.RegisterPlayer(player2);

        // player2's disambiguated display name would be "VeryLong (1)", which collides with
        // player1's existing display name, so it skips to " (2)".
        Assert.AreEqual("VeryLong (2)", DisplayNameFor(state, player2));
        Assert.AreEqual("VeryLongName", player2.Name, "User.Name must not be mutated by RegisterPlayer.");
    }

    [TestMethod]
    public void RegisterPlayer_DoesNotFireUserNameChanged()
    {
        // The whole point of splitting DisplayName out of User: name-disambiguation inside a
        // lobby must NOT fire IUserService.UserNameChanged or mutate the shared User instance.
        var host = MakeUser("Alice");
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));

        var player = MakeUser("Alice");
        var originalName = player.Name;

        state.RegisterPlayer(player);

        Assert.AreEqual(originalName, player.Name,
            "RegisterPlayer must leave User.Name untouched so IUserService.CurrentUser and other lobbies are unaffected.");
    }
}
