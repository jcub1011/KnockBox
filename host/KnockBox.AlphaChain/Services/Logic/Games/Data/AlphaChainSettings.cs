using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// Host-configurable match rules for Alpha Chain. Immutable init-only record,
    /// mutated atomically via <c>AlphaChainGameState.UpdateSettings</c> (mirrors
    /// <c>OperatorSettings</c> / <c>CodewordSettings</c>). Enum members carry
    /// <see cref="JsonStringEnumConverter"/> so the record persists by name when it is
    /// written to the host's <c>localStorage</c> in M5.
    /// </summary>
    public sealed record AlphaChainSettings
    {
        // ── Validation bounds (named constants; single source of truth) ────────

        /// <summary>Minimum legal shot-clock length, in seconds.</summary>
        public const int MinShotClockSeconds = 5;

        /// <summary>Maximum legal shot-clock length, in seconds.</summary>
        public const int MaxShotClockSeconds = 60;

        /// <summary>Minimum rounds per era.</summary>
        public const int MinEraInterval = 1;

        /// <summary>Upper bound on rounds per era (keeps the number input bounded).</summary>
        public const int MaxEraInterval = 50;

        /// <summary>Minimum number of eras in a match.</summary>
        public const int MinEraCount = 1;

        /// <summary>Upper bound on eras (keeps the number input bounded).</summary>
        public const int MaxEraCount = 50;

        /// <summary>Minimum intermission card-select timer, in seconds.</summary>
        public const int MinIntermissionSeconds = 10;

        /// <summary>Upper bound on the intermission card-select timer, in seconds.</summary>
        public const int MaxIntermissionSeconds = 180;

        /// <summary>Minimum sniper-ban timer, in seconds.</summary>
        public const int MinSniperBanSeconds = 5;

        /// <summary>Upper bound on the sniper-ban timer, in seconds.</summary>
        public const int MaxSniperBanSeconds = 120;

        /// <summary>Minimum "Get Ready" countdown shown before a round begins, in seconds.</summary>
        public const int MinCountdownSeconds = 3;

        /// <summary>Upper bound on the "Get Ready" countdown shown before a round begins, in seconds.</summary>
        public const int MaxCountdownSeconds = 15;

        /// <summary>Minimum cards dealt per era (0 is legal — "deal none of this type").</summary>
        public const int MinCardsDealtPerEra = 0;

        /// <summary>Upper bound on cards dealt per era (sanity cap for the number input).</summary>
        public const int MaxCardsDealtPerEra = 10;

        /// <summary>Minimum total runtime of the score-replay animation, in seconds.</summary>
        public const double MinEngineAnimationSeconds = 0.5;

        /// <summary>Maximum total runtime of the score-replay animation, in seconds.</summary>
        public const double MaxEngineAnimationSeconds = 10.0;

        // ── Settings ───────────────────────────────────────────────────────────

        /// <summary>Which letter class the per-round ban draws from.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BanLetterMode BanMode { get; init; } = BanLetterMode.All;

        /// <summary>Seconds on the per-turn shot clock.</summary>
        public int ShotClockSeconds { get; init; } = 20;

        /// <summary>Seconds players have to draft cards at an intermission (M4).</summary>
        public int IntermissionCardSelectSeconds { get; init; } = 60;

        /// <summary>Seconds the Sniper action card grants to choose a banned letter (M3+).</summary>
        public int SniperBanSeconds { get; init; } = 15;

        /// <summary>
        /// Seconds of the "Get Ready" countdown shown before a round begins — at game start
        /// (after any opening tutorial) and again after each era's letter ban — so players have
        /// a beat to prepare before the shot clock starts. The post-ban screen also surfaces the
        /// freshly-banned letter.
        /// </summary>
        public int PreRoundCountdownSeconds { get; init; } = 5;

        /// <summary>Rounds per era.</summary>
        public int EraInterval { get; init; } = 4;

        /// <summary>Total eras before the game ends.</summary>
        public int EraCount { get; init; } = 4;

        /// <summary>When true, eliminated players are out for good rather than scoring negatives.</summary>
        public bool SurvivalMode { get; init; } = false;

        /// <summary>
        /// When true (the default), the three scripted onboarding tutorials play at their cue
        /// points: Shiritori at game start, Engine when the first era ends, and Tax before the
        /// first Sniper Ban. Read at start and at the first era boundary/Intermission; effectively
        /// a start-time choice (the panel is lobby-only).
        /// </summary>
        public bool EnableTutorials { get; init; } = true;

        /// <summary>Modifier cards dealt to each player at an intermission. The Engine Bay is the
        /// only card tier now (the hand-played reaction tier is abolished), so this governs the
        /// whole per-era deal.</summary>
        public int ModifiersDealtPerEra { get; init; } = 3;

        /// <summary>
        /// Total runtime (seconds) of the score-replay animation that plays through the Engine
        /// Bay on every accepted word. Constant regardless of bay size — per-step time shrinks
        /// as cards are added — so a long engine never drags the game out.
        /// </summary>
        public double EngineAnimationSeconds { get; init; } = 1.0;

        /// <summary>
        /// Start-time-only choice set by the lobby's two start buttons (host as shared
        /// display vs. host as player). Lives on the record but is <b>never</b> persisted
        /// to <c>localStorage</c>; mirrors <c>OperatorSettings.HostPlays</c>. Drives
        /// <c>AbstractGameState.SetHostIsParticipant</c> at start.
        /// </summary>
        public bool HostPlays { get; init; } = false;

        // ── Validation ───────────────────────────────────────────────────────────

        /// <summary>
        /// Enumerates every rule this config violates. The single source of truth for what's
        /// a legal start — the lobby gates its start buttons on the result and
        /// <c>StartAsyncCore</c> refuses to begin an illegal match. An empty result means the
        /// config is legal. <see cref="HostPlays"/> is a start-time choice and is not validated.
        /// </summary>
        public ConfigValidationResult Validate()
        {
            var violations = ImmutableArray.CreateBuilder<string>();

            if (ShotClockSeconds < MinShotClockSeconds || ShotClockSeconds > MaxShotClockSeconds)
                violations.Add($"Shot clock must be between {MinShotClockSeconds} and {MaxShotClockSeconds} seconds.");

            if (EraInterval < MinEraInterval || EraInterval > MaxEraInterval)
                violations.Add($"Era interval must be between {MinEraInterval} and {MaxEraInterval} rounds.");

            if (EraCount < MinEraCount || EraCount > MaxEraCount)
                violations.Add($"Era count must be between {MinEraCount} and {MaxEraCount}.");

            if (IntermissionCardSelectSeconds < MinIntermissionSeconds || IntermissionCardSelectSeconds > MaxIntermissionSeconds)
                violations.Add($"Intermission timer must be between {MinIntermissionSeconds} and {MaxIntermissionSeconds} seconds.");

            if (SniperBanSeconds < MinSniperBanSeconds || SniperBanSeconds > MaxSniperBanSeconds)
                violations.Add($"Sniper-ban timer must be between {MinSniperBanSeconds} and {MaxSniperBanSeconds} seconds.");

            if (PreRoundCountdownSeconds < MinCountdownSeconds || PreRoundCountdownSeconds > MaxCountdownSeconds)
                violations.Add($"Get Ready countdown must be between {MinCountdownSeconds} and {MaxCountdownSeconds} seconds.");

            if (ModifiersDealtPerEra < MinCardsDealtPerEra || ModifiersDealtPerEra > MaxCardsDealtPerEra)
                violations.Add($"Modifiers dealt per era must be between {MinCardsDealtPerEra} and {MaxCardsDealtPerEra}.");

            if (EngineAnimationSeconds < MinEngineAnimationSeconds || EngineAnimationSeconds > MaxEngineAnimationSeconds)
                violations.Add($"Engine animation must be between {MinEngineAnimationSeconds:0.#} and {MaxEngineAnimationSeconds:0.#} seconds.");

            if (!Enum.IsDefined(BanMode))
                violations.Add("Ban mode is not a recognized value.");

            return violations.Count == 0
                ? ConfigValidationResult.Valid
                : new ConfigValidationResult(violations.ToImmutable());
        }
    }
}
