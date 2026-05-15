using System.Globalization;

namespace KnockBox.DndMapper.Helpers
{
    /// <summary>
    /// Picks a legible text color (black or white) for a token's initial against
    /// its fill, using the YIQ luma threshold — the standard quick contrast pick
    /// for solid backgrounds.
    /// </summary>
    public static class TokenTextContrast
    {
        // YIQ luma threshold above which the background is treated as "light" and
        // text flips to black. 160/255 lands a bit darker than the midpoint so
        // mid-saturation colors (the ones our FromName palette emits) stay on
        // white rather than briefly flipping to black on pure mid-yellows.
        private const int LumaThreshold = 160;

        public static string TextFillFor(string? backgroundHex)
        {
            if (!TryParseHexColor(backgroundHex, out var r, out var g, out var b))
                return "#ffffff";
            int luma = (r * 299 + g * 587 + b * 114) / 1000;
            return luma >= LumaThreshold ? "#000000" : "#ffffff";
        }

        internal static bool TryParseHexColor(string? color, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(color) || color[0] != '#') return false;
            var hex = color.AsSpan(1);
            if (hex.Length == 6)
            {
                return TryParseByte(hex.Slice(0, 2), out r)
                    && TryParseByte(hex.Slice(2, 2), out g)
                    && TryParseByte(hex.Slice(4, 2), out b);
            }
            if (hex.Length == 3)
            {
                if (!TryParseByte(hex.Slice(0, 1), out r)) return false;
                if (!TryParseByte(hex.Slice(1, 1), out g)) return false;
                if (!TryParseByte(hex.Slice(2, 1), out b)) return false;
                // Expand #rgb to #rrggbb by replicating each nibble.
                r *= 17; g *= 17; b *= 17;
                return true;
            }
            return false;
        }

        private static bool TryParseByte(ReadOnlySpan<char> span, out int value)
            => int.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
