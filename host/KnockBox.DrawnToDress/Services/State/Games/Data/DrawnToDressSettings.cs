using System.Text.Json.Serialization;

namespace KnockBox.DrawnToDress.Services.State.Games.Data
{
    public sealed record DrawnToDressSettings
    {
        /// <summary>
        /// When <see langword="true"/>, the game provides a mannequin drawing reference
        /// that highlights parts based on what is being drawn.
        /// </summary>
        public bool ShowMannequin { get; init; } = true;

        /// <summary>
        /// When <see langword="true"/>, the game advances phases automatically based on
        /// timers. When <see langword="false"/>, players must manually ready/submit to advance.
        /// </summary>
        public bool EnableTimer { get; init; } = true;

        // ── Drawing phase ─────────────────────────────────────────────────────

        /// <summary>
        /// Number of seconds each player has to draw a clothing item.
        /// </summary>
        public int DrawingTimeSec { get; init; } = 180;

        public bool AllowSketchingDuringOutfitBuilding { get; init; } = false;
        public bool ShowDrawingsOnHostScreen { get; init; } = true;

        // ── Clothing types ────────────────────────────────────────────────────

        private const string AssetBase = "_content/KnockBox.DrawnToDress/content/drawn-to-dress-assets";

        public static IReadOnlyList<ClothingTypeDefinition> DefaultClothingTypes { get; } =
        [
            new() { Id = ClothingType.Hat,    DisplayName = "Hat",    AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 450, MannequinAnchorY = 160, MannequinFocusImagePath = $"{AssetBase}/mannequin-blank-head-focus.webp" },
            new() { Id = ClothingType.Top,    DisplayName = "Top",    AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 450, MannequinAnchorY = 700, MannequinFocusImagePath = $"{AssetBase}/mannequin-blank-top-focus.webp" },
            new() { Id = ClothingType.Bottom, DisplayName = "Bottom", AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 450, MannequinAnchorY = 1080, MannequinFocusImagePath = $"{AssetBase}/mannequin-blank-pants-focus.webp" },
            new() { Id = ClothingType.Shoes,  DisplayName = "Shoes",  AllowMultiple = false, CanvasWidth = 600, CanvasHeight = 350, MannequinAnchorY = 1270, MannequinFocusImagePath = $"{AssetBase}/mannequin-blank-shoes-focus.webp" },
        ];

        /// <summary>
        /// Ordered list of clothing categories available in this session. The list itself
        /// is mutable for backward compatibility with existing call sites that build
        /// new collections via LINQ; replace the whole list via <c>with</c> when changing it.
        /// </summary>
        public List<ClothingTypeDefinition> ClothingTypes { get; init; } =
            [.. DefaultClothingTypes.Select(t => t with { })];

        public string MannequinImagePath { get; init; } = $"{AssetBase}/mannequin-blank.webp";

        public MannequinSize MannequinDimensions { get; init; } = new(1416, 1416);

        public double MannequinScaleFactor { get; init; } = 0.85;

        // ── Theme ─────────────────────────────────────────────────────────────

        // Persisted snapshots must survive enum reordering, so serialize by name.
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ThemeSource ThemeSource { get; init; } = ThemeSource.Random;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ThemeAnnouncement ThemeAnnouncement { get; init; } = ThemeAnnouncement.BeforeDrawing;

        public int ThemeAnnouncementTimeSec { get; init; } = 6;

        public int RandomVotingCandidateCount { get; init; } = 3;

        // ── Pool reveal phase ─────────────────────────────────────────────────

        public int PoolRevealTimeSec { get; init; } = 5;

        // ── Outfit Building phase ─────────────────────────────────────────────

        public int OutfitBuildingTimeSec { get; init; } = 90;

        public int OutfitCustomizationTimeSec { get; init; } = 75;

        // ── Pool / reuse / distinctness ───────────────────────────────────────

        public bool AllowReuseOwnItems { get; init; } = true;

        public bool AllowSelectOwnDrawings { get; init; } = false;

        public bool RequireDistinctItemsPerSlot { get; init; } = true;

        // ── Outfit rounds ────────────────────────────────────────────────────

        public int NumOutfitRounds { get; init; } = 1;

        // ── Outfit 2 ──────────────────────────────────────────────────────────

        public bool CanReuseOutfit1Items { get; init; } = false;

        public int Outfit2DistinctnessThreshold { get; init; } = 3;

        public bool SketchingRequired { get; init; } = false;

        // ── Voting ────────────────────────────────────────────────────────────

