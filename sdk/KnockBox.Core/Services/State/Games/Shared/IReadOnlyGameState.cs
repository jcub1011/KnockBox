using KnockBox.Core.Primitives.Events;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using System.Collections.Immutable;

namespace KnockBox.Core.Services.State.Games.Shared
{
    /// <summary>
    /// Read-only view over a lobby's <see cref="AbstractGameState"/>. Exposes the
    /// non-mutating surface — roster snapshots, lobby flags, lifecycle metadata, the
    /// state-change subscription, and the shared-read entry points — but none of the
    /// mutators (<see cref="AbstractGameState.Execute(System.Action)"/>,
    /// <see cref="AbstractGameState.SetJoinable"/>, etc.).
    /// </summary>
    /// <remarks>
    /// Components and pages that only observe state should depend on this interface to
    /// make read-only intent explicit and keep the engine-vs-UI boundary clear.
    /// <see cref="AbstractGameState"/> implements it, so any concrete state can be passed
    /// where an <see cref="IReadOnlyGameState"/> is expected. Reads remain lock-free over
    /// the immutable roster snapshots; the contract carries no thread-safety obligation
    /// beyond what <see cref="AbstractGameState"/> already documents.
    /// </remarks>
    public interface IReadOnlyGameState
    {
        /// <summary>
        /// The UTC time when this state was created.
        /// </summary>
        DateTime CreatedAt { get; }

        /// <summary>
        /// True if this state has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// The host of the game.
        /// </summary>
        User Host { get; }

        /// <summary>
        /// If this lobby is open for players to join. Indicates the game phase, not
        /// whether a seat is available.
        /// </summary>
        bool IsJoinable { get; }

        /// <summary>
        /// When <c>true</c>, the host is treated as a game participant — appearing in
        /// <see cref="Participants"/> alongside registered players.
        /// </summary>
        bool HostIsParticipant { get; }

        /// <summary>
        /// The registered players in this game (excludes the host).
        /// </summary>
        ImmutableArray<PlayerEntry> Players { get; }

        /// <summary>
        /// The full roster — <see cref="Host"/> first, then every registered player.
        /// </summary>
        ImmutableArray<PlayerEntry> RosterIncludingHost { get; }

        /// <summary>
        /// Participants for gameplay purposes — equals <see cref="Players"/> when
        /// <see cref="HostIsParticipant"/> is <c>false</c>; otherwise the host followed
        /// by the players.
        /// </summary>
        ImmutableArray<PlayerEntry> Participants { get; }

        /// <summary>
        /// Players that have been kicked from this game.
        /// </summary>
        ImmutableArray<User> KickedPlayers { get; }

        /// <summary>
        /// Raised after the state changes (fired outside the execute lock). Subscribe
        /// and dispose the returned handle to unsubscribe.
        /// </summary>
        IThreadSafeEventManager StateChangedEventManager { get; }

        /// <summary>
        /// Checks whether the given user has been kicked from this game.
        /// </summary>
        bool IsKicked(User? user);

        /// <summary>
        /// Subscribes to the "state disposed" signal. The returned <see cref="IDisposable"/>
        /// unsubscribes the handler.
        /// </summary>
        IDisposable SubscribeStateDisposed(Action handler);

        /// <summary>
        /// Subscribes to the "player unregistered" signal. The returned
        /// <see cref="IDisposable"/> unsubscribes the handler.
        /// </summary>
        IDisposable SubscribePlayerUnregistered(Action<User> handler);

        /// <summary>
        /// Runs <paramref name="action"/> under a shared read lock — concurrent with
        /// other readers, exclusive against writers. The lambda must not mutate state and
        /// fires no state-change notification.
        /// </summary>
        Result WithExclusiveRead(Action action);

        /// <summary>
        /// Async counterpart to <see cref="WithExclusiveRead"/>.
        /// </summary>
        ValueTask<Result> WithExclusiveReadAsync(Func<ValueTask> action, CancellationToken ct = default);
    }
}
