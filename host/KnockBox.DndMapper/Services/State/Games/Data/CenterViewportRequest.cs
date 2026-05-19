namespace KnockBox.DndMapper.Services.State.Games.Data
{
    /// <summary>
    /// Transient "centre everyone here" signal broadcast by the host (§6.4 / §15).
    /// Clients compare <see cref="Nonce"/> against their last-seen value to
    /// decide whether to react; this lets the host re-broadcast the same
    /// cell repeatedly and still nudge every viewport.
    /// </summary>
    public sealed record CenterViewportRequest(Guid MapId, double X, double Y, Guid Nonce);
}
