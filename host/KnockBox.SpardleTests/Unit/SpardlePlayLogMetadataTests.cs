using System.Collections.Immutable;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle;
using KnockBox.Spardle.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.SpardleTests.Unit;

[TestClass]
public class SpardlePlayLogMetadataTests
{
    private SpardleState _state = default!;
    private User _alice = default!;
    private User _bob = default!;
    private User _carol = default!;

    [TestInitialize]
    public void Setup()
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        _state = new SpardleState(host, NullLogger.Instance);

        _alice = UserFactory.Create("Alice", Guid.NewGuid());
        _bob = UserFactory.Create("Bob", Guid.NewGuid());
        _carol = UserFactory.Create("Carol", Guid.NewGuid());

        // Seed a finished match: GameOver phase, frozen participant snapshot,
        // per-player totals, and a two-round history.
        _state.Phase = GamePhase.GameOver;
        _state.SetMatchParticipants(new[]
        {
            new PlayerEntry(_alice, "Alice", null),
            new PlayerEntry(_bob, "Bob", null),
            new PlayerEntry(_carol, "Carol", null),
        });

        // Totals: Bob 30 (leader), Alice 20, Carol 10.
        _state.CreatePlayerState(_alice.Id).TotalScore = 20;
        _state.CreatePlayerState(_bob.Id).TotalScore = 30;
        _state.CreatePlayerState(_carol.Id).TotalScore = 10;

        // Round 1 won by Bob, round 2 won by Alice.
        _state.RoundHistory = ImmutableList.Create(
            Round(1, (_bob, 1), (_alice, 2), (_carol, 3)),
            Round(2, (_alice, 1), (_bob, 2), (_carol, 3)));
    }

    private static RoundResult Round(int number, params (User user, int placement)[] outcomes)
        => new()
        {
            RoundNumber = number,
            Answer = "apple",
            Outcomes = outcomes
                .Select(o => new PlayerRoundOutcome
                {
                    UserId = o.user.Id,
                    DisplayName = o.user.Name,
                    Placement = o.placement,
                })
                .ToList(),
        };

    [TestMethod]
    public void Build_IncludesMatchLevelMetadata()
    {
        var metadata = SpardlePlayLogMetadata.Build(_state, _alice.Id);

        Assert.AreEqual("2", metadata["Rounds Played"]);
        Assert.AreEqual("3", metadata["Players"]);
    }

    [TestMethod]
    public void Build_IncludesPersonalMetadata_WhenUserIsParticipant()
    {
        // Alice: 20 pts (2nd behind Bob's 30), won 1 round.
        var metadata = SpardlePlayLogMetadata.Build(_state, _alice.Id);

        Assert.AreEqual("20", metadata["My Score"]);
        Assert.AreEqual("1", metadata["Rounds Won"]);
        Assert.AreEqual("2 / 3", metadata["Placement"]);
    }

    [TestMethod]
    public void Build_RanksTopScorerFirst()
    {
        // Bob is the highest scorer → placement 1 / 3.
        var metadata = SpardlePlayLogMetadata.Build(_state, _bob.Id);

        Assert.AreEqual("30", metadata["My Score"]);
        Assert.AreEqual("1", metadata["Rounds Won"]);
        Assert.AreEqual("1 / 3", metadata["Placement"]);
    }

    [TestMethod]
    public void Build_RanksLowestScorerLast()
    {
        // Carol is the lowest scorer with no round wins → placement 3 / 3.
        var metadata = SpardlePlayLogMetadata.Build(_state, _carol.Id);

        Assert.AreEqual("10", metadata["My Score"]);
        Assert.AreEqual("0", metadata["Rounds Won"]);
        Assert.AreEqual("3 / 3", metadata["Placement"]);
    }

    [TestMethod]
    public void Build_TiebreaksEqualScoresByRoundsWon()
    {
        // Give Alice and Bob equal totals; Bob won more rounds (1) than Alice (1)?
        // Reseed so they tie on score (25) but Bob has 2 round wins vs Alice's 1.
        _state.CreatePlayerState(_alice.Id).TotalScore = 25;
        _state.CreatePlayerState(_bob.Id).TotalScore = 25;
        _state.RoundHistory = ImmutableList.Create(
            Round(1, (_bob, 1), (_alice, 2)),
            Round(2, (_bob, 1), (_alice, 2)),
            Round(3, (_alice, 1), (_bob, 2)));

        var aliceMeta = SpardlePlayLogMetadata.Build(_state, _alice.Id);
        var bobMeta = SpardlePlayLogMetadata.Build(_state, _bob.Id);

        // Bob (2 wins) outranks Alice (1 win) on the rounds-won tiebreak.
        Assert.AreEqual("1 / 3", bobMeta["Placement"]);
        Assert.AreEqual("2 / 3", aliceMeta["Placement"]);
    }

    [TestMethod]
    public void Build_OmitsPersonalMetadata_WhenUserIsNotParticipant()
    {
        var metadata = SpardlePlayLogMetadata.Build(_state, Guid.NewGuid());

        Assert.IsFalse(metadata.ContainsKey("My Score"));
        Assert.IsFalse(metadata.ContainsKey("Rounds Won"));
        Assert.IsFalse(metadata.ContainsKey("Placement"));
        // Match-level keys remain for a spectating host.
        Assert.AreEqual("2", metadata["Rounds Played"]);
        Assert.AreEqual("3", metadata["Players"]);
    }

    [TestMethod]
    public void Build_OmitsPersonalMetadata_WhenUserIdIsNull()
    {
        var metadata = SpardlePlayLogMetadata.Build(_state, currentUserId: null);

        Assert.IsFalse(metadata.ContainsKey("My Score"));
        Assert.AreEqual("3", metadata["Players"]);
    }
}
