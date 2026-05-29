namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// The host-configurable rules for a Tracery match. Held by
    /// <c>TraceryGameState.Settings</c> and replaced atomically via <c>with</c>
    /// expressions inside the state's execute lock (see
    /// <c>TraceryGameState.UpdateSettings</c>). Persisted to the host's browser
    /// localStorage by the room page so preferred rules survive across sessions.
    /// Property-initializer form keeps it round-trippable by System.Text.Json (Web
    /// defaults) via the parameterless constructor + init setters.
    /// </summary>
    /// <remarks>
    /// Defaults come from GDD §8. The scoring (<see cref="UniqueFindMultiplier"/>) and
    /// generation-quality (<see cref="MinFindableWords"/> etc.) knobs are surfaced here —
    /// rather than baked into the engine — so they stay playtest-tunable per GDD §10. The
    /// generation knobs are consumed in Milestone 03; the full scoring tables arrive in
    /// Milestone 06.
    /// </remarks>
    public sealed record TracerySettings
    {
        // ── Grid (GDD §8) ──────────────────────────────────────────────────────
        public int GridWidth { get; init; } = 4;
        public int GridHeight { get; init; } = 4;

        // ── Rounds & timing (GDD §8) ───────────────────────────────────────────
        /// <summary>Per-round play time. <see cref="System.TimeSpan.Zero"/> = unlimited.</summary>
        public TimeSpan RoundTimer { get; init; } = TimeSpan.FromSeconds(90);
        public int TotalRounds { get; init; } = 3;

        /// <summary>Intro/reveal/results pacing between the timed play phases.</summary>
        public TimeSpan TransitionDuration { get; init; } = TimeSpan.FromSeconds(5);

        // ── Word rules (GDD §4, §8) ────────────────────────────────────────────
        public int MinWordLength { get; init; } = 4;

        // ── Scoring toggles & tunables (GDD §5, §10) ───────────────────────────
        public bool UniqueFindBonusEnabled { get; init; } = true;
        public double UniqueFindMultiplier { get; init; } = 1.5;
        public bool RareLetterBonusEnabled { get; init; } = true;

        // ── Host role (mirrors Spardle) ────────────────────────────────────────
        /// <summary>
        /// When true and other players are present, the host plays as a normal
        /// participant instead of becoming the display-only observer. Off by default,
        /// preserving the "host is the shared display once others join" model from the GDD.
        /// </summary>
        public bool HostPlaysAlong { get; init; } = false;

        // ── Generation quality bar (Milestone 03; tunable now per GDD §6, §10) ──
        /// <summary>Minimum findable words a board must have to be accepted (0 = engine default).</summary>
        public int MinFindableWords { get; init; } = 0;

        /// <summary>A board must contain at least one findable word of this length (the "big find").</summary>
        public int MinLongWordLength { get; init; } = 7;

        /// <summary>Prefer boards with at least one rare-letter word.</summary>
        public bool RequireRareLetterWord { get; init; } = true;

        /// <summary>Cap on generate-and-test attempts before accepting the best candidate (0 = engine default).</summary>
        public int MaxGenerationAttempts { get; init; } = 0;
    }
}
