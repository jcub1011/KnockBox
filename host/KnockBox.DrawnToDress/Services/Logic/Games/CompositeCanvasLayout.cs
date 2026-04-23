using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games
{
    /// <summary>
    /// Single source of truth for the composite outfit canvas dimensions and default
    /// item positioning.  Used by both the client (OutfitCustomizationPhase, VotingPhase)
    /// and the server (OutfitCustomizationState) to ensure consistent layout.
    /// </summary>
    public static class CompositeCanvasLayout
    {
        private const int DefaultCanvasWidth = 600;
        private const int CanvasWidthPadding = 100;

        /// <summary>Total vertical padding (200 px top + 200 px bottom).</summary>
        private const int VerticalPadding = 400;

        public static int ComputeCompositeWidth(DrawnToDressConfig config)
            => (config.ClothingTypes.Count > 0
                ? config.ClothingTypes.Max(ct => ct.CanvasWidth)
                : DefaultCanvasWidth) + CanvasWidthPadding;

        /// <summary>
        /// The mannequin display size in the composite canvas, based on the widest
        /// item canvas so the mannequin-to-item ratio matches the drawing phase.
        /// </summary>
        public static double MannequinDisplaySize(DrawnToDressConfig config)
            => (config.ClothingTypes.Count > 0
                ? config.ClothingTypes.Max(ct => ct.CanvasWidth)
                : DefaultCanvasWidth) * config.MannequinScaleFactor;

        /// <summary>
        /// Composite canvas height = mannequin display height + 200 px padding top and bottom.
        /// </summary>
        public static int ComputeCompositeHeight(DrawnToDressConfig config)
            => (int)MannequinDisplaySize(config) + VerticalPadding;

        /// <summary>
        /// Returns the (X, Y) translation for a clothing item in the composite
        /// canvas, aligned so the item's visual center matches the corresponding mannequin
        /// body-part center.
        /// </summary>
        public static (double X, double Y) GetItemPosition(
            int itemCanvasWidth,
            int itemCanvasHeight,
            int itemAnchorY,
            int compositeWidth,
            int compositeHeight,
            double nativeMannequinSize)
        {
            // Derive mannequin display size from the composite height and padding.
            double mannequinDisplaySize = compositeHeight - VerticalPadding;
            double scale = mannequinDisplaySize / nativeMannequinSize;
            double mannequinYOffset = (compositeHeight - mannequinDisplaySize) / 2.0;

            double bodyPartCenterY = mannequinYOffset + itemAnchorY * scale;

            return (
                X: (compositeWidth - itemCanvasWidth) / 2.0,
                Y: bodyPartCenterY - itemCanvasHeight / 2.0
            );
        }

        /// <summary>
        /// Returns the (X, Y) translation for a clothing item in the composite
        /// canvas, respecting any user-provided position overrides in the
        /// <see cref="OutfitSubmission"/>.
        /// </summary>
        public static (double X, double Y) GetItemPosition(
            ClothingTypeDefinition ct,
            int compositeWidth,
            int compositeHeight,
            double nativeMannequinSize,
            OutfitSubmission? outfit = null)
        {
            if (outfit?.Customization.ItemPositionOverrides.TryGetValue(ct.Id, out var ovr) == true)
            {
                return (ovr.X, ovr.Y);
            }

            return GetItemPosition(
                ct.CanvasWidth,
                ct.CanvasHeight,
                ct.MannequinAnchorY,
                compositeWidth,
                compositeHeight,
                nativeMannequinSize);
        }
    }
}
