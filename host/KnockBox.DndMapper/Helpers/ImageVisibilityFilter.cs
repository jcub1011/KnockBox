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
    /// from non-hosts when ALL FOUR CORNERS of its axis-aligned bounding box
    /// fall on fogged cells. If any corner sits on a revealed cell the image
    /// stays visible — a deliberate "lean toward showing" so partial reveals
    /// during exploration don't strobe the whole image on and off as the
    /// host paints adjacent cells. Rotation is approximated via the
    /// unrotated AABB; heavy rotation is an accepted edge case in v1.</item>
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
            return notHidden.Where(img => AnyCornerRevealed(img, map));
        }

        private static bool AnyCornerRevealed(MapImage img, Map map)
        {
            // Subtract a small epsilon from the far corners so an image whose
            // width/height land exactly on a cell boundary still maps to the
            // *last* covered cell rather than the unrelated one past the edge.
            const double Eps = 0.0001;
            var x0 = (int)Math.Floor(img.X);
            var y0 = (int)Math.Floor(img.Y);
            var x1 = (int)Math.Floor(img.X + Math.Max(0, img.Width - Eps));
            var y1 = (int)Math.Floor(img.Y + Math.Max(0, img.Height - Eps));
            return !map.IsFogged(x0, y0)
                || !map.IsFogged(x1, y0)
                || !map.IsFogged(x0, y1)
                || !map.IsFogged(x1, y1);
        }
    }
}
