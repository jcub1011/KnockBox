using KnockBox.Tracery.Models;

namespace KnockBox.Tracery.Contracts
{
    /// <summary>
    /// The host-editable subset of a Tracery match's rules. Doubles as the
    /// <see cref="TraceryCommands.UpdateSettings"/> command payload (host → server) and a field on
    /// <see cref="TraceryView"/> (server → every client). It deliberately omits the server-only
    /// playtest tuning tables (length-bonus / rare-letter tables, generation-quality knobs); those
    /// stay at their defaults inside the server's <c>TracerySettings</c>, which this maps to/from.
    /// Init-only properties keep it round-trippable by System.Text.Json via the parameterless ctor.
    /// </summary>
    public sealed record TracerySettingsView
    {
        /// <summary>Smallest grid edge the game supports (matches the server's TracerySettings).</summary>
        public const int MinGridDimension = 3;

        /// <summary>Largest grid edge the game supports (the solver's validated ceiling).</summary>
        public const int MaxGridDimension = 8;

        // ── Mode ────────────────────────────────────────────────────────────────
        public GameMode Mode { get; init; } = GameMode.Standard;
        public int SearchListSize { get; init; } = 10;
        public int SearchPlacementBonusUnit { get; init; } = 10;

        // ── Grid ────────────────────────────────────────────────────────────────
        public int GridWidth { get; init; } = 5;
        public int GridHeight { get; init; } = 5;
        public int MinWordLength { get; init; } = 4;

        // ── Dictionaries ──────────────────────────────────────────────────────────
        public TraceryWordPool GenerationDictionary { get; init; } = TraceryWordPool.ReducedDictionary;
        public TraceryWordPool ValidationDictionary { get; init; } = TraceryWordPool.FullDictionary;

        // ── Rounds & timing ───────────────────────────────────────────────────────
        public int TotalRounds { get; init; } = 3;

        /// <summary>Per-round play time in seconds. <c>0</c> = unlimited (no auto-advance).</summary>
        public int RoundTimerSeconds { get; init; } = 90;

        /// <summary>The one-time "get ready" intro length, in seconds.</summary>
        public int TransitionSeconds { get; init; } = 5;

        /// <summary>The post-round reveal/intermission length, in seconds.</summary>
        public int IntermissionSeconds { get; init; } = 30;

        // ── Scoring ─────────────────────────────────────────────────────────────
        public bool UniqueFindBonusEnabled { get; init; } = true;
        public double UniqueFindMultiplier { get; init; } = 1.5;
        public bool RareLetterBonusEnabled { get; init; } = true;
    }
}
