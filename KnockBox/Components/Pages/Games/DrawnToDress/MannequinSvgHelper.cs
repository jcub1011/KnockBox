namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    /// <summary>
    /// Generates the mannequin SVG overlay used in drawing and outfit customization phases.
    /// The mannequin is drawn at scale(2,2) centered around a native 600px width.
    /// </summary>
    public static class MannequinSvgHelper
    {
        private const string HighlightColor = "#7c3aed";
        private const string DefaultColor = "#e2e8f0";

        /// <summary>
        /// Builds the mannequin SVG string.
        /// </summary>
        /// <param name="canvasWidth">Total canvas width; the mannequin is centered at (canvasWidth / 2) - 600.</param>
        /// <param name="yOffset">Vertical translation applied to the mannequin group.</param>
        /// <param name="activeTypeId">
        /// If non-null, the body part matching this clothing type ID is highlighted in purple.
        /// If null, all parts render in the default gray.
        /// </param>
        public static string Build(int canvasWidth, int yOffset, string? activeTypeId = null)
        {
            int xOffset = (canvasWidth / 2) - 600;

            string headColor = activeTypeId == "hat" ? HighlightColor : DefaultColor;
            string torsoColor = activeTypeId == "top" ? HighlightColor : DefaultColor;
            string legsColor = activeTypeId == "bottom" ? HighlightColor : DefaultColor;
            string feetColor = activeTypeId == "shoes" ? HighlightColor : DefaultColor;

            return $@"
                <g transform=""translate({xOffset}, {yOffset}) scale(2, 2)"" fill-opacity=""0.3"" stroke=""#94a3b8"" stroke-width=""2"">
                    <circle cx=""300"" cy=""80"" r=""50"" fill=""{headColor}"" />
                    <path d=""M240,140 L360,140 L340,300 L260,300 Z"" fill=""{torsoColor}"" />
                    <path d=""M240,140 L200,280"" stroke-linecap=""round"" />
                    <path d=""M360,140 L400,280"" stroke-linecap=""round"" />
                    <path d=""M260,300 L240,520"" fill=""none"" stroke=""{legsColor}"" stroke-width=""40"" stroke-linecap=""round"" />
                    <path d=""M340,300 L360,520"" fill=""none"" stroke=""{legsColor}"" stroke-width=""40"" stroke-linecap=""round"" />
                    <path d=""M220,520 L200,550 L250,550 Z"" fill=""{feetColor}"" />
                    <path d=""M380,520 L400,550 L350,550 Z"" fill=""{feetColor}"" />
                </g>";
        }
    }
}
