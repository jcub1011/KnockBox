using KnockBox.Core.Services.State.Games.Shared;
using Microsoft.AspNetCore.Http;

namespace KnockBox.Core.Plugins;

/// <summary>
/// Optional opt-in contract for game engines that expose HTTP endpoints under
/// the platform's generic plugin-route dispatcher
/// (<c>/api/plugins/{routeIdentifier}/{**path}</c>).
/// </summary>
/// <remarks>
/// The dispatcher invokes <see cref="HandleAsync"/> only after it has:
/// <list type="bullet">
///   <item>resolved the engine via the route identifier (keyed-DI),</item>
///   <item>verified the engine implements this interface,</item>
///   <item>resolved the room by its obfuscated URI segment,</item>
/// </list>
/// so the handler can assume <paramref name="state"/> is non-null.
/// <para>
/// The dispatcher does <b>not</b> authenticate the caller. The obfuscated room
/// URI's two random GUIDs are treated as the access token (matching the
/// existing room-URL convention). Handlers that need a stronger identity check
/// must read <c>context.User</c> themselves; for v1, no plugin endpoint
/// requires this.
/// </para>
/// <para>
/// The handler is responsible for any further sub-path routing within its
/// prefix (e.g. <c>images</c> vs <c>images/{id}</c>) and for HTTP method
/// discrimination.
/// </para>
/// </remarks>
public interface IGameEngineHttpHandler
{
    /// <summary>
    /// Handle a plugin-routed HTTP request.
    /// </summary>
    /// <param name="context">The ASP.NET Core HTTP context. The handler may read the body, write a response, set headers, etc.</param>
    /// <param name="roomUri">The obfuscated room URI segment (<c>{guidA}-{guidB}</c>) — same as the trailing segment of <c>LobbyRegistration.Uri</c>.</param>
    /// <param name="state">The resolved game state for the room. Mutations must go through <c>state.Execute*</c> per platform contract.</param>
    /// <param name="subPath">Path components after the obfuscated room URI, joined with '/' (no leading slash). Empty string if no sub-path.</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <returns>An <see cref="IResult"/> the dispatcher will execute.</returns>
    ValueTask<IResult> HandleAsync(
        HttpContext context,
        string roomUri,
        AbstractGameState state,
        string subPath,
        CancellationToken ct);
}
