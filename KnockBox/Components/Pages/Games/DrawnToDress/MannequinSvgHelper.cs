using System.Globalization;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    /// <summary>
    /// Generates SVG markup that displays the mannequin PNG as a semi-transparent
    /// reference overlay, positioned so the relevant body region is centered in the
    /// canvas viewport.
    /// </summary>
    public static class MannequinSvgHelper
    {
        private const string ImagePath = "/content/drawn-to-dress-assets/mannequin-blank.png";

        /// <summary>Native pixel dimensions of mannequin-blank.png.</summary>
        private const double NativeSize = 1416.0;

        /// <summary>
        /// Approximate Y-center of each body region on the 1416×1416 mannequin PNG.
        /// These may need calibration after visual testing.
        /// </summary>
        private static readonly Dictionary<string, double> NativeAnchorY = new()
        {
            ["hat"]    = 170,
            ["top"]    = 470,
            ["bottom"] = 870,
            ["shoes"]  = 1130,
        };

        /// <summary>
        /// Builds the mannequin SVG markup as an <c>&lt;image&gt;</c> element.
        /// </summary>
        /// <param name="canvasWidth">Width of the SVG viewBox coordinate space.</param>
        /// <param name="canvasHeight">Height of the SVG viewBox coordinate space.</param>
        /// <param name="activeTypeId">
        /// Clothing type ID whose body region should be vertically centered in the viewport.
        /// When <see langword="null"/>, the mannequin is positioned with a small top margin
        /// (suitable for the full-body outfit customization view).
        /// </param>
        public static string Build(int canvasWidth, int canvasHeight, string? activeTypeId = null)
        {
            double displayWidth = canvasWidth * 0.85;
            double displayHeight = displayWidth; // 1:1 aspect ratio
            double scale = displayWidth / NativeSize;
            double xOffset = (canvasWidth - displayWidth) / 2.0;

            double yOffset;
            if (activeTypeId is not null && NativeAnchorY.TryGetValue(activeTypeId, out double nativeY))
            {
                // Center the body region vertically in the canvas viewport.
                yOffset = (canvasHeight / 2.0) - (nativeY * scale);
            }
            else
            {
                // Full-body view: small top margin.
                yOffset = canvasHeight * 0.05;
            }

            return string.Create(CultureInfo.InvariantCulture,
                $"""<image href="{ImagePath}" x="{xOffset:F1}" y="{yOffset:F1}" width="{displayWidth:F1}" height="{displayHeight:F1}" opacity="0.3" />""");
        }
    }
}
