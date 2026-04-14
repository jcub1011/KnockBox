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

        /// <summary>
        /// The fraction of the item canvas width used for the mannequin display size.
        /// Matches the 0.85 factor in <c>MannequinSvgHelper.Build</c> so the mannequin
        /// appears the same size relative to items as it did during the drawing phase.
        /// </summary>
        public const double MannequinScaleFactor = 0.85;

        /// <summary>Native pixel dimensions of the mannequin PNG (1416×1416).</summary>
        public const double NativeMannequinSize = 1416.0;

        /// <summary>Total vertical padding (200 px top + 200 px bottom).</summary>
        private const int VerticalPadding = 400;

        /// <summary>Default anchor Y for clothing types not listed in <see cref="NativeAnchorY"/>.</summary>
        private const double FallbackAnchorY = 470;

        public static int ComputeCompositeWidth(IReadOnlyList<ClothingTypeDefinition> clothingTypes)
            => (clothingTypes.Count > 0
                ? clothingTypes.Max(ct => ct.CanvasWidth)
                : DefaultCanvasWidth) + CanvasWidthPadding;

        /// <summary>
        /// The mannequin display size in the composite canvas, based on the widest
        /// item canvas so the mannequin-to-item ratio matches the drawing phase.
        /// </summary>
        public static double MannequinDisplaySize(IReadOnlyList<ClothingTypeDefinition> clothingTypes)
            => (clothingTypes.Count > 0
                ? clothingTypes.Max(ct => ct.CanvasWidth)
                : DefaultCanvasWidth) * MannequinScaleFactor;

        /// <summary>
        /// Composite canvas height = mannequin display height + 200 px padding top and bottom.
        /// </summary>
        public static int ComputeCompositeHeight(IReadOnlyList<ClothingTypeDefinition> clothingTypes)
            => (int)MannequinDisplaySize(clothingTypes) + VerticalPadding;

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
            int compositeHeight)
        {
            // Derive mannequin display size from the composite height and padding.
            double mannequinDisplaySize = compositeHeight - VerticalPadding;
            double scale = mannequinDisplaySize / NativeMannequinSize;
            double mannequinYOffset = (compositeHeight - mannequinDisplaySize) / 2.0;

            double bodyPartCenterY = mannequinYOffset + itemAnchorY * scale;

            return (
                X: (compositeWidth - itemCanvasWidth) / 2.0,
                Y: bodyPartCenterY - itemCanvasHeight / 2.0
            );
        }
    }
}
