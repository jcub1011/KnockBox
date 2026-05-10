using KnockBox.Core.Plugins;
using KnockBox.Core.Services.State.Games.Shared;
using Microsoft.AspNetCore.Http;

namespace KnockBox.PlatformTests.Helpers;

/// <summary>
/// Records what the dispatcher passes to <see cref="HandleAsync"/> so tests can
/// assert the contract surface. The default <see cref="ResultToReturn"/> is a
/// 200/OK; override <see cref="ThrowOnHandle"/> to verify the dispatcher's
/// exception-translation path.
/// </summary>
internal sealed class FakeGameEngineHttpHandler : FakeAbstractGameEngine, IGameEngineHttpHandler
{
    public IResult ResultToReturn { get; set; } = Results.Ok(new { ok = true });
    public Exception? ThrowOnHandle { get; set; }
    public bool WasInvoked { get; private set; }
    public string? CapturedRoomUri { get; private set; }
    public string? CapturedSubPath { get; private set; }
    public AbstractGameState? CapturedState { get; private set; }
    public HttpContext? CapturedContext { get; private set; }

    public ValueTask<IResult> HandleAsync(
        HttpContext context,
        string roomUri,
        AbstractGameState state,
        string subPath,
        CancellationToken ct)
    {
        WasInvoked = true;
        CapturedContext = context;
        CapturedRoomUri = roomUri;
        CapturedSubPath = subPath;
        CapturedState = state;

        if (ThrowOnHandle is not null)
            throw ThrowOnHandle;

        return ValueTask.FromResult(ResultToReturn);
    }
}
