using System.Globalization;
using KnockBox.DrawnToDress.Services.Logic.Games;

namespace KnockBox.DrawnToDress.Pages
{
    /// <summary>
    /// Generates SVG markup that displays the mannequin PNG as a semi-transparent
    /// reference overlay, positioned so the relevant body region is centered in the
    /// canvas viewport.
    /// </summary>
    public static class MannequinSvgHelper
    {
        private const string ImagePath = "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/mannequin-blank.png";

        /// <summary>Native pixel dimensions of mannequin-blank.png.</summary>
        private const double NativeSize = CompositeCanvasLayout.NativeMannequinSize;

        /// <summary>
        /// Builds the mannequin SVG markup as an <c>&lt;image&gt;</c> element.
        /// </summary>
        /// <param name="canvasWidth">Width of the SVG viewBox coordinate space.</param>
        /// <param name="canvasHeight">Height of the SVG viewBox coordinate space.</param>
        /// <param name="activeTypeId">
        /// Clothing type ID whose body region should be vertically centered in the viewport.
        /// When <see langword="null"/>, the mannequin is positioned centered vertically
        /// (suitable for the full-body outfit customization view).
        /// </param>
        /// <param name="overrideDisplaySize">
        /// When provided, overrides the default <c>canvasWidth * 0.85</c> display size.
        /// Used by the composite canvas to keep the mannequin the same size relative to
        /// items as it was during the drawing phase.
        /// </param>
        public static string Build(int canvasWidth, int canvasHeight, string? activeTypeId = null, double? overrideDisplaySize = null)
        {
            double displayWidth = overrideDisplaySize ?? canvasWidth * 0.85;
            double displayHeight = displayWidth; // 1:1 aspect ratio
            double scale = displayWidth / NativeSize;
            double xOffset = (canvasWidth - displayWidth) / 2.0;

            double yOffset;
            if (activeTypeId is not null && CompositeCanvasLayout.NativeAnchorY.TryGetValue(activeTypeId, out double nativeY))
            {
                // Center the body region vertically in the canvas viewport.
                yOffset = (canvasHeight / 2.0) - (nativeY * scale);
            }
            else
            {
                // Full-body view: center the mannequin vertically (300 px padding each side).
                yOffset = (canvasHeight - displayHeight) / 2.0;
            }

            return string.Create(CultureInfo.InvariantCulture,
                $"""<image href="{ImagePath}" x="{xOffset:F1}" y="{yOffset:F1}" width="{displayWidth:F1}" height="{displayHeight:F1}" opacity="0.3" />""");
        }
    }
}

