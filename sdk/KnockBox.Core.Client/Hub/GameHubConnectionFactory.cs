using Microsoft.AspNetCore.SignalR.Client;

namespace KnockBox.Core.Client.Hub;

/// <summary>
/// Builds a <see cref="HubConnection"/> to the host's <c>GameHub</c>. The
/// connection is same-origin (the host serves the client), so the browser
/// automatically attaches any existing auth cookie on the handshake. The
/// player's self-asserted identity (the localStorage-backed user id/name that
/// is KnockBox's existing player-identity model) is carried as query string so
/// the hub can resolve the caller without a Blazor circuit.
/// </summary>
public sealed class GameHubConnectionFactory(Uri baseAddress)
{
    private readonly Uri _baseAddress = baseAddress;

    public HubConnection Create(Guid userId, string userName)
    {
        var hubUri = new Uri(
            _baseAddress,
            $"hubs/game?userId={Uri.EscapeDataString(userId.ToString())}&userName={Uri.EscapeDataString(userName)}");

        return new HubConnectionBuilder()
            .WithUrl(hubUri)
            .WithAutomaticReconnect()
            .Build();
    }
}
