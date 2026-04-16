using System.Globalization;

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

        /// <summary>
        /// Builds the mannequin SVG markup as an <c>&lt;image&gt;</c> element.
        /// </summary>
        /// <param name="canvasWidth">Width of the SVG viewBox coordinate space.</param>
        /// <param name="canvasHeight">Height of the SVG viewBox coordinate space.</param>
        /// <param name="itemAnchorY">
        /// Y-coordinate of the body region to vertically center in the viewport
        /// (in native mannequin-image coordinates).
        /// </param>
        /// <param name="nativeMannequinSize">Native pixel dimension of the mannequin image.</param>
        /// <param name="mannequinScaleFactor">Fraction of canvas width used for mannequin display size.</param>
        /// <param name="overrideDisplaySize">
        /// When provided, overrides the default <c>canvasWidth * mannequinScaleFactor</c> display size.
        /// Used by the composite canvas to keep the mannequin the same size relative to
        /// items as it was during the drawing phase.
        /// </param>
        public static string Build(int canvasWidth, int canvasHeight, int itemAnchorY,
            double nativeMannequinSize, double mannequinScaleFactor, double? overrideDisplaySize = null)
        {
            double displayWidth = overrideDisplaySize ?? canvasWidth * mannequinScaleFactor;
            double displayHeight = displayWidth; // 1:1 aspect ratio
            double scale = displayWidth / nativeMannequinSize;
            double xOffset = (canvasWidth - displayWidth) / 2.0;

            double yOffset = (canvasHeight / 2.0) - (itemAnchorY * scale);

            return string.Create(CultureInfo.InvariantCulture,
                $"""<image href="{ImagePath}" x="{xOffset:F1}" y="{yOffset:F1}" width="{displayWidth:F1}" height="{displayHeight:F1}" opacity="0.3" />""");
        }
    }
}
