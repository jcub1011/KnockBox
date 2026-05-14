using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    public static class SnapToGridHelper
    {
        /// <summary>
        /// Snaps a (x, y) drop position to the nearest cell center when
        /// <see cref="GridConfig.SnapToGrid"/> is true, then clamps to the grid bounds.
        /// Coordinates are in cell units (the SVG viewBox space).
        /// </summary>
        public static (double X, double Y) Snap(double x, double y, GridConfig grid)
        {
            ArgumentNullException.ThrowIfNull(grid);

            if (!grid.SnapToGrid)
            {
                return (
                    Math.Clamp(x, 0, grid.WidthCells),
                    Math.Clamp(y, 0, grid.HeightCells));
            }

            double sx = Math.Round(x - 0.5) + 0.5;
            double sy = Math.Round(y - 0.5) + 0.5;
            return (
                Math.Clamp(sx, 0.5, Math.Max(0.5, grid.WidthCells - 0.5)),
                Math.Clamp(sy, 0.5, Math.Max(0.5, grid.HeightCells - 0.5)));
        }

        /// <summary>
        /// Snaps a corner-anchored (x, y) position to whole-cell coordinates when
        /// <see cref="GridConfig.SnapToGrid"/> is true. Used for image positions and
        /// resize-corner anchors — images are intentionally allowed to extend past the
        /// grid bounds (decorative overlays, oversized maps), so no clamping is applied.
        /// </summary>
        public static (double X, double Y) SnapCorner(double x, double y, GridConfig grid)
        {
            ArgumentNullException.ThrowIfNull(grid);
            if (!grid.SnapToGrid) return (x, y);
            return (Math.Round(x), Math.Round(y));
        }
    }
}
