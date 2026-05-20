namespace KnockBox.DndMapper.Services.State.Games.Data
{
    /// <summary>
    /// Host-drawn rectangle (in cell coordinates of <see cref="MapId"/>) that
    /// the display view uses as its SVG viewBox so a chosen region fills the
    /// projector / TV. Lives on state so the display circuit can observe it
    /// via the standard state-change subscription, but is excluded from
    /// save/load — restarting the host process clears the focus.
    /// </summary>
    public sealed record FocusRect(Guid MapId, double X, double Y, double Width, double Height);
}
