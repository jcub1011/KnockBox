using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.CoreTests.Unit.Projection;

/// <summary>
/// Coverage for <see cref="AbstractStateProjector{TState, TView}"/> — the
/// default-deny base every game's projector derives from. Pins the untyped→typed
/// bridge and the wrong-state-type guard so the host can drive projection without
/// compile-time plugin knowledge.
/// </summary>
[TestClass]
public sealed class AbstractStateProjectorTests
{
    private sealed class TestGameState(User host, ILogger logger) : AbstractGameState(host, logger);

    private sealed class OtherGameState(User host, ILogger logger) : AbstractGameState(host, logger);

    private sealed record TestView(Guid RecipientId, string HostName);

    /// <summary>
    /// A deliberately partial projection: it copies only two fields, demonstrating
    /// that anything not explicitly projected (the rest of the state) never reaches
    /// the view — the structural default-deny guarantee.
    /// </summary>
    private sealed class TestProjector : AbstractStateProjector<TestGameState, TestView>
    {
        public override TestView ProjectFor(TestGameState state, Guid recipientId)
            => new(recipientId, state.Host.Name);
    }

    private static AbstractGameState MakeState<TState>() where TState : AbstractGameState
        => (TState)Activator.CreateInstance(
            typeof(TState), UserFactory.Create("Host", Guid.NewGuid()), Mock.Of<ILogger>())!;

    [TestMethod]
    public void UntypedProjectFor_MatchingStateType_DelegatesToTypedOverride()
    {
        using var state = MakeState<TestGameState>();
        var recipient = Guid.NewGuid();
        IGameStateProjector projector = new TestProjector();

        var view = projector.ProjectFor(state, recipient);

        var typed = (TestView)view!;
        Assert.AreEqual(recipient, typed.RecipientId);
        Assert.AreEqual("Host", typed.HostName);
    }

    [TestMethod]
    public void UntypedProjectFor_WrongStateType_ReturnsNull()
    {
        using var state = MakeState<OtherGameState>();
        IGameStateProjector projector = new TestProjector();

        var view = projector.ProjectFor(state, Guid.NewGuid());

        Assert.IsNull(view);
    }
}
