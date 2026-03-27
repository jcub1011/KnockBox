namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    /// <summary>
    /// All tunable configuration values for a Drawn To Dress session.
    /// Default property values match the GDD baseline.
    /// </summary>
    public class DrawnToDressConfig
    {
        // ── Drawing phase ─────────────────────────────────────────────────────

        /// <summary>
        /// Number of seconds each player has to draw a clothing item.
        /// GDD default: 180 s (3 minutes).
        /// </summary>
        public int DrawingTimeSec { get; set; } = 180;

        /// <summary>
        /// When <see langword="true"/>, players may continue sketching while the
        /// Outfit Building phase is active (e.g. to touch-up items before submitting).
        /// GDD default: <see langword="false"/>.
        /// </summary>
        public bool AllowSketchingDuringOutfitBuilding { get; set; } = false;

        // ── Clothing types ────────────────────────────────────────────────────

        /// <summary>
        /// Ordered list of clothing categories available in this session.
        /// Determines which drawing slots exist and how outfits are assembled.
        /// GDD default: Hat, Top, Bottom, Shoes.
        /// </summary>
        public List<ClothingTypeDefinition> ClothingTypes { get; set; } =
        [
            new() { Id = "hat",        DisplayName = "Hat",       AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 600 },
            new() { Id = "top",        DisplayName = "Top",       AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 600 },
            new() { Id = "bottom",     DisplayName = "Bottom",    AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 600 },
            new() { Id = "shoes",      DisplayName = "Shoes",     AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 600 },
        ];

        // ── Theme ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Determines how the round theme is chosen.
        /// GDD default: <see cref="ThemeSource.Random"/>.
        /// </summary>
        public ThemeSource ThemeSource { get; set; } = ThemeSource.Random;

        /// <summary>
        /// Controls when the selected theme is revealed to players relative to the drawing
        /// phase.
        /// GDD default: <see cref="ThemeAnnouncement.BeforeDrawing"/>.
        /// </summary>
        public ThemeAnnouncement ThemeAnnouncement { get; set; } = ThemeAnnouncement.BeforeDrawing;

        /// <summary>
        /// Number of seconds the theme is displayed before drawing begins.
        /// GDD default: 10 s.
        /// </summary>
        public int ThemeAnnouncementTimeSec { get; set; } = 10;

        /// <summary>
        /// Number of candidate themes presented to players for voting when
        /// <see cref="ThemeSource"/> is <see cref="ThemeSource.RandomVoting"/>.
        /// GDD default: 3.
        /// </summary>
        public int RandomVotingCandidateCount { get; set; } = 3;

        // ── Pool reveal phase ─────────────────────────────────────────────────

        /// <summary>
        /// Number of seconds the pool-reveal screen is displayed before automatically
        /// advancing to outfit building.  Players may press Ready to skip early.
        /// GDD default: 5 s.
        /// </summary>
        public int PoolRevealTimeSec { get; set; } = 5;

        // ── Outfit Building phase ─────────────────────────────────────────────

        /// <summary>
        /// Number of seconds players have to assemble their outfit from available items.
        /// GDD default: 90 s.
        /// </summary>
        public int OutfitBuildingTimeSec { get; set; } = 90;

        /// <summary>
        /// Number of seconds players have to add a custom name and finalize their outfit.
        /// GDD default: 60 s.
        /// </summary>
        public int OutfitCustomizationTimeSec { get; set; } = 60;

        // ── Pool / reuse / distinctness ───────────────────────────────────────

        /// <summary>
        /// When <see langword="true"/>, a player may use their own drawn items in their
        /// outfit in addition to items they have claimed from the pool.
        /// GDD default: <see langword="true"/>.
        /// </summary>
        public bool AllowReuseOwnItems { get; set; } = true;

        /// <summary>
        /// When <see langword="true"/>, each outfit slot must be filled by a distinct
        /// clothing item (no item may appear twice in the same outfit).
        /// GDD default: <see langword="true"/>.
        /// </summary>
        public bool RequireDistinctItemsPerSlot { get; set; } = true;

        // ── Outfit rounds ────────────────────────────────────────────────────

        /// <summary>
        /// Number of outfit rounds each player builds.
        /// GDD default: 1.
        /// </summary>
        public int NumOutfitRounds { get; set; } = 1;

        // ── Outfit 2 ──────────────────────────────────────────────────────────

        /// <summary>
        /// When <see langword="true"/>, players may include in their Outfit 2 the same
        /// items they selected for Outfit 1.  When <see langword="false"/> (default) those
        /// items are excluded from each player's Outfit 2 available set.
        /// GDD default: <see langword="false"/>.
        /// </summary>
        public bool CanReuseOutfit1Items { get; set; } = false;

        /// <summary>
        /// Minimum number of items that Outfit 2 may share (in the same clothing-type slot)
        /// with <em>any</em> player's Outfit 1 before the submission is rejected as too
        /// similar.  Set to <c>0</c> to disable distinctness validation.
        /// GDD default: <c>3</c> (three or more shared items → rejection).
        /// </summary>
        public int Outfit2DistinctnessThreshold { get; set; } = 3;

        /// <summary>
        /// When <see langword="true"/>, players must add a sketch overlay during the
        /// outfit-customization phase before they can submit. The timer and completion
        /// rules are enforced; a submission without a sketch is rejected.
        /// GDD default: <see langword="false"/>.
        /// </summary>
        public bool SketchingRequired { get; set; } = false;

        // ── Voting ────────────────────────────────────────────────────────────

        /// <summary>
        /// The criteria on which outfits are judged. Each criterion carries a relative
        /// weight used when aggregating scores.
        /// GDD defaults: Creativity (1.0), Theme Match (1.0), Overall Look (1.0).
        /// </summary>
        public List<VotingCriterionDefinition> VotingCriteria { get; set; } =
        [
            new() { Id = "creativity",   DisplayName = "Creativity",   Weight = 1.0 },
            new() { Id = "theme_match",  DisplayName = "Theme Match",  Weight = 1.0 },
            new() { Id = "overall_look", DisplayName = "Overall Look", Weight = 1.0 },
        ];

        /// <summary>
        /// Number of seconds voters have per voting round to cast all of their votes.
        /// GDD default: 60 s.
        /// </summary>
        public int VotingTimeSec { get; set; } = 60;

        /// <summary>
        /// When <see langword="true"/>, the creator of each outfit is revealed to voters
        /// during the voting phase. When <see langword="false"/>, outfits are shown
        /// anonymously.
        /// GDD default: <see langword="false"/>.
        /// </summary>
        public bool ShowCreatorDuringVoting { get; set; } = false;

        /// <summary>
        /// Controls which vote information is visible to players during an active voting round.
        /// GDD default: <see cref="VoteVisibilityMode.Hidden"/>.
        /// </summary>
        public VoteVisibilityMode VoteVisibility { get; set; } = VoteVisibilityMode.Hidden;

        // ── Tournament format ─────────────────────────────────────────────────

        /// <summary>
        /// Number of Swiss voting rounds to run.
        /// GDD default: 3.
        /// </summary>
        public int VotingRounds { get; set; } = 3;

        // ── Bonus points ──────────────────────────────────────────────────────

        /// <summary>
        /// Bonus points awarded to a player who submits a complete outfit (all required
        /// clothing types filled) before the outfit building timer expires.
        /// GDD default: 1.
        /// </summary>
        public int BonusPointsForCompleteOutfit { get; set; } = 1;

        /// <summary>
        /// Bonus points awarded to the player(s) with the highest-scoring outfit in a round.
        /// GDD default: 3.
        /// </summary>
        public int RoundLeaderBonusPoints { get; set; } = 3;

        /// <summary>
        /// Bonus points awarded to the overall tournament winner(s).
        /// GDD default: 10.
        /// </summary>
        public int TournamentWinnerBonusPoints { get; set; } = 10;

        // ── Voting round results ────────────────────────────────────────────

        /// <summary>
        /// Number of seconds the voting round results screen is displayed before
        /// automatically advancing to the next round (or final results).
        /// GDD default: 5 s.
        /// </summary>
        public int VotingRoundResultsTimeSec { get; set; } = 5;

        // ── Coin flip ────────────────────────────────────────────────────────

        /// <summary>
        /// Number of seconds the caller has to choose heads or tails during a coin flip.
        /// GDD default: 15 s.
        /// </summary>
        public int CoinFlipTimeSec { get; set; } = 15;

        // ── Host / connectivity ───────────────────────────────────────────────

        /// <summary>
        /// Number of seconds after the host disconnects before the game is automatically
        /// ended.
        /// GDD default: 120 s.
        /// </summary>
        public int HostDisconnectTimeoutSec { get; set; } = 120;

        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>
        /// The recommended minimum number of players for meaningful gameplay.
        /// Below this count the host sees a warning but can still start.
        /// </summary>
        public const int RecommendedMinimumPlayers = 3;

        // ── Validation ────────────────────────────────────────────────────────

        /// <summary>
        /// Normalizes this config in-place, clamping all numeric values to sensible
        /// minimums and removing invalid combinations.
        /// </summary>
        public void Normalize()
        {
            // ── Drawing phase ──────────────────────────────────────────────────
            if (DrawingTimeSec < 30)         DrawingTimeSec = 30;
            if (ThemeAnnouncementTimeSec < 5) ThemeAnnouncementTimeSec = 5;
            if (RandomVotingCandidateCount < 2) RandomVotingCandidateCount = 2;

            // ── Pool reveal ────────────────────────────────────────────────────
            if (PoolRevealTimeSec < 5) PoolRevealTimeSec = 5;

            // ── Outfit building ────────────────────────────────────────────────
            if (OutfitBuildingTimeSec < 30)     OutfitBuildingTimeSec = 30;
            if (OutfitCustomizationTimeSec < 15) OutfitCustomizationTimeSec = 15;

            // ── Outfit rounds ─────────────────────────────────────────────
            if (NumOutfitRounds < 1) NumOutfitRounds = 1;
            if (NumOutfitRounds > 4) NumOutfitRounds = 4;

            // ── Clothing types: require at least one type ─────────────────────
            if (ClothingTypes.Count == 0)
            {
                ClothingTypes =
                [
                    new() { Id = "top", DisplayName = "Top", AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 600 },
                ];
            }

            // ── Voting ─────────────────────────────────────────────────────────
            if (VotingTimeSec < 15) VotingTimeSec = 15;
            if (VotingRounds < 1)  VotingRounds = 1;

            // Ensure voting criteria have non-negative weights; remove any with empty Id.
            VotingCriteria.RemoveAll(c => string.IsNullOrWhiteSpace(c.Id));
            foreach (var criterion in VotingCriteria)
            {
                if (criterion.Weight < 0) criterion.Weight = 0;
            }

            // Require at least one voting criterion.
            if (VotingCriteria.Count == 0)
            {
                VotingCriteria =
                [
                    new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
                ];
            }

            // ── Bonus points ───────────────────────────────────────────────────
            if (BonusPointsForCompleteOutfit < 0) BonusPointsForCompleteOutfit = 0;
            if (RoundLeaderBonusPoints < 0) RoundLeaderBonusPoints = 0;
            if (TournamentWinnerBonusPoints < 0) TournamentWinnerBonusPoints = 0;

            // ── Voting round results ─────────────────────────────────────────────
            if (VotingRoundResultsTimeSec < 3) VotingRoundResultsTimeSec = 3;

            // ── Coin flip ─────────────────────────────────────────────────────────
            if (CoinFlipTimeSec < 5) CoinFlipTimeSec = 5;

            // ── Host / connectivity ────────────────────────────────────────────
            if (HostDisconnectTimeoutSec < 30) HostDisconnectTimeoutSec = 30;
        }
    }
}
