namespace KnockBox.DndMapper.Models
{
    // Per-circuit, mutable holder for the active map's viewport center (in cell
    // coordinates). MapCanvas writes whenever the user pans/zooms; spawn callers
    // (HostTokenPanel) read it to anchor newly spawned tokens to wherever the
    // host is currently looking. Null until the canvas has rendered at least once.
    public sealed class DndMapperViewport
    {
        public double? CenterX { get; set; }
        public double? CenterY { get; set; }
        public Guid? MapId { get; set; }

        public (double X, double Y)? GetCenterFor(Guid mapId)
            => MapId == mapId && CenterX is double x && CenterY is double y ? (x, y) : null;

        public void Set(Guid mapId, double centerX, double centerY)
        {
            MapId = mapId;
            CenterX = centerX;
            CenterY = centerY;
        }
    }
}
