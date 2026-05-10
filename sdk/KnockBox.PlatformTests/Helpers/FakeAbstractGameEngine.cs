using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.PlatformTests.Helpers;

/// <summary>
/// Minimal <see cref="AbstractGameEngine"/> with no <c>IGameEngineHttpHandler</c>
/// implementation — used to assert the dispatcher returns 404 for engines that
/// have not opted into HTTP routing.
/// </summary>
internal class FakeAbstractGameEngine : AbstractGameEngine
{
    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
    {
        var state = new FakeState(host);
        return Task.FromResult<ValueResult<AbstractGameState>>(state);
    }

    protected override Task<Result> StartAsyncCore(AbstractGameState state, CancellationToken ct = default)
        => Task.FromResult(Result.Success);

    internal sealed class FakeState(User host) : AbstractGameState(host, NullLogger.Instance);
}
