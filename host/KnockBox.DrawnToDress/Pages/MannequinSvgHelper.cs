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
        /// <param name="mannequinImagePath">Path to the mannequin reference image.</param>
        /// <param name="overrideDisplaySize">
        /// When provided, overrides the default <c>canvasWidth * mannequinScaleFactor</c> display size.
        /// Used by the composite canvas to keep the mannequin the same size relative to
        /// items as it was during the drawing phase.
        /// </param>
        /// <param name="opacity">Opacity of the mannequin and face images (0.0 to 1.0).</param>
        /// <param name="faceType">The face expression to overlay on the mannequin.</param>
        public static string Build(int canvasWidth, int canvasHeight, int itemAnchorY,
            double nativeMannequinSize, double mannequinScaleFactor, string mannequinImagePath, double? overrideDisplaySize = null,
            double opacity = 0.3, KnockBox.DrawnToDress.Services.State.Games.Data.FaceType faceType = KnockBox.DrawnToDress.Services.State.Games.Data.FaceType.Default)
        {
            double displayWidth = overrideDisplaySize ?? canvasWidth * mannequinScaleFactor;
            double displayHeight = displayWidth; // 1:1 aspect ratio
            double scale = displayWidth / nativeMannequinSize;
            double xOffset = (canvasWidth - displayWidth) / 2.0;

            double yOffset = (canvasHeight / 2.0) - (itemAnchorY * scale);

            string mannequin = string.Create(CultureInfo.InvariantCulture,
                $"""<image href="{mannequinImagePath}" x="{xOffset:F1}" y="{yOffset:F1}" width="{displayWidth:F1}" height="{displayHeight:F1}" opacity="{opacity:F1}" />""");

            string facePath = GetFacePath(faceType);
            string face = string.Create(CultureInfo.InvariantCulture,
                $"""<image href="{facePath}" x="{xOffset:F1}" y="{yOffset:F1}" width="{displayWidth:F1}" height="{displayHeight:F1}" opacity="{opacity:F1}" />""");

            return mannequin + face;
        }

        private static string GetFacePath(KnockBox.DrawnToDress.Services.State.Games.Data.FaceType faceType) => faceType switch
        {
            KnockBox.DrawnToDress.Services.State.Games.Data.FaceType.Happy => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-happy.png",
            KnockBox.DrawnToDress.Services.State.Games.Data.FaceType.Devious => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-devious.png",
            KnockBox.DrawnToDress.Services.State.Games.Data.FaceType.Disgust => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-disgust.png",
            KnockBox.DrawnToDress.Services.State.Games.Data.FaceType.Cry => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-cry.png",
            KnockBox.DrawnToDress.Services.State.Games.Data.FaceType.Mogging => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-mogging.png",
            KnockBox.DrawnToDress.Services.State.Games.Data.FaceType.Drag => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-drag.png",
            _ => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-default.png",
        };
    }
}
