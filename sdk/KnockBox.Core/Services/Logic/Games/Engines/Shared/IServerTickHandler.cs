using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Core.Services.Logic.Games.Engines.Shared
{
    /// <summary>
    /// Lets a game engine opt into a server-owned clock. In the Blazor Server model
    /// the <b>host's browser circuit</b> drove time-based transitions by calling the
    /// engine every frame (<c>LobbyPageBase.TryGetHostTick</c> → <c>ITickService</c>).
    /// In the WASM model the host has no circuit, so the server must drive the clock:
    /// the platform's <c>LobbyTickService</c> walks every open lobby on a fixed cadence
    /// and, for engines implementing this interface, calls <see cref="Tick"/>.
    /// <para>
    /// Resolved off the keyed <see cref="AbstractGameEngine"/> exactly like
    /// <see cref="IGameStateProjector"/> / <see cref="IGameCommandHandler"/> — the host
    /// needs no compile-time knowledge of the plugin. Mutate through
    /// <see cref="AbstractGameState.Execute(System.Action)"/>; the resulting
    /// state-change notification re-projects the view to every connection, so no
    /// explicit broadcast is required.
    /// </para>
    /// </summary>
    public interface IServerTickHandler
    {
        /// <summary>
        /// Called server-side at a fixed cadence with the current wall-clock time.
        /// Implementations should be cheap and self-gate (return quickly when no
        /// time-based transition is due). Must acquire the state lock itself via
        /// <see cref="AbstractGameState.Execute(System.Action)"/> rather than assuming
        /// a lock is held.
        /// </summary>
        void Tick(AbstractGameState state, DateTimeOffset now);
    }
}
