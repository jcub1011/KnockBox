using KnockBox.Core.Services.State.Users;

namespace KnockBox.Core.Services.State.Games.Shared.Projection
{
    /// <summary>
    /// Produces a per-recipient, default-deny view of a game's authoritative
    /// state. Implemented by a game's engine (a stateless singleton) so the host
    /// can project without compile-time knowledge of the plugin.
    /// <para>
    /// The returned object MUST be a serializable view DTO (a <c>*.Contracts</c>
    /// type) that contains ONLY what <paramref name="recipientId"/> is allowed to
    /// see — never the raw <see cref="AbstractGameState"/>. This is the
    /// security-critical boundary in the WASM model: anything returned here is
    /// serialized and lands in that one player's browser memory.
    /// </para>
    /// <para>
    /// Callers MUST invoke this inside the state's read lock
    /// (<see cref="AbstractGameState.WithExclusiveRead"/> /
    /// <see cref="AbstractGameState.WithExclusiveReadAsync"/>) so projection sees a
    /// consistent snapshot.
    /// </para>
    /// </summary>
    public interface IGameStateProjector
    {
        /// <summary>
        /// Projects <paramref name="state"/> for the player identified by
        /// <paramref name="recipientId"/>. Returns <see langword="null"/> if no
        /// projection applies (e.g. the recipient is not a participant).
        /// </summary>
        object? ProjectFor(AbstractGameState state, Guid recipientId);
    }
}
