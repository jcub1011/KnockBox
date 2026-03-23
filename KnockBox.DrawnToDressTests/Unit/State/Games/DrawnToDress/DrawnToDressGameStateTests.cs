using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBoxTests.Unit.State.Games.DrawnToDress
{
    [TestClass]
    public class DrawnToDressGameStateTests
    {
        private Mock<ILogger<DrawnToDressGameState>> _loggerMock = default!;
        private User _host = default!;
        private DrawnToDressGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _host = new User("Host", "host-id");
            _state = new DrawnToDressGameState(_host, _loggerMock.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _state.Dispose();
        }

        // ------------------------------------------------------------------
        // Initial state
        // ------------------------------------------------------------------

        [TestMethod]
        public void InitialPhase_IsLobby()
        {
            Assert.AreEqual(GamePhase.Lobby, _state.CurrentPhase);
        }

        [TestMethod]
        public void InitialPool_IsEmpty()
        {
            Assert.AreEqual(0, _state.AllDrawings.Count);
            Assert.AreEqual(0, _state.AvailablePool.Count);
        }

        [TestMethod]
        public void InitialOutfits_IsEmpty()
        {
            Assert.AreEqual(0, _state.Outfits.Count);
        }

        // ------------------------------------------------------------------
        // Drawing management
        // ------------------------------------------------------------------

        [TestMethod]
        public void AddDrawing_ItemAppearsInAllDrawings()
        {
            var item = MakeItem("p1", ClothingType.Hat);
            _state.AddDrawing(item);

            Assert.AreEqual(1, _state.AllDrawings.Count);
            Assert.AreEqual(item.Id, _state.AllDrawings[0].Id);
        }

        [TestMethod]
        public void RebuildAvailablePool_PopulatesFromAllDrawings()
        {
            _state.AddDrawing(MakeItem("p1", ClothingType.Hat));
            _state.AddDrawing(MakeItem("p2", ClothingType.Shirt));

            _state.RebuildAvailablePool();

            Assert.AreEqual(2, _state.AvailablePool.Count);
        }

        [TestMethod]
        public void RebuildAvailablePool_ExcludesSpecifiedItems()
        {
            var item1 = MakeItem("p1", ClothingType.Hat);
            var item2 = MakeItem("p2", ClothingType.Shirt);
            _state.AddDrawing(item1);
            _state.AddDrawing(item2);

            _state.RebuildAvailablePool(new[] { item1.Id });

            Assert.AreEqual(1, _state.AvailablePool.Count);
            Assert.AreEqual(item2.Id, _state.AvailablePool[0].Id);
        }

        // ------------------------------------------------------------------
        // Item claiming
        // ------------------------------------------------------------------

        [TestMethod]
        public void ClaimItem_RemovesFromAvailablePool()
        {
            var item = MakeItem("p1", ClothingType.Hat);
            _state.AddDrawing(item);
            _state.RebuildAvailablePool();

            var claimed = _state.ClaimItem(item.Id);

            Assert.IsNotNull(claimed);
            Assert.AreEqual(item.Id, claimed.Id);
            Assert.AreEqual(0, _state.AvailablePool.Count);
        }

        [TestMethod]
        public void ClaimItem_AlreadyClaimed_ReturnsNull()
        {
            var item = MakeItem("p1", ClothingType.Hat);
            _state.AddDrawing(item);
            _state.RebuildAvailablePool();

            _state.ClaimItem(item.Id);
            var second = _state.ClaimItem(item.Id);

            Assert.IsNull(second);
        }

        [TestMethod]
        public void ReturnItem_AddsBackToAvailablePool()
        {
            var item = MakeItem("p1", ClothingType.Hat);
            _state.AddDrawing(item);
            _state.RebuildAvailablePool();

            var claimed = _state.ClaimItem(item.Id)!;
            _state.ReturnItem(claimed);

            Assert.AreEqual(1, _state.AvailablePool.Count);
        }

        // ------------------------------------------------------------------
        // Phase transitions
        // ------------------------------------------------------------------

        [TestMethod]
        public void SetPhase_UpdatesCurrentPhase()
        {
            _state.SetPhase(GamePhase.Drawing);
            Assert.AreEqual(GamePhase.Drawing, _state.CurrentPhase);
        }

        [TestMethod]
        public void AdvanceDrawingType_IncrementsIndex()
        {
            int initial = _state.CurrentDrawingTypeIndex;
            _state.AdvanceDrawingType();
            Assert.AreEqual(initial + 1, _state.CurrentDrawingTypeIndex);
        }

        [TestMethod]
        public void IsLastDrawingType_TrueWhenAtLastType()
        {
            // Default clothing types: Hat, Shirt, Pants, Shoes (4 types, index 0-3)
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(_state.IsLastDrawingType);
                _state.AdvanceDrawingType();
            }
            Assert.IsTrue(_state.IsLastDrawingType);
        }

        // ------------------------------------------------------------------
        // Outfit management
        // ------------------------------------------------------------------

        [TestMethod]
        public void TryAddOutfit_AddsOutfitSuccessfully()
        {
            var outfit = new Outfit
            {
                PlayerId = "p1",
                PlayerName = "Player",
                OutfitNumber = 1,
            };

            bool added = _state.TryAddOutfit(outfit);

            Assert.IsTrue(added);
            Assert.AreEqual(1, _state.Outfits.Count);
        }

        [TestMethod]
        public void GetPlayerOutfit_ReturnsCorrectOutfit()
        {
            var outfit = new Outfit { PlayerId = "p1", PlayerName = "Player", OutfitNumber = 1 };
            _state.TryAddOutfit(outfit);

            var result = _state.GetPlayerOutfit("p1", 1);

            Assert.IsNotNull(result);
            Assert.AreEqual(outfit.Id, result.Id);
        }

        [TestMethod]
        public void GetPlayerOutfit_WrongRound_ReturnsNull()
        {
            var outfit = new Outfit { PlayerId = "p1", PlayerName = "Player", OutfitNumber = 1 };
            _state.TryAddOutfit(outfit);

            var result = _state.GetPlayerOutfit("p1", 2);

            Assert.IsNull(result);
        }

        // ------------------------------------------------------------------
        // Scoring
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetOrAddPlayerScore_CreatesNewEntry()
        {
            var score = _state.GetOrAddPlayerScore("p1", "Player One");

            Assert.IsNotNull(score);
            Assert.AreEqual("Player One", score.PlayerName);
            Assert.AreEqual(0, score.TotalPoints);
        }

        [TestMethod]
        public void GetOrAddPlayerScore_ReturnsSameInstance()
        {
            var s1 = _state.GetOrAddPlayerScore("p1", "Player One");
            var s2 = _state.GetOrAddPlayerScore("p1", "Player One");

            Assert.AreSame(s1, s2);
        }

        [TestMethod]
        public void AddPoints_IncrementsScore()
        {
            _state.GetOrAddPlayerScore("p1", "Player One");
            _state.AddPoints("p1", 10);

            Assert.AreEqual(10, _state.PlayerScores["p1"].TotalPoints);
        }

        // ------------------------------------------------------------------
        // Distinctness check
        // ------------------------------------------------------------------

        [TestMethod]
        public void IsDistinctFromAllOutfit1s_DistinctOutfit_ReturnsTrue()
        {
            // Outfit1 has items A, B, C, D
            var (hat1, shirt1, pants1, shoes1) = MakeFourItems();
            var outfit1 = BuildOutfit("p1", 1, hat1, shirt1, pants1, shoes1);
            _state.TryAddOutfit(outfit1);

            // Outfit2 shares 2 items (same as distinctness rule = 2 means max 2 shared)
            var (hat2, shirt2, _, _) = MakeFourItems();
            var outfit2 = BuildOutfit("p1", 2, hat2, shirt2, pants1, shoes1); // shares pants1, shoes1 = 2 shared

            // Default distinctness rule = 2 → max shared = 4 - 2 = 2. Exactly 2 shared is allowed.
            Assert.IsTrue(_state.IsDistinctFromAllOutfit1s(outfit2));
        }

        [TestMethod]
        public void IsDistinctFromAllOutfit1s_TooSimilar_ReturnsFalse()
        {
            var (hat1, shirt1, pants1, shoes1) = MakeFourItems();
            var outfit1 = BuildOutfit("p1", 1, hat1, shirt1, pants1, shoes1);
            _state.TryAddOutfit(outfit1);

            // Outfit2 shares 3 items → violates distinctness rule of 2 (max shared = 2)
            var (hat2, _, _, _) = MakeFourItems();
            var outfit2 = BuildOutfit("p1", 2, hat2, shirt1, pants1, shoes1); // 3 shared

            Assert.IsFalse(_state.IsDistinctFromAllOutfit1s(outfit2));
        }

        [TestMethod]
        public void IsDistinctFromAllOutfit1s_NoOutfit1s_ReturnsTrue()
        {
            var (hat, shirt, pants, shoes) = MakeFourItems();
            var outfit2 = BuildOutfit("p1", 2, hat, shirt, pants, shoes);

            Assert.IsTrue(_state.IsDistinctFromAllOutfit1s(outfit2));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static ClothingItem MakeItem(string creatorId, ClothingType type) =>
            new() { CreatorId = creatorId, CreatorName = "Player", Type = type };

        private static (ClothingItem hat, ClothingItem shirt, ClothingItem pants, ClothingItem shoes) MakeFourItems() =>
            (MakeItem("x", ClothingType.Hat),
             MakeItem("x", ClothingType.Shirt),
             MakeItem("x", ClothingType.Pants),
             MakeItem("x", ClothingType.Shoes));

        private static Outfit BuildOutfit(string playerId, int outfitNum,
            ClothingItem hat, ClothingItem shirt, ClothingItem pants, ClothingItem shoes)
        {
            var o = new Outfit { PlayerId = playerId, PlayerName = "P", OutfitNumber = outfitNum };
            o.Items[ClothingType.Hat] = hat;
            o.Items[ClothingType.Shirt] = shirt;
            o.Items[ClothingType.Pants] = pants;
            o.Items[ClothingType.Shoes] = shoes;
            return o;
        }
    }
}
