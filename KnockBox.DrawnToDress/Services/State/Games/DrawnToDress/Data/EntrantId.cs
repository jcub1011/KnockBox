namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    /// <summary>
    /// Strongly-typed identifier for a tournament entrant, encoding both the player
    /// and the outfit round. Replaces the previous "{playerId}:{round}" string convention.
    /// </summary>
    public readonly record struct EntrantId(string PlayerId, int Round)
    {
        /// <summary>Returns the canonical string form "PlayerId:Round".</summary>
        public override string ToString() => $"{PlayerId}:{Round}";

        /// <summary>
        /// Attempts to parse a string in "playerId:round" format into an <see cref="EntrantId"/>.
        /// Returns <see langword="false"/> if the format is invalid.
        /// </summary>
        public static bool TryParse(string? value, out EntrantId result)
        {
            result = default;
            if (string.IsNullOrEmpty(value)) return false;

            int colonIndex = value.IndexOf(':');
            if (colonIndex <= 0 || colonIndex >= value.Length - 1) return false;

            var playerId = value[..colonIndex];
            if (!int.TryParse(value.AsSpan(colonIndex + 1), out var round)) return false;

            result = new EntrantId(playerId, round);
            return true;
        }
    }
}
