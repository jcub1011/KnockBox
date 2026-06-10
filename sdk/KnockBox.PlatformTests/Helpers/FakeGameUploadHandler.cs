using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.PlatformTests.Helpers;

/// <summary>
/// Records what <see cref="PluginUploadDispatcher"/> passes to
/// <see cref="HandleUploadAsync"/> (caller, kind, file name, fully-read body) so
/// tests can assert the upload contract surface. Defaults to success; set
/// <see cref="ResultToReturn"/> to verify the dispatcher's failure mapping.
/// </summary>
internal sealed class FakeGameUploadHandler : FakeAbstractGameEngine, IGameUploadHandler
{
    public Result ResultToReturn { get; set; } = Result.Success;
    public bool WasInvoked { get; private set; }
    public User? CapturedCaller { get; private set; }
    public string? CapturedKind { get; private set; }
    public string? CapturedFileName { get; private set; }
    public string? CapturedContent { get; private set; }
    public AbstractGameState? CapturedState { get; private set; }

    public async ValueTask<Result> HandleUploadAsync(
        User caller,
        AbstractGameState state,
        string uploadKind,
        string fileName,
        Stream content,
        CancellationToken ct = default)
    {
        WasInvoked = true;
        CapturedCaller = caller;
        CapturedKind = uploadKind;
        CapturedFileName = fileName;
        CapturedState = state;

        using var reader = new StreamReader(content);
        CapturedContent = await reader.ReadToEndAsync(ct);

        return ResultToReturn;
    }
}
