using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM.States;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDressTests.Unit.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Tests FSM creation, state entry, and nominal phase transitions for Drawn To Dress.
    /// </summary>
    [TestClass]
    public class DrawnToDressFsmTests
    {
        private Mock<ILogger<DrawnToDressGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DrawnToDressGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private DrawnToDressGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<DrawnToDressGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DrawnToDressGameState>>();
            _host = new User("Host", "host1");

            _engine = new DrawnToDressGameEngine(
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        // ── FSM creation ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task CreateStateAsync_InitializesFsmInLobbyState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (DrawnToDressGameState)result.Value!;
            Assert.IsNotNull(state.Context);
            Assert.IsNotNull(state.Context.Fsm);
            Assert.IsInstanceOfType<LobbyState>(state.Context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public async Task StartAsync_TransitionsFromLobbyToDrawingPhase()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;

            var startResult = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)startResult.IsSuccess);
            // For the default random theme source the chain is:
            // ThemeSelectionState → DrawingRoundState
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
            Assert.IsInstanceOfType<DrawingRoundState>(state.Context!.Fsm.CurrentState);
            Assert.IsNotNull(state.PhaseDeadlineUtc);
        }

        // ── Command dispatch ──────────────────────────────────────────────────

        [TestMethod]
        public async Task ProcessCommand_UnknownCommand_DoesNotTransition()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            var initialState = context.Fsm.CurrentState;

            // MarkReadyCommand is not handled in LobbyState → should be a no-op.
            _engine.ProcessCommand(context, new MarkReadyCommand(_host.Id));

            Assert.AreSame(initialState, context.Fsm.CurrentState);
        }

        // ── Nominal phase flow ────────────────────────────────────────────────

        [TestMethod]
        public async Task DrawingRoundState_OnTimerExpiry_TransitionsToOutfitBuilding()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            // Simulate timer expiry by ticking far into the future.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // PoolRevealState immediately chains to OutfitBuildingState.
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);
        }

        [TestMethod]
        public async Task DrawingRoundState_AllPlayersReady_TransitionsEarly()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Register a player and mark them ready.
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            // All one player is ready, so transition should fire.
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task OutfitBuildingState_OnTimerExpiry_TransitionsToCustomization()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Skip to OutfitBuildingState.
            context.Fsm.TransitionTo(context, new OutfitBuildingState());
            Assert.AreEqual(GamePhase.OutfitBuilding, state.Phase);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitCustomizationState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitCustomization, state.Phase);
        }

        [TestMethod]
        public async Task OutfitCustomizationState_OnTimerExpiry_TransitionsToVotingSetup()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // No players → no distinctness conflicts, so goes straight to VotingRoundSetupState
            // which chains to VotingMatchupState.
            context.Fsm.TransitionTo(context, new OutfitCustomizationState());
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Voting, state.Phase);
        }

        [TestMethod]
        public async Task VotingMatchupState_OnTimerExpiry_TransitionsToRoundResults()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            context.Fsm.TransitionTo(context, new VotingRoundSetupState());
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.VotingRoundResults, state.Phase);
        }

        [TestMethod]
        public async Task VotingRoundResultsState_AllRoundsComplete_TransitionsToFinalResults()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Pre-fill all configured voting rounds so the next MarkReady ends the game.
            for (int i = 0; i < state.Config.VotingRounds; i++)
                state.VotingRounds.Add(new() { RoundNumber = i + 1 });

            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            // Add a player and have them acknowledge the results.
            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            Assert.IsInstanceOfType<FinalResultsState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Results, state.Phase);
        }

        [TestMethod]
        public async Task VotingRoundResultsState_MoreRoundsRemain_TransitionsToNextSetup()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Leave fewer completed rounds than configured (default 3).
            // Add 2 rounds, so one more should remain.
            state.VotingRounds.Add(new() { RoundNumber = 1 });
            state.VotingRounds.Add(new() { RoundNumber = 2 });

            context.Fsm.TransitionTo(context, new VotingRoundResultsState());

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            _engine.ProcessCommand(context, new MarkReadyCommand("p1"));

            // VotingRoundSetupState chains immediately to VotingMatchupState.
            Assert.IsInstanceOfType<VotingMatchupState>(context.Fsm.CurrentState);
        }

        // ── Coin flip ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task CoinFlipState_OnEnter_TransitionsToRoundResults()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            state.VotingRounds.Add(new() { RoundNumber = 1 });
            context.Fsm.TransitionTo(context, new CoinFlipState());

            Assert.IsInstanceOfType<VotingRoundResultsState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.VotingRoundResults, state.Phase);
            // PendingCoinFlipMatchupId is cleared on exit from CoinFlipState.
            Assert.IsNull(state.PendingCoinFlipMatchupId);
        }

        // ── Pause / abandon ───────────────────────────────────────────────────

        [TestMethod]
        public async Task PauseGame_FromDrawingRound_TransitionsToPausedState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);

            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));

            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Paused, state.Phase);
        }

        [TestMethod]
        public async Task ResumeGame_FromPaused_ReturnsToPreviousState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Pause from the drawing round.
            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));
            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);

            // Resume — should go back to DrawingRoundState.
            _engine.ProcessCommand(context, new ResumeGameCommand(_host.Id));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
        }

        [TestMethod]
        public async Task ResumeGame_ByNonHost_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));
            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);

            // Non-host attempts to resume.
            _engine.ProcessCommand(context, new ResumeGameCommand("nonhost_id"));

            Assert.IsInstanceOfType<PausedState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task AbandonGame_FromDrawingRound_TransitionsToAbandonedState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            _engine.ProcessCommand(context, new AbandonGameCommand(_host.Id));

            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Abandoned, state.Phase);
        }

        [TestMethod]
        public async Task AbandonGame_FromPausedState_TransitionsToAbandonedState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            _engine.ProcessCommand(context, new PauseGameCommand(_host.Id));
            _engine.ProcessCommand(context, new AbandonGameCommand(_host.Id));

            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Abandoned, state.Phase);
        }

        [TestMethod]
        public async Task AbandonGame_FromFinalResults_TransitionsToAbandonedState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            context.Fsm.TransitionTo(context, new FinalResultsState());
            Assert.AreEqual(GamePhase.Results, state.Phase);

            _engine.ProcessCommand(context, new AbandonGameCommand(_host.Id));

            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Abandoned, state.Phase);
        }

        [TestMethod]
        public async Task AbandonedState_AcceptsNoFurtherCommands()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            _engine.ProcessCommand(context, new AbandonGameCommand(_host.Id));
            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);

            // Any subsequent command should be a no-op.
            _engine.ProcessCommand(context, new StartGameCommand(_host.Id));
            Assert.IsInstanceOfType<AbandonedState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Abandoned, state.Phase);
        }

        // ── Theme selection ───────────────────────────────────────────────────

        [TestMethod]
        public async Task LobbyState_StartGameCommand_ByNonHost_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            // Attempt to start from a non-host player ID.
            _engine.ProcessCommand(context, new StartGameCommand("nonhost_id"));

            // Should remain in lobby.
            Assert.IsInstanceOfType<LobbyState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public async Task ThemeSelectionState_HostPick_WaitsForSelectThemeCommand()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Force back to ThemeSelectionState with HostPick mode.
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.HostPick;
            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.ThemeSelection, state.Phase);

            // Now host selects a theme.
            _engine.ProcessCommand(context, new SelectThemeCommand(_host.Id, "retro_futurism"));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Drawing, state.Phase);
            Assert.AreEqual("retro_futurism", state.CurrentTheme?.Id);
        }

        [TestMethod]
        public async Task ThemeSelectionState_Random_ImmediatelySelectsThemeAndAdvancesToDrawing()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.Random;
            var context = state.Context!;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Random source → immediately transitions to DrawingRoundState on entry.
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsFalse(string.IsNullOrEmpty(state.CurrentTheme.Id));
        }

        [TestMethod]
        public async Task ThemeSelectionState_PlayerWritten_WaitsForAllSubmissions()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.PlayerWritten;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);

            // Only the first player submits — should stay in ThemeSelectionState.
            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p1", "Sci-Fi Noir"));
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.IsNull(state.CurrentTheme);
        }

        [TestMethod]
        public async Task ThemeSelectionState_PlayerWritten_AllPlayersSubmit_SelectsThemeAndAdvances()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.PlayerWritten;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p1", "Sci-Fi Noir"));
            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p2", "Medieval Fantasy"));

            // Both submitted → should advance to DrawingRoundState with one of their themes.
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.IsNotNull(state.CurrentTheme);
            var validThemes = new[] { "Sci-Fi Noir", "Medieval Fantasy" };
            CollectionAssert.Contains(validThemes, state.CurrentTheme.Id);
        }

        [TestMethod]
        public async Task ThemeSelectionState_PlayerWritten_SubmissionsStoredInGameState()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.PlayerWritten;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());
            _engine.ProcessCommand(context, new SubmitPlayerThemeCommand("p1", "Cosmic Horror"));

            Assert.IsTrue(state.PlayerThemeSubmissions.ContainsKey("p1"));
            Assert.AreEqual("Cosmic Horror", state.PlayerThemeSubmissions["p1"]);
            Assert.IsFalse(state.PlayerThemeSubmissions.ContainsKey("p2"));
        }

        [TestMethod]
        public async Task ThemeSelectionState_RandomVoting_PopulatesCandidatesOnEntry()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.RandomVoting;
            state.Config.RandomVotingCandidateCount = 3;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.AreEqual(3, state.ThemeCandidates.Count);
            // Candidates should be distinct.
            var ids = state.ThemeCandidates.Select(t => t.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
        }

        [TestMethod]
        public async Task ThemeSelectionState_RandomVoting_AllPlayersVote_SelectsWinnerAndAdvances()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.RandomVoting;
            state.Config.RandomVotingCandidateCount = 3;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };
            state.GamePlayers["p3"] = new() { PlayerId = "p3" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());
            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);

            var winningCandidate = state.ThemeCandidates[0];

            // Two players vote for the first candidate; one for another.
            _engine.ProcessCommand(context, new VoteForThemeCommand("p1", winningCandidate.Id));
            _engine.ProcessCommand(context, new VoteForThemeCommand("p2", winningCandidate.Id));
            _engine.ProcessCommand(context, new VoteForThemeCommand("p3", state.ThemeCandidates[1].Id));

            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
            Assert.AreEqual(winningCandidate.Id, state.CurrentTheme?.Id);
        }

        [TestMethod]
        public async Task ThemeSelectionState_RandomVoting_WaitsForAllVotes()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.RandomVoting;
            state.Config.RandomVotingCandidateCount = 3;

            state.GamePlayers["p1"] = new() { PlayerId = "p1" };
            state.GamePlayers["p2"] = new() { PlayerId = "p2" };

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Only one player votes — should stay in ThemeSelectionState.
            _engine.ProcessCommand(context, new VoteForThemeCommand("p1", state.ThemeCandidates[0].Id));

            Assert.IsInstanceOfType<ThemeSelectionState>(context.Fsm.CurrentState);
            Assert.IsNull(state.CurrentTheme);
        }

        // ── Announcement timing ───────────────────────────────────────────────

        [TestMethod]
        public async Task ThemeAnnouncement_BeforeDrawing_ThemeRevealedAfterSelection()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeAnnouncement.BeforeDrawing;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Theme should be revealed immediately in BeforeDrawing mode.
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsTrue(state.ThemeRevealedToPlayers);
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeAnnouncement_AfterDrawing_ThemeNotRevealedDuringDrawing()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeAnnouncement.AfterDrawing;

            context.Fsm.TransitionTo(context, new ThemeSelectionState());

            // Theme selected but NOT yet revealed to players.
            Assert.IsNotNull(state.CurrentTheme);
            Assert.IsFalse(state.ThemeRevealedToPlayers);
            Assert.IsInstanceOfType<DrawingRoundState>(context.Fsm.CurrentState);
        }

        [TestMethod]
        public async Task ThemeAnnouncement_AfterDrawing_ThemeRevealedAfterDrawingCompletes()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            state.Config.ThemeSource = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeSource.Random;
            state.Config.ThemeAnnouncement = KnockBox.Services.State.Games.DrawnToDress.Data.ThemeAnnouncement.AfterDrawing;

            // Enter ThemeSelectionState → auto-selects theme but does not reveal.
            context.Fsm.TransitionTo(context, new ThemeSelectionState());
            Assert.IsFalse(state.ThemeRevealedToPlayers);

            // Simulate drawing timer expiry → PoolRevealState → OutfitBuildingState.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            // After drawing completes (in PoolRevealState) the theme should be revealed.
            Assert.IsTrue(state.ThemeRevealedToPlayers);
            Assert.IsInstanceOfType<OutfitBuildingState>(context.Fsm.CurrentState);
        }

        // ── Same theme for both outfits ────────────────────────────────────────

        [TestMethod]
        public async Task BothOutfits_SameThemeUsedThroughoutSession()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            // Default: Random source, BeforeDrawing.
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            var selectedTheme = state.CurrentTheme;
            Assert.IsNotNull(selectedTheme, "Theme should be selected after game start.");

            // Advance through drawing and outfit building.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1)); // Drawing → PoolReveal → OutfitBuilding
            Assert.AreEqual(selectedTheme, state.CurrentTheme,
                "Theme must remain the same after drawing phase.");

            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1)); // OutfitBuilding → OutfitCustomization
            Assert.AreEqual(selectedTheme, state.CurrentTheme,
                "Theme must remain the same during outfit building.");
        }

        // ── Outfit distinctness resolution ────────────────────────────────────

        [TestMethod]
        public async Task OutfitCustomizationState_WithConflict_GoesToDistinctnessResolution()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            await _engine.StartAsync(_host, state);
            var context = state.Context!;

            // Set up two players sharing the same item.
            var sharedId = Guid.NewGuid();
            state.GamePlayers["p1"] = new()
            {
                PlayerId = "p1",
                SubmittedOutfit = new()
                {
                    PlayerId = "p1",
                    SelectedItemsByType = new() { ["hat"] = sharedId },
                }
            };
            state.GamePlayers["p2"] = new()
            {
                PlayerId = "p2",
                SubmittedOutfit = new()
                {
                    PlayerId = "p2",
                    SelectedItemsByType = new() { ["hat"] = sharedId },
                }
            };

            context.Fsm.TransitionTo(context, new OutfitCustomizationState());
            // Timer expires with all outfits already submitted and a conflict present.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddHours(1));

            Assert.IsInstanceOfType<OutfitDistinctnessResolutionState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.OutfitDistinctnessResolution, state.Phase);
        }

        // ── Lobby: host permissions ───────────────────────────────────────────

        [TestMethod]
        public async Task LobbyState_UpdateConfigCommand_ByHost_UpdatesConfig()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            var updatedConfig = new KnockBox.Services.State.Games.DrawnToDress.Data.DrawnToDressConfig
            {
                DrawingTimeSec = 240,
            };

            _engine.ProcessCommand(context, new UpdateConfigCommand(_host.Id, updatedConfig));

            // Should remain in lobby and config should be updated.
            Assert.IsInstanceOfType<LobbyState>(context.Fsm.CurrentState);
            Assert.AreEqual(240, state.Config.DrawingTimeSec);
        }

        [TestMethod]
        public async Task LobbyState_UpdateConfigCommand_ByNonHost_IsRejected()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;
            int originalDrawingTime = state.Config.DrawingTimeSec;

            var updatedConfig = new KnockBox.Services.State.Games.DrawnToDress.Data.DrawnToDressConfig
            {
                DrawingTimeSec = 999,
            };

            _engine.ProcessCommand(context, new UpdateConfigCommand("nonhost_id", updatedConfig));

            // Config should not have changed.
            Assert.AreEqual(originalDrawingTime, state.Config.DrawingTimeSec);
        }

        [TestMethod]
        public async Task LobbyState_UpdateConfigCommand_NormalizesInvalidValues()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            var invalidConfig = new KnockBox.Services.State.Games.DrawnToDress.Data.DrawnToDressConfig
            {
                DrawingTimeSec = 1,     // below minimum of 30
                VotingRounds = 0,       // below minimum of 1
                BonusPointsForCompleteOutfit = -10, // negative
            };

            _engine.ProcessCommand(context, new UpdateConfigCommand(_host.Id, invalidConfig));

            Assert.AreEqual(30, state.Config.DrawingTimeSec);
            Assert.AreEqual(1, state.Config.VotingRounds);
            Assert.AreEqual(0, state.Config.BonusPointsForCompleteOutfit);
        }

        [TestMethod]
        public async Task LobbyState_StartGameCommand_ByNonHost_DoesNotTransition()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DrawnToDressGameState)stateResult.Value!;
            var context = state.Context!;

            _engine.ProcessCommand(context, new StartGameCommand("notthehost"));

            Assert.IsInstanceOfType<LobbyState>(context.Fsm.CurrentState);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }
    }
}
