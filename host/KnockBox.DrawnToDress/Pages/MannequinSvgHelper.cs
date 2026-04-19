using System.Globalization;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Pages
{
    /// <summary>
    /// Display/appearance knobs for <see cref="MannequinSvgHelper.Build"/>.
    /// Structural parameters (canvas dimensions, anchor, native size, scale) remain
    /// positional; everything else flows through this record.
    /// </summary>
    public readonly record struct MannequinDisplayOptions(
        string MannequinImagePath,
        double? OverrideDisplaySize = null,
        double Opacity = 0.3,
        FaceType FaceType = FaceType.Default);

    /// <summary>
    /// Generates SVG markup that displays the mannequin PNG as a semi-transparent
    /// reference overlay, positioned so the relevant body region is centered in the
    /// canvas viewport.
    /// </summary>
    public static class MannequinSvgHelper
    {
        /// <summary>
        /// Builds the mannequin SVG markup as an <c>&lt;image&gt;</c> element, optionally
        /// layered with a face overlay when <see cref="MannequinDisplayOptions.FaceType"/>
        /// is not <see cref="FaceType.Default"/>.
        /// </summary>
        /// <param name="canvasWidth">Width of the SVG viewBox coordinate space.</param>
        /// <param name="canvasHeight">Height of the SVG viewBox coordinate space.</param>
        /// <param name="itemAnchorY">
        /// Y-coordinate of the body region to vertically center in the viewport
        /// (in native mannequin-image coordinates).
        /// </param>
        /// <param name="nativeMannequinSize">Native pixel dimension of the mannequin image.</param>
        /// <param name="mannequinScaleFactor">Fraction of canvas width used for mannequin display size.</param>
        /// <param name="options">Image path, opacity, optional display-size override, and face selection.</param>
        public static string Build(int canvasWidth, int canvasHeight, int itemAnchorY,
            double nativeMannequinSize, double mannequinScaleFactor, MannequinDisplayOptions options)
        {
            double displayWidth = options.OverrideDisplaySize ?? canvasWidth * mannequinScaleFactor;
            double displayHeight = displayWidth; // 1:1 aspect ratio
            double scale = displayWidth / nativeMannequinSize;
            double xOffset = (canvasWidth - displayWidth) / 2.0;

            double yOffset = (canvasHeight / 2.0) - (itemAnchorY * scale);

            string mannequin = string.Create(CultureInfo.InvariantCulture,
                $"""<image href="{options.MannequinImagePath}" x="{xOffset:F1}" y="{yOffset:F1}" width="{displayWidth:F1}" height="{displayHeight:F1}" opacity="{options.Opacity:F1}" />""");

            // Skip the face overlay entirely for the default face — avoids a second
            // HTTP request on every render when no expression was chosen.
            if (options.FaceType == FaceType.Default)
            {
                return mannequin;
            }

            string facePath = GetFacePath(options.FaceType);
            string face = string.Create(CultureInfo.InvariantCulture,
                $"""<image href="{facePath}" x="{xOffset:F1}" y="{yOffset:F1}" width="{displayWidth:F1}" height="{displayHeight:F1}" opacity="{options.Opacity:F1}" />""");

            return mannequin + face;
        }

        private static string GetFacePath(FaceType faceType) => faceType switch
        {
            FaceType.Happy => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-happy.png",
            FaceType.Devious => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-devious.png",
            FaceType.Disgust => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-disgust.png",
            FaceType.Cry => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-cry.png",
            FaceType.Mogging => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-mogging.png",
            FaceType.Drag => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-drag.png",
            _ => "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets/face-default.png",
        };
    }
}
