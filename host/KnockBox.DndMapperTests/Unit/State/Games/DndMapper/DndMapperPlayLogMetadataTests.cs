using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapper.Services.State.Games.PlayLog;
using KnockBox.DndMapperTests.Helpers;

namespace KnockBox.DndMapperTests.Unit.State.Games.DndMapper
{
    /// <summary>
    /// Verifies <see cref="DndMapperPlayLogMetadata.Build"/> summarizes the session-level
    /// activity (maps drawn, characters tracked, dice rolled) DnD Mapper logs on leave.
    /// </summary>
    [TestClass]
    public class DndMapperPlayLogMetadataTests
    {
        private DndMapperGameEngine _engine = default!;
        private DndMapperGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            // Seed the RNG so the single roll below resolves deterministically.
            (_engine, _state, _host, _) = EngineTestFactory.Build(17);
        }

        [TestMethod]
        public void Build_WithActivity_EmitsSessionSummaryKeys()
        {
            // One map (via the engine verb), one character sheet, and one roll.
            Assert.IsTrue(_engine.CreateMapAsync(_state, _host, "Tavern").TryGetSuccess(out _));

            var sheetId = Guid.NewGuid();
            _state.Sheets = _state.Sheets.SetItem(sheetId, new CharacterSheet
            {
                Id = sheetId,
                CharacterName = "Aragorn",
            });

            var req = new RollRequest(new[] { new DiceTerm(1, 20) }, null, 0, RollMode.Normal, "");
            Assert.IsTrue(_engine.RollAsync(_state, _host, req).TryGetSuccess(out _));

            var metadata = DndMapperPlayLogMetadata.Build(_state, _host.Id);

            Assert.AreEqual("1", metadata["Maps"]);
            Assert.AreEqual("1", metadata["Characters"]);
            Assert.AreEqual("1", metadata["Rolls"]);
            // Host plus any registered players — host-only session here.
            Assert.AreEqual("1", metadata["Players"]);
            // Duration is always present and well-formed (mm:ss for a fresh state).
            Assert.IsTrue(metadata.ContainsKey("Duration"));
        }
    }
}
