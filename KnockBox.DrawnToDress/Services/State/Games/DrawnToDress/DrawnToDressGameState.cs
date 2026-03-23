using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.DrawnToDress
{
    public class DrawnToDressGameState(
        User host,
        ILogger<DrawnToDressGameState> logger,
        DrawnToDressSettings? settings = null)
        : AbstractGameState(host, logger)
    {
        private readonly List<ClothingItem> _availablePool = new();
        private readonly List<ClothingItem> _allDrawings = new();
        private readonly ConcurrentDictionary<Guid, Outfit> _outfits = new();
        private readonly ConcurrentDictionary<string, PlayerScore> _playerScores = new();
        private readonly List<VotingMatchup> _votingMatchups = new();
        private readonly Lock _poolLock = new();
        private readonly Lock _matchupLock = new();

        // ------------------------------------------------------------------
        // Settings
        // ------------------------------------------------------------------

        public DrawnToDressSettings Settings { get; } = settings ?? new DrawnToDressSettings();

        // ------------------------------------------------------------------
        // Phase tracking
        // ------------------------------------------------------------------

        public GamePhase CurrentPhase { get; private set; } = GamePhase.Lobby;
        public int CurrentDrawingTypeIndex { get; private set; } = 0;
        public int CurrentOutfitRound { get; private set; } = 1;
        public int CurrentVotingRound { get; private set; } = 0;
        public int TotalVotingRounds { get; private set; } = 0;

        public ClothingType CurrentDrawingType =>
            Settings.ClothingTypes[CurrentDrawingTypeIndex];

        public bool IsLastDrawingType =>
            CurrentDrawingTypeIndex >= Settings.ClothingTypes.Count - 1;

        // ------------------------------------------------------------------
        // Clothing pool
        // ------------------------------------------------------------------

        /// <summary>All clothing items ever drawn across all players.</summary>
        public IReadOnlyList<ClothingItem> AllDrawings
        {
            get
            {
                lock (_poolLock) { return _allDrawings.ToList(); }
            }
        }

        /// <summary>Items currently available for claiming.</summary>
        public IReadOnlyList<ClothingItem> AvailablePool
        {
            get
            {
                lock (_poolLock) { return _availablePool.ToList(); }
            }
        }

        // ------------------------------------------------------------------
        // Outfits
        // ------------------------------------------------------------------

        public IReadOnlyDictionary<Guid, Outfit> Outfits => _outfits;

        public Outfit? GetPlayerOutfit(string playerId, int outfitNumber) =>
            _outfits.Values.FirstOrDefault(o => o.PlayerId == playerId && o.OutfitNumber == outfitNumber);

        // ------------------------------------------------------------------
        // Scores
        // ------------------------------------------------------------------

        public IReadOnlyDictionary<string, PlayerScore> PlayerScores => _playerScores;

        // ------------------------------------------------------------------
        // Voting
        // ------------------------------------------------------------------

        public IReadOnlyList<VotingMatchup> VotingMatchups
        {
            get
            {
                lock (_matchupLock) { return _votingMatchups.ToList(); }
            }
        }

        public IReadOnlyList<VotingMatchup> CurrentRoundMatchups
        {
            get
            {
                lock (_matchupLock)
                {
                    return _votingMatchups
                        .Where(m => m.VotingRound == CurrentVotingRound)
                        .ToList();
                }
            }
        }

        // ------------------------------------------------------------------
        // Internal mutation helpers (called inside engine's Execute blocks)
        // ------------------------------------------------------------------

        public void SetPhase(GamePhase phase) => CurrentPhase = phase;

        public void SetCurrentOutfitRound(int round) => CurrentOutfitRound = round;

        public void AdvanceDrawingType() => CurrentDrawingTypeIndex++;

        public void ResetDrawingTypeIndex() => CurrentDrawingTypeIndex = 0;

        public void SetTotalVotingRounds(int rounds) => TotalVotingRounds = rounds;

        public void AdvanceVotingRound() => CurrentVotingRound++;

        // ------------------------------------------------------------------
        // Clothing item management
        // ------------------------------------------------------------------

        public void AddDrawing(ClothingItem item)
        {
            lock (_poolLock)
            {
                _allDrawings.Add(item);
            }
        }

        /// <summary>
        /// Populates the available pool for the current outfit building round.
        /// Each player sees all items except their own drawings.
        /// </summary>
        public void RebuildAvailablePool(IEnumerable<Guid>? excludedItemIds = null)
        {
            var excluded = excludedItemIds?.ToHashSet() ?? new HashSet<Guid>();

            lock (_poolLock)
            {
                _availablePool.Clear();
                foreach (var item in _allDrawings)
                {
                    if (!excluded.Contains(item.Id))
                        _availablePool.Add(item);
                }
            }
        }

        /// <summary>
        /// Attempts to claim an item from the available pool.
        /// Returns the item on success, null if it was already claimed.
        /// </summary>
        public ClothingItem? ClaimItem(Guid itemId)
        {
            lock (_poolLock)
            {
                var item = _availablePool.FirstOrDefault(i => i.Id == itemId);
                if (item is null) return null;
                _availablePool.Remove(item);
                return item;
            }
        }

        /// <summary>Returns an item back to the available pool (e.g. when a player swaps).</summary>
        public void ReturnItem(ClothingItem item)
        {
            lock (_poolLock)
            {
                _availablePool.Add(item);
            }
        }

        // ------------------------------------------------------------------
        // Outfit management
        // ------------------------------------------------------------------

        public bool TryAddOutfit(Outfit outfit) => _outfits.TryAdd(outfit.Id, outfit);

        // ------------------------------------------------------------------
        // Scoring
        // ------------------------------------------------------------------

        public PlayerScore GetOrAddPlayerScore(string playerId, string playerName) =>
            _playerScores.GetOrAdd(playerId, _ => new PlayerScore { PlayerId = playerId, PlayerName = playerName });

        public void AddPoints(string playerId, int points)
        {
            if (!_playerScores.TryGetValue(playerId, out var score)) return;
            lock (score) { score.TotalPoints += points; }
        }

        // ------------------------------------------------------------------
        // Voting management
        // ------------------------------------------------------------------

        public void AddVotingMatchup(VotingMatchup matchup)
        {
            lock (_matchupLock) { _votingMatchups.Add(matchup); }
        }

        // ------------------------------------------------------------------
        // Distinctness check
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns true when <paramref name="outfit2"/> differs from every existing Outfit 1
        /// by at least <see cref="DrawnToDressSettings.OutfitDistinctnessRule"/> items.
        /// </summary>
        public bool IsDistinctFromAllOutfit1s(Outfit outfit2)
        {
            int maxShared = Settings.ClothingTypes.Count - Settings.OutfitDistinctnessRule;
            var outfit2Ids = outfit2.ItemIds.ToHashSet();

            foreach (var outfit1 in _outfits.Values.Where(o => o.OutfitNumber == 1))
            {
                int shared = outfit1.ItemIds.Count(id => outfit2Ids.Contains(id));
                if (shared > maxShared)
                    return false;
            }
            return true;
        }
    }
}
