using System;
using System.Globalization;
using System.Text.RegularExpressions;
using KnockBox.DndMapper.Services.State.Games;

namespace KnockBox.DndMapper.Helpers
{
    /// <summary>
    /// Picks a dice background color for a user. Host gets gold; a player with a
    /// colored token assigned gets the token color; everyone else gets a hue
    /// derived deterministically from their user id so the color stays stable
    /// across reconnects without storing per-session state.
    /// </summary>
    public static class DiceColorResolver
    {
        public const string HostGold = "#FFD700";

        public static string Resolve(DndMapperGameState state, string userId)
        {
            if (string.IsNullOrEmpty(userId)) return FallbackForHash(0);
            if (state.Host.Id == userId) return HostGold;

            foreach (var map in state.Maps)
            {
                foreach (var token in map.Tokens)
                {
                    if (token.OwnerUserId == userId && IsValidHex(token.Color))
                        return token.Color;
                }
            }

            var hash = (uint)StringComparer.Ordinal.GetHashCode(userId);
            return FallbackForHash(hash);
        }

        internal static string FallbackForHash(uint hash)
        {
            double hue = hash % 360;
            return HslToHex(hue, 0.55, 0.55);
        }

        private static readonly Regex HexPattern = new(
            @"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static bool IsValidHex(string? value)
            => !string.IsNullOrEmpty(value) && HexPattern.IsMatch(value);

        internal static string HslToHex(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double hp = h / 60.0;
            double x = c * (1 - Math.Abs(hp % 2 - 1));
            double r1, g1, b1;
            if (hp < 1)      (r1, g1, b1) = (c, x, 0);
            else if (hp < 2) (r1, g1, b1) = (x, c, 0);
            else if (hp < 3) (r1, g1, b1) = (0, c, x);
            else if (hp < 4) (r1, g1, b1) = (0, x, c);
            else if (hp < 5) (r1, g1, b1) = (x, 0, c);
            else             (r1, g1, b1) = (c, 0, x);
            double m = l - c / 2;
            int r = (int)Math.Round((r1 + m) * 255);
            int g = (int)Math.Round((g1 + m) * 255);
            int b = (int)Math.Round((b1 + m) * 255);
            return "#" + r.ToString("x2", CultureInfo.InvariantCulture)
                       + g.ToString("x2", CultureInfo.InvariantCulture)
                       + b.ToString("x2", CultureInfo.InvariantCulture);
        }
    }
}
