using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Helpers
{
    /// <summary>
    /// Mirrors <c>DndMapperGameEngine.CanMoveToken</c> for client-side use.
    /// Engine remains the source of truth; the JS drag layer uses this as a hint
    /// to suppress drags the engine would reject.
    /// </summary>
    public static class TokenMovabilityResolver
    {
        public static bool CanMove(bool isHost, bool isOwner, bool isParticipant, TokenMovementPolicy policy)
            => policy switch
            {
                TokenMovementPolicy.OwnerOrHost => isHost || isOwner,
                TokenMovementPolicy.Anyone => isHost || isParticipant,
                TokenMovementPolicy.HostOnly => isHost,
                _ => false,
            };
    }
}
