using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.Core.Services.State.Shared
{
    /// <summary>
    /// Read-only attach point for tabs that observe an existing room without
    /// registering a user (e.g. the DnD Mapper display view at
    /// <c>/room/dnd-mapper/{code}/display</c>). The observer never affects
    /// <c>IsJoinable</c>, player counts, or any other engine state — it just
    /// returns the live <see cref="AbstractGameState"/> for read access.
    /// </summary>
    public interface IGameRoomObserver
    {
        ValueResult<ObserverAttachment> Attach(string routeIdentifier, string obfuscatedRoomCode);
    }

    /// <summary>
    /// Handle returned by <see cref="IGameRoomObserver.Attach"/>. Disposing
    /// the <see cref="Lifetime"/> detaches; the underlying room is unaffected.
    /// </summary>
    public sealed record ObserverAttachment(AbstractGameState State, IDisposable Lifetime);
}
