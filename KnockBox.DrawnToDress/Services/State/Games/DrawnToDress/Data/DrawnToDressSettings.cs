namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    public class DrawnToDressSettings
    {
        /// <summary>Drawing time per clothing type in seconds. Default: 60.</summary>
        public int DrawingTimePerRound { get; set; } = 60;

        /// <summary>Maximum number of items a player may draw per clothing type. Default: 5.</summary>
        public int MaxItemsPerType { get; set; } = 5;

        /// <summary>Ordered list of clothing types players will draw each round.</summary>
        public List<ClothingType> ClothingTypes { get; set; } =
        [
            ClothingType.Hat,
            ClothingType.Shirt,
            ClothingType.Pants,
            ClothingType.Shoes,
        ];

        /// <summary>Outfit building time limit in seconds. Default: 180 (3 minutes).</summary>
        public int OutfitBuildingTimeLimit { get; set; } = 180;

        /// <summary>Number of outfit rounds (1 or 2). Default: 2.</summary>
        public int NumOutfitRounds { get; set; } = 2;

        /// <summary>Minimum players required to start. Default: 4.</summary>
        public int MinPlayers { get; set; } = 4;

        /// <summary>
        /// Outfit 2 must differ from every Outfit 1 by at least this many items. Default: 2.
        /// </summary>
        public int OutfitDistinctnessRule { get; set; } = 2;

        /// <summary>Whether Outfit 2 may reuse items from Outfit 1. Default: false.</summary>
        public bool CanReuseOutfit1Items { get; set; } = false;

        /// <summary>Voting criteria used during the tournament.</summary>
        public List<VotingCriterion> VotingCriteria { get; set; } =
        [
            VotingCriterion.ThemeAdherence,
            VotingCriterion.PersonalPreference,
        ];

        /// <summary>Point weight per criterion. Must contain an entry for each criterion in VotingCriteria.</summary>
        public Dictionary<VotingCriterion, int> CriterionWeights { get; set; } = new()
        {
            [VotingCriterion.ThemeAdherence] = 5,
            [VotingCriterion.PersonalPreference] = 5,
        };

        /// <summary>Bonus points for winning the most matchups in a voting round. Default: 3.</summary>
        public int RoundWinBonus { get; set; } = 3;

        /// <summary>Bonus points for the tournament winner. Default: 10.</summary>
        public int TournamentWinBonus { get; set; } = 10;

        /// <summary>Optional theme for the game session.</summary>
        public string? Theme { get; set; }
    }
}