        /// <summary>
        /// The criteria on which outfits are judged. Mutable for backward compatibility
        /// with existing call sites that compute new lists; replace via <c>with</c> when
        /// changing it.
        /// </summary>
        public List<VotingCriterionDefinition> VotingCriteria { get; init; } =
        [
            new() { Id = "creativity",   DisplayName = "Creativity",   Weight = 1.0 },
            new() { Id = "theme_match",  DisplayName = "Theme Match",  Weight = 1.0 },
            new() { Id = "overall_look", DisplayName = "Overall Look", Weight = 1.0 },
        ];

        public int VotingTimeSec { get; init; } = 60;

        public bool ShowCreatorDuringVoting { get; init; } = false;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public VoteVisibilityMode VoteVisibility { get; init; } = VoteVisibilityMode.Hidden;

        // ── Tournament format ─────────────────────────────────────────────────

        public int VotingRounds { get; init; } = 0;

        // ── Bonus points ──────────────────────────────────────────────────────

        public int BonusPointsForCompleteOutfit { get; init; } = 1;

        public int RoundLeaderBonusPoints { get; init; } = 3;

        public int TournamentWinnerBonusPoints { get; init; } = 10;

        // ── Voting round results ────────────────────────────────────────────

        public int VotingRoundResultsTimeSec { get; init; } = 5;

        // ── Coin flip ────────────────────────────────────────────────────────

        public int CoinFlipTimeSec { get; init; } = 15;

        // ── Host / connectivity ───────────────────────────────────────────────

        public int HostDisconnectTimeoutSec { get; init; } = 120;

        // ── Constants ─────────────────────────────────────────────────────────

        public const int RecommendedMinimumPlayers = 3;

        // ── Validation ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a copy of this record with all numeric values clamped to sensible
        /// minimums and invalid combinations removed.
        /// </summary>
        public DrawnToDressSettings Normalize()
        {
            // ── Voting criteria ────────────────────────────────────────────────
            // Strip empty IDs and clamp negative weights via a fresh list.
            var normalizedCriteria = VotingCriteria
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => new VotingCriterionDefinition
                {
                    Id = c.Id,
                    DisplayName = c.DisplayName,
                    Weight = c.Weight < 0 ? 0 : c.Weight,
                })
                .ToList();
            if (normalizedCriteria.Count == 0)
            {
                normalizedCriteria =
                [
                    new() { Id = "creativity", DisplayName = "Creativity", Weight = 1.0 },
                ];
            }

            // ── Clothing types: require at least one ───────────────────────────
            var normalizedClothingTypes = ClothingTypes;
            if (normalizedClothingTypes.Count == 0)
            {
                var fallback = DefaultClothingTypes.First(t => t.Id == ClothingType.Top);
                normalizedClothingTypes = [fallback with { }];
            }

            return this with
            {
                DrawingTimeSec = DrawingTimeSec < 30 ? 30 : DrawingTimeSec,
                ThemeAnnouncementTimeSec = ThemeAnnouncementTimeSec < 5 ? 5 : ThemeAnnouncementTimeSec,
                RandomVotingCandidateCount = RandomVotingCandidateCount < 2 ? 2 : RandomVotingCandidateCount,
                PoolRevealTimeSec = PoolRevealTimeSec < 5 ? 5 : PoolRevealTimeSec,
                OutfitBuildingTimeSec = OutfitBuildingTimeSec < 30 ? 30 : OutfitBuildingTimeSec,
                OutfitCustomizationTimeSec = OutfitCustomizationTimeSec < 15 ? 15 : OutfitCustomizationTimeSec,
                NumOutfitRounds = NumOutfitRounds < 1 ? 1 : (NumOutfitRounds > 4 ? 4 : NumOutfitRounds),
                ClothingTypes = normalizedClothingTypes,
                VotingTimeSec = VotingTimeSec < 15 ? 15 : VotingTimeSec,
                VotingRounds = VotingRounds < 0 ? 0 : VotingRounds,
                VotingCriteria = normalizedCriteria,
                BonusPointsForCompleteOutfit = BonusPointsForCompleteOutfit < 0 ? 0 : BonusPointsForCompleteOutfit,
                RoundLeaderBonusPoints = RoundLeaderBonusPoints < 0 ? 0 : RoundLeaderBonusPoints,
                TournamentWinnerBonusPoints = TournamentWinnerBonusPoints < 0 ? 0 : TournamentWinnerBonusPoints,
                VotingRoundResultsTimeSec = VotingRoundResultsTimeSec < 3 ? 3 : VotingRoundResultsTimeSec,
                CoinFlipTimeSec = CoinFlipTimeSec < 5 ? 5 : CoinFlipTimeSec,
                HostDisconnectTimeoutSec = HostDisconnectTimeoutSec < 30 ? 30 : HostDisconnectTimeoutSec,
            };
        }
    }
}
