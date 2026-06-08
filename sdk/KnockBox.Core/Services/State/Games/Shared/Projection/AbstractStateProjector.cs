namespace KnockBox.Core.Services.State.Games.Shared.Projection
{
    /// <summary>
    /// Base class for a game's per-recipient state projector. Bridges the
    /// strongly-typed <see cref="IStateProjector{TState, TView}"/> a game writes
    /// against to the untyped <see cref="IGameStateProjector"/> the host drives,
    /// performing the <c>is TState</c> guard once so every game doesn't repeat it.
    /// <para>
    /// <b>Default-deny by construction.</b> The single projection method is
    /// <see langword="abstract"/>: a subclass must build the
    /// <typeparamref name="TView"/> explicitly, field by field. There is no
    /// reflective auto-mapping, so a state field that the subclass does not
    /// deliberately copy is simply absent from the projection — a newly added
    /// secret defaults to "not projected" rather than silently leaking. Anything
    /// this returns is serialized and lands in the recipient's browser; copy a
    /// secret-bearing field <b>only</b> when the entry being projected belongs to
    /// <c>recipientId</c>.
    /// </para>
    /// </summary>
    /// <typeparam name="TState">The game's concrete <see cref="AbstractGameState"/>.</typeparam>
    /// <typeparam name="TView">The serializable <c>*.Contracts</c> view DTO.</typeparam>
    public abstract class AbstractStateProjector<TState, TView>
        : IStateProjector<TState, TView>, IGameStateProjector
        where TState : AbstractGameState
    {
        /// <inheritdoc/>
        public abstract TView ProjectFor(TState state, Guid recipientId);

        /// <summary>
        /// Untyped entry point used by the host. Returns <see langword="null"/>
        /// when <paramref name="state"/> is not a <typeparamref name="TState"/>
        /// (a misconfiguration), otherwise delegates to the typed override.
        /// </summary>
        object? IGameStateProjector.ProjectFor(AbstractGameState state, Guid recipientId)
            => state is TState typed ? ProjectFor(typed, recipientId) : null;
    }
}
