using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    /// <summary>
    /// Filters images for canvas rendering. Two independent rules:
    /// <list type="bullet">
    /// <item><c>Hidden</c> images are excluded for everyone — host, players,
    /// and the display view. The host still manages them via
    /// <c>HostLayerPanel</c> (the row stays in the list, dimmed), where the
    /// eye toggle restores visibility.</item>
    /// <item>Fog visibility only applies to non-hosts: an image is hidden
    /// from non-hosts when EVERY cell its axis-aligned bounding box overlaps
    /// is fogged. If any overlapping cell is revealed the image stays visible
    /// — a deliberate "lean toward showing" so partial reveals don't strobe
    /// images on and off as the host paints adjacent cells. Rotation is
    /// approximated via the unrotated AABB; heavy rotation is an accepted
    /// edge case in v1.</item>
    /// </list>
    /// </summary>
    public static class ImageVisibilityFilter
    {
        public static IEnumerable<MapImage> VisibleImagesFor(IEnumerable<MapImage> images, Map map, bool isHost)
        {
            ArgumentNullException.ThrowIfNull(images);
            ArgumentNullException.ThrowIfNull(map);
            var notHidden = images.Where(img => !img.Hidden);
            if (isHost) return notHidden;
            return notHidden.Where(img => AnyCellRevealed(img, map));
        }

        private static bool AnyCellRevealed(MapImage img, Map map)
        {
            // Subtract a small epsilon from the far edges so an image whose
            // width/height lands exactly on a cell boundary still maps to the
            // *last* covered cell rather than the unrelated one past the edge.
            const double Eps = 0.0001;
            var x0 = (int)Math.Floor(img.X);
            var y0 = (int)Math.Floor(img.Y);
            var x1 = (int)Math.Floor(img.X + Math.Max(0, img.Width - Eps));
            var y1 = (int)Math.Floor(img.Y + Math.Max(0, img.Height - Eps));
            // Previously this checked only the four corners, which mis-fired
            // for full-map-sized background images: their corners always land
            // on the outermost cells, often still fogged, so the entire
            // background got filtered out from the display and the projector
            // appeared blank. Walking the AABB is bounded by the map dimensions
            // (which are small, typically <100×100), so the cost is acceptable
            // and only paid when the projection is rebuilt.
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!map.IsFogged(x, y)) return true;
                }
            }
            return false;
        }
    }
}
