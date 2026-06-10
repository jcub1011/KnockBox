using KnockBox.Core.Client.Hub;
using KnockBox.Core.Client.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace KnockBox.Core.Client;

/// <summary>
/// DI wiring for the KnockBox WebAssembly client. Registers the HTTP client used
/// to stream runtime plugin assets, the runtime plugin loader, and the game-hub
/// connection factory.
/// </summary>
public static class KnockBoxClientServiceCollectionExtensions
{
    public static IServiceCollection AddKnockBoxClient(this IServiceCollection services, string baseAddress)
    {
        var baseUri = new Uri(baseAddress);

        // One HttpClient pointed at the host origin — backs both runtime DLL
        // streaming and any plugin manifest fetch.
        services.AddSingleton(_ => new HttpClient { BaseAddress = baseUri });

        services.AddSingleton<IClientPluginLoader, RuntimePluginLoader>();
        services.AddSingleton(_ => new GameHubConnectionFactory(baseUri));
        services.AddSingleton<IClientSessionTokenProvider, ClientSessionTokenProvider>();

        // Streams plugin file uploads to POST /api/games/upload (any game client
        // may @inject it). Reuses the host-origin HttpClient + the session token.
        services.AddSingleton<PluginUploadClient>();

        return services;
    }
}
