using System.Diagnostics;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Dictionary;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Tracery.Tests.Helpers;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tracery.Tests.Unit.Logic
{
    /// <summary>
    /// Performance sanity for the solver/generator (Milestone 08, GDD §9). Generate-and-test runs
    /// the solver many times per accepted board, so its cost is the game's dominant runtime risk.
    /// The bounds here are deliberately generous — they are not micro-benchmarks. The failure mode
    /// they guard against is a regression in prefix pruning: without it an 8×8 DFS is astronomically
    /// large and never returns, so it overruns any finite bound by orders of magnitude. A warm trie
    /// is built once in <see cref="ClassInit"/> so the ~386k-word load never lands inside a timed region.
    /// </summary>
    [TestClass]
    public class SolverPerformanceTests
    {
        // The settings panel clamps grid dimensions to [3, 8], so 8×8 (64 cells) is the largest
        // board the game can ever ask the solver to chew through.
        private const int LargestGridSide = 8;

        private static WordListService _wordList = default!;
        private static TracerySolver _solver = default!;

        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            _wordList = new WordListService(NullLogger<WordListService>.Instance);
            // Build the trie once here (outside any timed region) at the engine's global floor.
            _solver = new TracerySolver(TraceryTrie.BuildFrom(_wordList, minWordLength: 3));
        }

        // ── Single solve on the largest grid ────────────────────────────────

        [TestMethod]
        public void Solve_LargestGrid_CompletesWellWithinBound()
        {
            // A dense, plausible board (no deliberately dead letters) — the realistic worst case
            // the generator hands the solver every attempt.
            var grid = FilledGrid("retinaslopedcugmabrowthykfinvexjadpoquzelb");

            // Warm the JIT with one untimed solve, then measure.
            _solver.Solve(grid, minWordLength: 4);

            var sw = Stopwatch.StartNew();
            var found = _solver.Solve(grid, minWordLength: 4);
            sw.Stop();

            Assert.IsTrue(found.Count > 0, "An 8×8 English-letter board should yield findable words.");
            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(2),
                $"Solving an 8×8 board took {sw.ElapsedMilliseconds} ms — prefix pruning may have regressed.");
        }

        // ── Generate-and-test on the largest grid ───────────────────────────

        [TestMethod]
        public void Generate_LargestGrid_ProducesPassingBoardWithinBound()
        {
            var generator = new GridGenerator(_solver, new RandomNumberService(), _wordList, NullLogger.Instance);
            var settings = new TracerySettings() with { GridWidth = LargestGridSide, GridHeight = LargestGridSide };

            // Warm-up generation (untimed) so JIT of the generate/solve loop is excluded.
            Assert.IsTrue(generator.Generate(settings).IsSuccess);

            // Generate several full boards under one bound to amortise scheduling noise. Each board
            // runs the solver up to MaxGenerationAttempts (default 50) times, so this is the real
            // per-round cost the host pays, several rounds over.
            const int boards = 3;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < boards; i++)
            {
                var result = generator.Generate(settings);
                Assert.IsTrue(result.IsSuccess, $"Board {i} failed to generate at {LargestGridSide}×{LargestGridSide}.");
                Assert.IsTrue(result.Value.FindableWords.Keys.Any(w => w.Length >= 7),
                    "Every accepted/seeded board must still carry a big find.");
            }
            sw.Stop();

            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(5),
                $"Generating {boards} 8×8 boards took {sw.ElapsedMilliseconds} ms — generate-and-test may have regressed.");
        }

        // ── Each pool's trie is built once, cached on the singleton engine ──

        [TestMethod]
        public void Engine_BuildsEachPoolTrieOnce_AcrossManySolversAndRounds()
        {
            // A real word service so the trie genuinely builds (and logs) on first use.
            var engineLogger = new ListLogger<TraceryGameEngine>();
            var engine = new TraceryGameEngine(
                _wordList, new RandomNumberService(), engineLogger, NullLogger<TraceryGameState>.Instance);

            // Hammer every entry point that could trigger a build: repeated solver/generator
            // requests, plus several full rounds (EnterPlaying generates a board each time). The
            // default settings split board generation (ReducedDictionary) from answer validation
            // (FullDictionary), so a round touches both pools — each must still build at most once.
            for (int i = 0; i < 5; i++)
            {
                _ = engine.GetSolver(WordPoolMode.FullDictionary);
                _ = engine.GetGenerator(WordPoolMode.FullDictionary);
            }

            var host = UserFactory.Create("Host", Guid.NewGuid());
            var created = engine.CreateStateAsync(host).GetAwaiter().GetResult();
            Assert.IsTrue(created.TryGetSuccess(out var s));
            var state = (TraceryGameState)s!;
            state.UpdateSettings(x => x with
            {
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5)
            });
            engine.StartAsync(host, state).GetAwaiter().GetResult();
            for (int round = 0; round < 3; round++)
                state.Execute(() => engine.EnterPlaying(state));

            // Built once per distinct pool and cached thereafter — never rebuilt per call or round.
            Assert.AreEqual(1, engineLogger.CountContaining("Building Tracery dictionary trie for FullDictionary"),
                "The full-dictionary trie must be built exactly once and cached on the singleton engine.");
            Assert.AreEqual(1, engineLogger.CountContaining("Building Tracery dictionary trie for ReducedDictionary"),
                "The reduced-dictionary trie must be built exactly once and cached on the singleton engine.");
            Assert.AreEqual(2, engineLogger.CountContaining("Building Tracery dictionary trie"),
                "Only the two pools the default settings touch should ever build a trie.");
        }

        // Cycles the seed string to exactly the 64 cells of an 8×8 grid so the board is dense and
        // deterministic without hand-counting letters.
        private static Grid FilledGrid(string seed)
        {
            const int area = LargestGridSide * LargestGridSide;
            char[] letters = new char[area];
            for (int i = 0; i < area; i++)
                letters[i] = seed[i % seed.Length];
            return new Grid(LargestGridSide, LargestGridSide, letters);
        }
    }
}
