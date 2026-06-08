namespace KnockBox.Core.Services.State.Games.Shared.Projection
{
    /// <summary>
    /// Strongly-typed companion to <see cref="IGameStateProjector"/>: produces a
    /// per-recipient, default-deny <typeparamref name="TView"/> from a concrete
    /// <typeparamref name="TState"/>. Game code implements this (usually via
    /// <see cref="AbstractStateProjector{TState, TView}"/>) so the projection is
    /// expressed against the real state type, while the host drives projection
    /// through the untyped <see cref="IGameStateProjector"/> surface.
    /// </summary>
    /// <typeparam name="TState">The game's concrete <see cref="AbstractGameState"/>.</typeparam>
    /// <typeparam name="TView">
    /// The serializable view DTO (a <c>*.Contracts</c> type) containing ONLY what
    /// the recipient may see.
    /// </typeparam>
    public interface IStateProjector<in TState, out TView>
        where TState : AbstractGameState
    {
        /// <summary>
        /// Projects <paramref name="state"/> for <paramref name="recipientId"/>.
        /// Must be called inside the state's read lock for a consistent snapshot.
        /// </summary>
        TView ProjectFor(TState state, Guid recipientId);
    }
}
