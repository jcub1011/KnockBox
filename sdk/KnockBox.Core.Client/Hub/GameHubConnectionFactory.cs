using Microsoft.AspNetCore.SignalR.Client;

namespace KnockBox.Core.Client.Hub;

/// <summary>
/// Builds a <see cref="HubConnection"/> to the host's <c>GameHub</c>. Identity is
/// carried as a server-signed, per-tab session token (see
/// <see cref="IClientSessionTokenProvider"/>) attached via SignalR's access-token
/// mechanism rather than a visible URL parameter; the hub unprotects it to resolve
/// the caller without a Blazor circuit. The display name is a non-authoritative
/// label passed in the query string.
/// </summary>
public sealed class GameHubConnectionFactory(Uri baseAddress)
{
    private readonly Uri _baseAddress = baseAddress;

    public HubConnection Create(string sessionToken, string userName)
    {
        var hubUri = new Uri(
            _baseAddress,
            $"hubs/game?userName={Uri.EscapeDataString(userName)}");

        return new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(sessionToken);
            })
            .WithAutomaticReconnect()
            .Build();
    }
}
