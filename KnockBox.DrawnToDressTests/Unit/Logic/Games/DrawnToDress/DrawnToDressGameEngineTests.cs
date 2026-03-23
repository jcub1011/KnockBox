using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBoxTests.Unit.Logic.Games.DrawnToDress
{
    [TestClass]
    public class DrawnToDressGameEngineTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger<DrawnToDressGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DrawnToDressGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private DrawnToDressGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _engineLoggerMock = new Mock<ILogger<DrawnToDressGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _host = new User("Host", "host-id");

            _engine = new DrawnToDressGameEngine(
                _randomMock.Object,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        // ------------------------------------------------------------------
        // CreateStateAsync
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsJoinableState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (DrawnToDressGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
            Assert.AreEqual(GamePhase.Lobby, state.CurrentPhase);

            state.Dispose();
        }

        [TestMethod]
        public async Task CreateStateAsync_NullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // StartAsync
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task StartAsync_ValidConditions_TransitionsToDrawingPhase()
        {
            using var state = await CreateStateWithPlayersAsync(4);

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
            Assert.AreEqual(GamePhase.Drawing, state.CurrentPhase);
        }

        [TestMethod]
        public async Task StartAsync_NonHost_ReturnsError()
        {
            using var state = await CreateStateWithPlayersAsync(4);
            var nonHost = new User("Other", "other");

            var result = await _engine.StartAsync(nonHost, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_TooFewPlayers_ReturnsError()
        {
            using var state = await CreateStateAsync();
            // No extra players joined – host alone is not enough

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_WrongStateType_ReturnsError()
        {
            var result = await _engine.StartAsync(_host, null!);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Drawing phase: SubmitDrawing
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task SubmitDrawing_ValidDrawing_AddsToPool()
        {
            using var state = await StartGameAsync(4);

            var player = new User("P1", "p1");
            var result = _engine.SubmitDrawing(player, state, "<svg/>");

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(1, state.AllDrawings.Count);
            Assert.AreEqual(ClothingType.Hat, state.AllDrawings[0].Type);
        }

        [TestMethod]
        public async Task SubmitDrawing_WrongPhase_ReturnsError()
        {
            using var state = await CreateStateAsync();
            // Still in Lobby phase

            var result = _engine.SubmitDrawing(_host, state, "<svg/>");

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task SubmitDrawing_ExceedsMaxItems_ReturnsError()
        {
            using var state = await StartGameAsync(4);
            var player = new User("P1", "p1");

            for (int i = 0; i < state.Settings.MaxItemsPerType; i++)
                _engine.SubmitDrawing(player, state, "<svg/>");

            var result = _engine.SubmitDrawing(player, state, "<svg/>"); // one too many

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Drawing phase: AdvanceDrawingRound
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task AdvanceDrawingRound_AdvancesToNextType()
        {
            using var state = await StartGameAsync(4);

            var result = _engine.AdvanceDrawingRound(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(ClothingType.Shirt, state.CurrentDrawingType);
        }

        [TestMethod]
        public async Task AdvanceDrawingRound_LastType_TransitionsToOutfitBuilding()
        {
            using var state = await StartGameAsync(4);

            // Advance through Hat → Shirt → Pants → Shoes (last)
            _engine.AdvanceDrawingRound(_host, state); // Hat → Shirt
            _engine.AdvanceDrawingRound(_host, state); // Shirt → Pants
            _engine.AdvanceDrawingRound(_host, state); // Pants → Shoes (last)
            var result = _engine.AdvanceDrawingRound(_host, state); // Shoes → OutfitBuilding

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.CurrentPhase);
        }

        [TestMethod]
        public async Task AdvanceDrawingRound_NonHost_ReturnsError()
        {
            using var state = await StartGameAsync(4);
            var nonHost = new User("Other", "other");

            var result = _engine.AdvanceDrawingRound(nonHost, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Outfit building phase: ClaimItem
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task ClaimItem_ValidClaim_ItemMovedToOutfit()
        {
            using var state = await StartOutfitBuildingAsync(4);
            var player = new User("P1", "p1");

            // Get a hat from the pool that wasn't drawn by this player
            var hat = state.AvailablePool.First(i => i.Type == ClothingType.Hat && i.CreatorId != player.Id);

            var result = _engine.ClaimItem(player, state, hat.Id);

            Assert.IsTrue((bool)result.IsSuccess);
            var outfit = state.GetPlayerOutfit(player.Id, 1);
            Assert.IsNotNull(outfit);
            Assert.AreEqual(hat.Id, outfit.Items[ClothingType.Hat]?.Id);
        }

        [TestMethod]
        public async Task ClaimItem_OwnDrawing_ReturnsError()
        {
            using var state = await StartOutfitBuildingAsync(4);

            // Find an item drawn by the host
            var ownItem = state.AllDrawings.FirstOrDefault(d => d.CreatorId == _host.Id);
            if (ownItem is null)
            {
                Assert.Inconclusive("Host has no drawings in pool.");
                return;
            }

            var result = _engine.ClaimItem(_host, state, ownItem.Id);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task ClaimItem_WrongPhase_ReturnsError()
        {
            using var state = await CreateStateAsync();
            var result = _engine.ClaimItem(_host, state, Guid.NewGuid());

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Outfit building phase: LockOutfit
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task LockOutfit_CompleteOutfit_LocksSuccessfully()
        {
            using var state = await StartOutfitBuildingAsync(4);
            var player = new User("P1", "p1");
            FillCompleteOutfit(player, state);

            var result = _engine.LockOutfit(player, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsTrue(state.GetPlayerOutfit(player.Id, 1)!.IsLocked);
        }

        [TestMethod]
        public async Task LockOutfit_IncompleteOutfit_ReturnsError()
        {
            using var state = await StartOutfitBuildingAsync(4);
            var player = new User("P1", "p1");
            // Claim only one item
            var hat = state.AvailablePool.First(i => i.Type == ClothingType.Hat && i.CreatorId != player.Id);
            _engine.ClaimItem(player, state, hat.Id);

            var result = _engine.LockOutfit(player, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Outfit building phase: EndOutfitBuilding
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task EndOutfitBuilding_TransitionsToCustomization()
        {
            using var state = await StartOutfitBuildingAsync(4);

            var result = _engine.EndOutfitBuilding(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(GamePhase.OutfitCustomization, state.CurrentPhase);
        }

        [TestMethod]
        public async Task EndOutfitBuilding_NonHost_ReturnsError()
        {
            using var state = await StartOutfitBuildingAsync(4);
            var nonHost = new User("Other", "other");

            var result = _engine.EndOutfitBuilding(nonHost, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Customization phase: SubmitOutfit
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task SubmitOutfit_ValidNameAndCompleteOutfit_Succeeds()
        {
            using var state = await StartCustomizationAsync(4);
            var player = new User("P1", "p1");

            var result = _engine.SubmitOutfit(player, state, "My Cool Hat");

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsTrue(state.GetPlayerOutfit(player.Id, 1)!.IsSubmitted);
        }

        [TestMethod]
        public async Task SubmitOutfit_EmptyName_ReturnsError()
        {
            using var state = await StartCustomizationAsync(4);
            var player = new User("P1", "p1");

            var result = _engine.SubmitOutfit(player, state, "   ");

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task SubmitOutfit_WrongPhase_ReturnsError()
        {
            using var state = await CreateStateAsync();

            var result = _engine.SubmitOutfit(_host, state, "Name");

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Voting phase: CastVote
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task CastVote_ValidVote_RecordsSuccessfully()
        {
            using var state = await StartVotingAsync(4);

            var matchup = state.CurrentRoundMatchups.First();
            var voter = FindNonCreatorVoter(state, matchup);

            var votes = BuildVotes(state.Settings.VotingCriteria, voteForA: true);
            var result = _engine.CastVote(voter, state, matchup.Id, votes);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsTrue(matchup.VotedPlayerIds.Contains(voter.Id));
        }

        [TestMethod]
        public async Task CastVote_DoubleVote_ReturnsError()
        {
            using var state = await StartVotingAsync(4);
            var matchup = state.CurrentRoundMatchups.First();
            var voter = FindNonCreatorVoter(state, matchup);

            var votes = BuildVotes(state.Settings.VotingCriteria, voteForA: true);
            _engine.CastVote(voter, state, matchup.Id, votes);
            var result = _engine.CastVote(voter, state, matchup.Id, votes);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task CastVote_CreatorVoting_ReturnsError()
        {
            using var state = await StartVotingAsync(4);
            var matchup = state.CurrentRoundMatchups.First();
            var outfitA = state.Outfits[matchup.OutfitAId];
            var creator = new User("Creator", outfitA.PlayerId);

            var votes = BuildVotes(state.Settings.VotingCriteria, voteForA: true);
            var result = _engine.CastVote(creator, state, matchup.Id, votes);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Voting phase: FinalizeVotingRound
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task FinalizeVotingRound_LastRound_TransitionsToResults()
        {
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                       .Returns(0);

            using var state = await StartVotingAsync(4);

            // Finalize all rounds (there should be ceil(log2(8)) = 3 for 8 outfits / 2 per 4 players)
            for (int i = 0; i < state.TotalVotingRounds; i++)
                _engine.FinalizeVotingRound(_host, state);

            Assert.AreEqual(GamePhase.Results, state.CurrentPhase);
        }

        [TestMethod]
        public async Task FinalizeVotingRound_NonHost_ReturnsError()
        {
            using var state = await StartVotingAsync(4);
            var nonHost = new User("Other", "other");

            var result = _engine.FinalizeVotingRound(nonHost, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ------------------------------------------------------------------
        // Helper methods
        // ------------------------------------------------------------------

        private async Task<DrawnToDressGameState> CreateStateAsync()
        {
            var r = await _engine.CreateStateAsync(_host);
            return (DrawnToDressGameState)r.Value!;
        }

        private async Task<DrawnToDressGameState> CreateStateWithPlayersAsync(int playerCount)
        {
            var state = await CreateStateAsync();
            for (int i = 0; i < playerCount; i++)
            {
                var player = new User($"Player{i}", $"p{i}");
                state.RegisterPlayer(player);
            }
            return state;
        }

        private async Task<DrawnToDressGameState> StartGameAsync(int playerCount)
        {
            var state = await CreateStateWithPlayersAsync(playerCount);
            await _engine.StartAsync(_host, state);
            return state;
        }

        /// <summary>
        /// Starts a game, submits drawings for all players and all types, then advances to OutfitBuilding.
        /// </summary>
        private async Task<DrawnToDressGameState> StartOutfitBuildingAsync(int playerCount)
        {
            var state = await StartGameAsync(playerCount);
            var allPlayers = GetAllPlayers(state);

            // Submit 2 drawings per player per type to ensure enough pool variety for claiming
            foreach (var type in state.Settings.ClothingTypes)
            {
                foreach (var player in allPlayers)
                {
                    _engine.SubmitDrawing(player, state, $"<svg data-type='{type}' data-player='{player.Id}' n='1'/>");
                    _engine.SubmitDrawing(player, state, $"<svg data-type='{type}' data-player='{player.Id}' n='2'/>");
                }

                _engine.AdvanceDrawingRound(_host, state);
            }

            // After advancing past the last type, the phase should be OutfitBuilding
            Assert.AreEqual(GamePhase.OutfitBuilding, state.CurrentPhase);
            return state;
        }

        /// <summary>
        /// Brings the game to OutfitCustomization with all players having a complete locked outfit.
        /// </summary>
        private async Task<DrawnToDressGameState> StartCustomizationAsync(int playerCount)
        {
            var state = await StartOutfitBuildingAsync(playerCount);
            var allPlayers = GetAllPlayers(state);

            foreach (var player in allPlayers)
                FillCompleteOutfit(player, state);

            _engine.EndOutfitBuilding(_host, state);
            Assert.AreEqual(GamePhase.OutfitCustomization, state.CurrentPhase);
            return state;
        }

        /// <summary>
        /// Brings the game to the Voting phase with all players having submitted both outfits.
        /// </summary>
        private async Task<DrawnToDressGameState> StartVotingAsync(int playerCount)
        {
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                       .Returns(0);

            // Settings: only 1 outfit round to simplify
            var settings = new DrawnToDressSettings { NumOutfitRounds = 1, MinPlayers = playerCount };

            var stateWithSettings = new DrawnToDressGameState(_host, _stateLoggerMock.Object, settings);
            stateWithSettings.UpdateJoinableStatus(true);

            for (int i = 0; i < playerCount; i++)
                stateWithSettings.RegisterPlayer(new User($"Player{i}", $"p{i}"));

            await _engine.StartAsync(_host, stateWithSettings);
            var allPlayers = GetAllPlayers(stateWithSettings);

            // Submit 2 drawings per player per type so the pool has enough variety for claiming
            foreach (var type in stateWithSettings.Settings.ClothingTypes)
            {
                foreach (var player in allPlayers)
                {
                    _engine.SubmitDrawing(player, stateWithSettings, $"<svg/>");
                    _engine.SubmitDrawing(player, stateWithSettings, $"<svg/>");
                }
                _engine.AdvanceDrawingRound(_host, stateWithSettings);
            }

            // Fill and lock outfits
            foreach (var player in allPlayers)
                FillCompleteOutfit(player, stateWithSettings);

            _engine.EndOutfitBuilding(_host, stateWithSettings);

            // Submit outfits with names
            foreach (var player in allPlayers)
                _engine.SubmitOutfit(player, stateWithSettings, $"Outfit by {player.Name}");

            _engine.EndCustomizationPhase(_host, stateWithSettings);

            Assert.AreEqual(GamePhase.Voting, stateWithSettings.CurrentPhase);
            return stateWithSettings;
        }

        private static List<User> GetAllPlayers(DrawnToDressGameState state) =>
            new[] { state.Host }.Concat(state.Players).ToList();

        /// <summary>Claims one item of each clothing type for the player from the pool (skipping own drawings).</summary>
        private void FillCompleteOutfit(User player, DrawnToDressGameState state)
        {
            foreach (var type in state.Settings.ClothingTypes)
            {
                var existing = state.GetPlayerOutfit(player.Id, state.CurrentOutfitRound)?.Items[type];
                if (existing is not null) continue; // already has this type

                var available = state.AvailablePool.FirstOrDefault(
                    i => i.Type == type && i.CreatorId != player.Id);
                if (available is null) continue;

                _engine.ClaimItem(player, state, available.Id);
            }
        }

        private static User FindNonCreatorVoter(DrawnToDressGameState state, VotingMatchup matchup)
        {
            var outfitA = state.Outfits[matchup.OutfitAId];
            var outfitB = state.Outfits[matchup.OutfitBId];

            var allPlayers = new[] { state.Host }.Concat(state.Players).ToList();
            return allPlayers.First(p => p.Id != outfitA.PlayerId && p.Id != outfitB.PlayerId);
        }

        private static Dictionary<VotingCriterion, bool> BuildVotes(
            IEnumerable<VotingCriterion> criteria, bool voteForA) =>
            criteria.ToDictionary(c => c, _ => voteForA);
    }
}
