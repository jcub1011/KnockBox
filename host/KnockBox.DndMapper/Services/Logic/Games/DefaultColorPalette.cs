using System.Globalization;

namespace KnockBox.DndMapper.Services.Logic.Games
{
    internal static class DefaultColorPalette
    {
        private static readonly string[] _palette =
        [
            "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728",
            "#9467bd", "#8c564b", "#e377c2", "#17becf"
        ];

        public const string Neutral = "#888";

        public static string ForPlayerSlot(int slotIndex)
            => _palette[((slotIndex % _palette.Length) + _palette.Length) % _palette.Length];

        // Deterministic name -> hex via FNV-1a hash to a hue, projected through fixed
        // saturation/lightness so a "Bob" always becomes the same readable color across
        // sessions and processes (string.GetHashCode is randomized per AppDomain).
        public static string FromName(string? name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) return Neutral;

            uint hash = 2166136261u;
            foreach (var ch in trimmed)
            {
                hash ^= ch;
                hash *= 16777619u;
            }

            int hue = (int)(hash % 360u);
            return HslToHex(hue, 0.62, 0.52);
        }

        private static string HslToHex(int hue, double saturation, double lightness)
        {
            double h = ((hue % 360) + 360) % 360 / 360.0;
            double s = Math.Clamp(saturation, 0.0, 1.0);
            double l = Math.Clamp(lightness, 0.0, 1.0);

            double r, g, b;
            if (s == 0.0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            int ri = (int)Math.Round(r * 255);
            int gi = (int)Math.Round(g * 255);
            int bi = (int)Math.Round(b * 255);
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", ri, gi, bi);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }
    }
}
