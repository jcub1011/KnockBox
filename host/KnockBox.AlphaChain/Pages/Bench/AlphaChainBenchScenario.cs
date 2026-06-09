using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.AlphaChain.Pages.Bench
{
    /// <summary>
    /// Drives a live, throwaway Alpha Chain match for the hidden card bench so player-to-player cards
    /// (Tax Collector, Bounty Hunter, Flak Cannon, Titanium Mirror, …) can be exercised exactly as
    /// they behave in a real game. It is the same ceremony the engine tests perform — construct the
    /// real <see cref="AlphaChainGameEngine"/> over a permissive word list, register N synthetic
    /// players, start, drain the "Get Ready" countdown — wrapped behind a small imperative API the
    /// Razor page calls. Unlike the old bench (a direct <c>IEngineEvaluator.CalculateSteps</c> call,
    /// which never fired the lifecycle hooks reactive cards rely on), every submission runs the full
    /// RoundState pipeline: validation, scoring, the Zero-Point Tax, and every card reaction.
    /// <para>
    /// Eras are configured as long as the rules allow and intermissions are pushed far out so the
    /// match stays in <see cref="AlphaChainGamePhase.Round"/> for any realistic bench session; if it
    /// ever leaves (a very long session, or game over), the page surfaces <see cref="Phase"/> and the
    /// user simply resets.
    /// </para>
    /// </summary>
    internal sealed class AlphaChainBenchScenario : IDisposable
    {
        private readonly AlphaChainGameEngine _engine;
        private readonly IModifierCardFactory _factory;
        private readonly User _host;
        private AlphaChainGameState? _state;

        public AlphaChainBenchScenario(
            IRandomNumberService rng,
            IEngineEvaluator evaluator,
            IModifierCardFactory factory,
            ILogger<AlphaChainGameEngine> engineLogger,
            ILogger<AlphaChainGameState> stateLogger)
        {
            _factory = factory;
            _host = UserFactory.Create("Bench Host", Guid.NewGuid());
            _engine = new AlphaChainGameEngine(
                new BenchWordListService(), rng, evaluator, factory, engineLogger, stateLogger);
        }

        /// <summary>The lowest and highest player counts the engine accepts (mirrors <c>AbstractGameEngine(2, 8)</c>).</summary>
        public const int MinPlayers = 2;
        public const int MaxPlayers = 8;

        /// <summary>True once a scenario has been started and is sitting in a round.</summary>
        public bool IsReady => _state is not null;

        /// <summary>The live inner game state the bench drives, or null before the first reset. Exposed
        /// so the WASM projector can render the bench by projecting this state through the normal path.</summary>
        internal AlphaChainGameState? State => _state;

        /// <summary>The live phase; the page shows a "reset to continue" hint if it ever leaves <c>Round</c>.</summary>
        public AlphaChainGamePhase Phase => _state?.Phase ?? AlphaChainGamePhase.Setup;

        /// <summary>The id of the player whose turn it is, or null before the first reset.</summary>
        public Guid? CurrentPlayerId => _state?.TurnManager.CurrentPlayer;

        /// <summary>Player ids in turn order (P0, P1, …), for the UI to lay out per-player columns.</summary>
        public IReadOnlyList<Guid> TurnOrder => _state?.TurnManager.TurnOrder ?? [];

        /// <summary>The current banned letter (lower-case), or null when no ban is in effect.</summary>
        public char? BannedLetter => _state?.BannedLetter;

        /// <summary>The live per-player state, or null when the id is unknown.</summary>
        public AlphaChainPlayerState? Player(Guid id) =>
            _state is not null && _state.GamePlayers.TryGetValue(id, out var p) ? p : null;

        /// <summary>The just-played word's per-card scoring trace, or null before the first accepted word.</summary>
        public ScoreReplay? LatestReplay => _state?.LatestScoreReplay;

        /// <summary>Every accepted submission this scenario, in chronological order (cleared on reset).</summary>
        public IReadOnlyList<AlphaChainSubmission> SubmissionHistory => _state?.SubmissionHistory ?? [];

        /// <summary>Automated engine effects that fired off-submission (e.g. a reflection landing on a later turn).</summary>
        public IReadOnlyList<EngineEffectEvent> EngineNotices => _state?.LatestEngineNotices ?? [];

        /// <summary>
        /// Tears down any prior match and starts a fresh one with <paramref name="playerCount"/> players
        /// (clamped to the engine's legal range), tutorials off, then ticks past the "Get Ready"
        /// countdown so submissions are accepted immediately. Every player's Engine Bay starts empty so
        /// the bench is a clean slate — build each bay with <see cref="SetBay"/>.
        /// </summary>
        public async Task ResetAsync(int playerCount)
        {
            playerCount = Math.Clamp(playerCount, MinPlayers, MaxPlayers);

            _state?.Dispose();
            _state = null;

            var state = (AlphaChainGameState)(await _engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(UserFactory.Create($"P{i}", Guid.NewGuid()));

            // Tutorials off so the game starts straight into the round loop; eras stretched as far as
            // the rules allow so a bench session never trips an Intermission (card draft) or game over.
            state.UpdateSettings(s => s with
            {
                EnableTutorials = false,
                EraInterval = AlphaChainSettings.MaxEraInterval,
                EraCount = AlphaChainSettings.MaxEraCount,
            });

            await _engine.StartAsync(_host, state);

            if (state.Phase == AlphaChainGamePhase.Countdown)
                _engine.Tick(state.Context!, state.SubPhaseEndTime.AddSeconds(1));

            // Clean slate: drop anything an opening deal may have placed so each bay is built explicitly.
            state.Execute(() =>
            {
                foreach (var player in state.GamePlayers.Values)
                    player.EngineBay.Clear();
            });

            _state = state;
        }

        /// <summary>Sets (or clears, with null) the match's banned letter so taxed-word reactions can be staged.</summary>
        public void SetBannedLetter(char? letter)
        {
            if (_state is not { } state) return;
            char? normalized = letter is { } c && char.IsLetter(c) ? char.ToLowerInvariant(c) : null;
            state.Execute(() => state.BannedLetter = normalized);
        }

        /// <summary>Rebuilds <paramref name="playerId"/>'s Engine Bay (left → right pipeline order) from <paramref name="cards"/>.</summary>
        public void SetBay(Guid playerId, IReadOnlyList<ModifierId> cards)
        {
            if (_state is not { } state || !state.GamePlayers.ContainsKey(playerId)) return;

            // A display context is enough to construct cards — they never dereference it at creation.
            var displayContext = new EngineEvaluationContext(string.Empty, [], []);
            state.Execute(() =>
            {
                var bay = state.GamePlayers[playerId].EngineBay;
                bay.Clear();
                foreach (var id in cards)
                    bay.Add(_factory.CreateCard(displayContext, id));
            });
        }

        /// <summary>Sets a player's running score directly, to stage leader/relative-score scenarios
        /// (Bounty Hunter docks the leader; Flak Cannon shaves higher-scoring opponents).</summary>
        public void SetScore(Guid playerId, int score)
        {
            if (_state is not { } state || !state.GamePlayers.ContainsKey(playerId)) return;
            state.Execute(() => state.GamePlayers[playerId].Score = Math.Max(0, score));
        }

        /// <summary>
        /// Submits <paramref name="word"/> for the current player and returns the engine's typed result.
        /// When <paramref name="remainingSeconds"/> is given, the submission clock is positioned so the
        /// submitter has exactly that many seconds left (drives Chrono Syphon); otherwise the wall clock
        /// is used. The engine advances the turn on an accepted word.
        /// </summary>
        public Task<ValueResult<SubmitWordResult>> SubmitAsync(string word, int? remainingSeconds = null)
        {
            if (_state is not { } state || state.TurnManager.CurrentPlayer is not { } submitter)
                return Task.FromResult(ValueResult<SubmitWordResult>.FromError("No active scenario."));

            DateTimeOffset? now = remainingSeconds is { } secs
                ? state.PhaseEndTime.AddSeconds(-secs)
                : null;

            return _engine.SubmitWordAsync(submitter, word, state, now);
        }

        /// <summary>Advances the active seat without playing a word, so the user can hand the turn to a
        /// chosen submitter (the engine only accepts a word from the current player).</summary>
        public Task<Result> SkipTurnAsync()
        {
            if (_state is not { } state || state.TurnManager.CurrentPlayer is not { } current)
                return Task.FromResult(Result.FromError("No active scenario."));
            return _engine.AdvanceTurnAsync(current, state);
        }

        public void Dispose()
        {
            _state?.Dispose();
            _state = null;
        }
    }
}
