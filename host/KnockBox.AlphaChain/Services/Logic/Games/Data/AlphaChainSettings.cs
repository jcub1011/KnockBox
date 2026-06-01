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
        /// <summary>Which letter class the per-round ban draws from.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BanLetterMode BanMode { get; init; } = BanLetterMode.All;

        /// <summary>Seconds on the per-turn shot clock.</summary>
        public int ShotClockSeconds { get; init; } = 12;

        /// <summary>Seconds players have to draft cards at an intermission (M4).</summary>
        public int IntermissionCardSelectSeconds { get; init; } = 30;

        /// <summary>Seconds the Sniper action card grants to choose a banned letter (M3+).</summary>
        public int SniperBanSeconds { get; init; } = 15;

        /// <summary>Rounds per era.</summary>
        public int EraInterval { get; init; } = 4;

        /// <summary>Total eras before the game ends.</summary>
        public int EraCount { get; init; } = 4;

        /// <summary>When true, eliminated players are out for good rather than scoring negatives.</summary>
        public bool SurvivalMode { get; init; } = false;

        /// <summary>Modifier cards dealt to each player at an intermission. Consumed in M4.</summary>
        public int ModifiersDealtPerEra { get; init; } = 3;

        /// <summary>Action cards dealt to each player at an intermission. Consumed in M4.</summary>
        public int ActionsDealtPerEra { get; init; } = 2;

        /// <summary>
        /// Start-time-only choice set by the lobby's two start buttons (host as shared
        /// display vs. host as player). Lives on the record but is <b>never</b> persisted
        /// to <c>localStorage</c>; mirrors <c>OperatorSettings.HostPlays</c>. Drives
        /// <c>AbstractGameState.SetHostIsParticipant</c> at start.
        /// </summary>
        public bool HostPlays { get; init; } = false;
    }
}
