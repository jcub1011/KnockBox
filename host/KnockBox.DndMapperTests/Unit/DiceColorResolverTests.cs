using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapperTests.Helpers;
using System.Collections.Immutable;

namespace KnockBox.DndMapperTests.Unit
{
    [TestClass]
    public class DiceColorResolverTests
    {
        [TestMethod]
        public void Resolve_Host_ReturnsGold()
        {
            var (_, state, host, _) = EngineTestFactory.Build();
            Assert.AreEqual(DiceColorResolver.HostGold, DiceColorResolver.Resolve(state, host.Id));
        }

        [TestMethod]
        public void Resolve_PlayerWithToken_ReturnsTokenColor()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var player = EngineTestFactory.RegisterPlayer(state);

            var map = new Map
            {
                Id = Guid.NewGuid(),
                Tokens = ImmutableList.Create(new Token
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = player.Id,
                    Color = "#abcdef",
                }),
            };
            state.Maps = ImmutableList.Create(map);

            Assert.AreEqual("#abcdef", DiceColorResolver.Resolve(state, player.Id));
        }

        [TestMethod]
        public void Resolve_PlayerWithoutToken_ReturnsStableHexFromUserId()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var player = EngineTestFactory.RegisterPlayer(state);

            var first = DiceColorResolver.Resolve(state, player.Id);
            var second = DiceColorResolver.Resolve(state, player.Id);

            Assert.AreEqual(first, second);
            StringAssert.Matches(first, new System.Text.RegularExpressions.Regex("^#[0-9a-f]{6}$"));
        }

        [TestMethod]
        public void Resolve_NonHostWithoutToken_DoesNotReturnGold()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var player = EngineTestFactory.RegisterPlayer(state);
            Assert.AreNotEqual(DiceColorResolver.HostGold, DiceColorResolver.Resolve(state, player.Id));
        }

        [TestMethod]
        public void Resolve_PlayerWithEmptyTokenColor_FallsBackToHash()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var player = EngineTestFactory.RegisterPlayer(state);

            var map = new Map
            {
                Id = Guid.NewGuid(),
                Tokens = ImmutableList.Create(new Token
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = player.Id,
                    Color = "",
                }),
            };
            state.Maps = ImmutableList.Create(map);

            var resolved = DiceColorResolver.Resolve(state, player.Id);
            StringAssert.Matches(resolved, new System.Text.RegularExpressions.Regex("^#[0-9a-f]{6}$"));
        }

        [TestMethod]
        [DataRow("#zzz")]
        [DataRow("#linkedin")]
        [DataRow("#FFGG00")]
        [DataRow("#1234567")]
        [DataRow("FFFFFF")]
        [DataRow("#")]
        public void Resolve_PlayerWithInvalidHexTokenColor_FallsBackToHash(string badColor)
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var player = EngineTestFactory.RegisterPlayer(state);

            var map = new Map
            {
                Id = Guid.NewGuid(),
                Tokens = ImmutableList.Create(new Token
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = player.Id,
                    Color = badColor,
                }),
            };
            state.Maps = ImmutableList.Create(map);

            var resolved = DiceColorResolver.Resolve(state, player.Id);
            Assert.AreNotEqual(badColor, resolved);
            StringAssert.Matches(resolved, new System.Text.RegularExpressions.Regex("^#[0-9a-f]{6}$"));
        }

        [TestMethod]
        [DataRow("#fff")]
        [DataRow("#ffff")]
        [DataRow("#ffffff")]
        [DataRow("#FFFFFFFF")]
        [DataRow("#aBcDeF")]
        public void Resolve_PlayerWithValidHexTokenColor_ReturnsTokenColor(string goodColor)
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var player = EngineTestFactory.RegisterPlayer(state);

            var map = new Map
            {
                Id = Guid.NewGuid(),
                Tokens = ImmutableList.Create(new Token
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = player.Id,
                    Color = goodColor,
                }),
            };
            state.Maps = ImmutableList.Create(map);

            Assert.AreEqual(goodColor, DiceColorResolver.Resolve(state, player.Id));
        }

        // ── ResolveForToken ─────────────────────────────────────────────

        [TestMethod]
        public void ResolveForToken_LinkedSheetWithColor_ReturnsSheetColor()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var sheetId = Guid.NewGuid();
            var tokenId = Guid.NewGuid();

            state.Sheets = state.Sheets.SetItem(sheetId, new CharacterSheet
            {
                Id = sheetId,
                CharacterName = "Sheet color wins",
                Color = "#abcdef",
            });
            state.Maps = ImmutableList.Create(new Map
            {
                Id = Guid.NewGuid(),
                Tokens = ImmutableList.Create(new Token
                {
                    Id = tokenId,
                    SheetId = sheetId,
                    Color = "#111111", // token's own color shouldn't win
                }),
            });

            Assert.AreEqual("#abcdef", DiceColorResolver.ResolveForToken(state, tokenId));
        }

        [TestMethod]
        public void ResolveForToken_LinkedSheetWithoutColor_FallsBackToTokenColor()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var sheetId = Guid.NewGuid();
            var tokenId = Guid.NewGuid();

            state.Sheets = state.Sheets.SetItem(sheetId, new CharacterSheet
            {
                Id = sheetId,
                CharacterName = "Legacy sheet",
                Color = "", // empty = "fall back to token color"
            });
            state.Maps = ImmutableList.Create(new Map
            {
                Id = Guid.NewGuid(),
                Tokens = ImmutableList.Create(new Token
                {
                    Id = tokenId,
                    SheetId = sheetId,
                    Color = "#abcdef",
                }),
            });

            Assert.AreEqual("#abcdef", DiceColorResolver.ResolveForToken(state, tokenId));
        }

        [TestMethod]
        public void ResolveForToken_NoLinkedSheet_FallsBackToTokenColor()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var tokenId = Guid.NewGuid();

            state.Maps = ImmutableList.Create(new Map
            {
                Id = Guid.NewGuid(),
                Tokens = ImmutableList.Create(new Token
                {
                    Id = tokenId,
                    SheetId = null,
                    Color = "#abcdef",
                }),
            });

            Assert.AreEqual("#abcdef", DiceColorResolver.ResolveForToken(state, tokenId));
        }

        [TestMethod]
        public void ResolveForToken_TokenWithEmptyColor_FallsBackToDeterministicHash()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var tokenId = Guid.NewGuid();

            state.Maps = ImmutableList.Create(new Map
            {
                Id = Guid.NewGuid(),
                Tokens = ImmutableList.Create(new Token
                {
                    Id = tokenId,
                    Color = "",
                }),
            });

            var first = DiceColorResolver.ResolveForToken(state, tokenId);
            var second = DiceColorResolver.ResolveForToken(state, tokenId);
            Assert.AreEqual(first, second);
            StringAssert.Matches(first, new System.Text.RegularExpressions.Regex("^#[0-9a-f]{6}$"));
        }

        [TestMethod]
        public void ResolveForToken_UnknownTokenId_FallsBackToDeterministicHashOnId()
        {
            var (_, state, _, _) = EngineTestFactory.Build();
            var unknown = Guid.NewGuid();

            // No maps, no tokens — must still produce a stable color from the id.
            var first = DiceColorResolver.ResolveForToken(state, unknown);
            var second = DiceColorResolver.ResolveForToken(state, unknown);
            Assert.AreEqual(first, second);
            StringAssert.Matches(first, new System.Text.RegularExpressions.Regex("^#[0-9a-f]{6}$"));
        }
    }
}
