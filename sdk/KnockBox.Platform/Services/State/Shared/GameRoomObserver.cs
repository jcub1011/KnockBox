using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Platform.Games;

namespace KnockBox.Platform.Services.State.Shared
{
    /// <summary>
    /// Default <see cref="IGameRoomObserver"/> implementation backed by
    /// <see cref="ILobbyService.TryGetByUri"/>. The lifetime token is a no-op
    /// disposer — the underlying room outlives the observer regardless, so
    /// detachment is only meaningful for the observer's own bookkeeping
    /// (e.g. unsubscribing from <c>StateChangedEventManager</c>).
    /// </summary>
    internal sealed class GameRoomObserver(ILobbyService lobbies) : IGameRoomObserver
    {
        public ValueResult<ObserverAttachment> Attach(string routeIdentifier, string obfuscatedRoomCode)
        {
            if (string.IsNullOrWhiteSpace(routeIdentifier))
                return ValueResult<ObserverAttachment>.FromError("Route identifier is required.");
            if (string.IsNullOrWhiteSpace(obfuscatedRoomCode))
                return ValueResult<ObserverAttachment>.FromError("Room code is required.");

            var uri = $"room/{routeIdentifier}/{obfuscatedRoomCode}";
            if (!lobbies.TryGetByUri(uri, out var registration))
                return ValueResult<ObserverAttachment>.FromError("Room not found.");

            return ValueResult<ObserverAttachment>.FromValue(
                new ObserverAttachment(registration.State, new NoopLifetime()));
        }

        private sealed class NoopLifetime : IDisposable
        {
            public void Dispose() { }
        }
    }
}
