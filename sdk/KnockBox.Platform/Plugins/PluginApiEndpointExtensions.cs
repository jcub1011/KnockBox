using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// Maps the platform's generic plugin-route HTTP dispatcher onto
/// <c>/api/plugins/{routeIdentifier}/{**subPath}</c>. Plugins opt into HTTP
/// endpoints by implementing <see cref="Core.Plugins.IGameEngineHttpHandler"/>
/// on their engine; the dispatcher resolves the engine and the room via the
/// existing keyed-DI / lobby-URI infrastructure.
/// </summary>
public static class PluginApiEndpointExtensions
{
    private static readonly string[] SupportedMethods = ["GET", "POST", "PUT", "DELETE"];

    public static IEndpointRouteBuilder MapPluginApi(this IEndpointRouteBuilder app)
    {
        // AllowAnonymous is explicit so a future global authorization fallback
        // doesn't accidentally lock down plugin endpoints. v1 treats the
        // obfuscated room URI as the access token (GDD §5.4); handlers that
        // need a stronger identity check must read context.User themselves.
        app.MapMethods(
                "/api/plugins/{routeIdentifier}/{**subPath}",
                SupportedMethods,
                async (string routeIdentifier, string? subPath, HttpContext ctx, PluginHttpDispatcher dispatcher, CancellationToken ct) =>
                    await dispatcher.DispatchAsync(routeIdentifier, subPath ?? string.Empty, ctx, ct))
            .AllowAnonymous();

        return app;
    }
}
