using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Dictionary;
using KnockBox.Tracery.Tests.Helpers;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tracery.Tests.Unit.Logic
{
    [TestClass]
    public class GridGeneratorTests
    {
        // Cumulative-weight bands a draw must land in to produce a given letter (see
        // LetterDistribution). Used to build deterministic letter sequences below.
        //   a=0  c=11  j=47  r=73  s=79  t=84
        private const int A = 0, C = 11, J = 47, R = 73, S = 79, T = 84;

        private static GridGenerator MakeGenerator(IRandomNumberService rng, params string[] dictWords)
            => new(new TracerySolver(TraceryTrie.FromWords(dictWords)),
                   rng, new WordListService(NullLogger<WordListService>.Instance), NullLogger.Instance);

        private static TracerySettings Settings(int w, int h) =>
            new TracerySettings() with { GridWidth = w, GridHeight = h };

        // ── Generate-and-test: accepted boards clear the bar ─────────────────────

        [TestMethod]
        public void Generate_AcceptedBoard_ClearsBar_AndDoesNotFallBack()
        {
            // 2×2 board "cars": c(0) a(1) / r(2) s(3). Every cell is mutually adjacent.
            var rng = new SequentialRng(C, A, R, S);
            var gen = MakeGenerator(rng, "car", "cars");
            var settings = Settings(2, 2) with
            {
                MinWordLength = 3,
                MinFindableWords = 1,
                MinLongWordLength = 3,
                RequireRareLetterWord = false,
                MaxGenerationAttempts = 1,
            };

            var result = gen.Generate(settings);

            Assert.IsTrue(result.IsSuccess);
            var board = result.Value;
            Assert.IsFalse(board.UsedFallback);
            Assert.IsTrue(board.FindableWords.ContainsKey("car"));
            Assert.IsTrue(board.FindableWords.ContainsKey("cars"));
            Assert.IsTrue(board.FindableWords.Count >= 1);
        }

        [TestMethod]
        public void Generate_PreservesLowercaseKeys()
        {
            var rng = new SequentialRng(C, A, R, S);
            var gen = MakeGenerator(rng, "car", "cars");
            var settings = Settings(2, 2) with
            {
                MinWordLength = 3, MinFindableWords = 1, MinLongWordLength = 3,
                RequireRareLetterWord = false, MaxGenerationAttempts = 1,
            };

            var board = gen.Generate(settings).Value;

            foreach (var key in board.FindableWords.Keys)
                Assert.AreEqual(key.ToLowerInvariant(), key, "Findable-word keys must be lowercase.");
        }

        [TestMethod]
        public void Generate_DeterministicRng_IsReproducible()
        {
            var settings = Settings(2, 2) with
            {
                MinWordLength = 3, MinFindableWords = 1, MinLongWordLength = 3,
                RequireRareLetterWord = false, MaxGenerationAttempts = 1,
            };

            var a = MakeGenerator(new SequentialRng(C, A, R, S), "car", "cars").Generate(settings).Value;
            var b = MakeGenerator(new SequentialRng(C, A, R, S), "car", "cars").Generate(settings).Value;

            Assert.AreEqual(a.Grid.CellCount, b.Grid.CellCount);
            for (int i = 0; i < a.Grid.CellCount; i++)
                Assert.AreEqual(a.Grid[i], b.Grid[i], $"Cell {i} differs between identical-seed runs.");
        }

        // ── Rare-letter guarantee ────────────────────────────────────────────────

        [TestMethod]
        public void Generate_WhenRareRequired_AcceptedBoardHasRareLetterWord()
        {
            // 2×2 "jart": j(0) a(1) / r(2) t(3). "jar" carries the rare 'j'.
            var rng = new SequentialRng(J, A, R, T);
            var gen = MakeGenerator(rng, "jar", "rat", "tar");
            var settings = Settings(2, 2) with
            {
                MinWordLength = 3, MinFindableWords = 1, MinLongWordLength = 3,
                RequireRareLetterWord = true, MaxGenerationAttempts = 1,
            };

            var result = gen.Generate(settings);

            Assert.IsTrue(result.IsSuccess);
            var board = result.Value;
            Assert.IsFalse(board.UsedFallback);
            Assert.IsTrue(board.FindableWords.Keys.Any(w => w.Any(LetterDistribution.IsRare)),
                "An accepted rare-required board must have a findable rare-letter word.");
        }

        [TestMethod]
        public void Generate_WhenRareNotRequired_AcceptsBoardWithoutRareWord()
        {
            var rng = new SequentialRng(C, A, R, S);
            var gen = MakeGenerator(rng, "car", "cars");
            var settings = Settings(2, 2) with
            {
                MinWordLength = 3, MinFindableWords = 1, MinLongWordLength = 3,
                RequireRareLetterWord = false, MaxGenerationAttempts = 1,
            };

            var board = gen.Generate(settings).Value;

            Assert.IsFalse(board.UsedFallback);
            Assert.IsFalse(board.FindableWords.Keys.Any(w => w.Any(LetterDistribution.IsRare)),
                "This board has no rare word — the test only holds because the bar didn't require one.");
        }

        // ── Long-word guarantee against the real dictionary ──────────────────────

        [TestMethod]
        public void Generate_DefaultBar_RealDictionary_GuaranteesBigFind()
        {
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            var solver = new TracerySolver(TraceryTrie.BuildFrom(svc, TraceryGameEngineMinFloor));
            var gen = new GridGenerator(solver, new RandomNumberService(), svc, NullLogger.Instance);

            // Defaults: 4×4, MinWordLength 4, MinLongWordLength 7, RequireRare true,
            // MinFindableWords 0 (→ engine default 12), MaxGenerationAttempts 0 (→ 50).
            var result = gen.Generate(new TracerySettings());

            Assert.IsTrue(result.IsSuccess);
            var board = result.Value;
            // The big-find guarantee holds whether the board was accepted (cleared the ≥7 bar)
            // or seeded by the fallback (which plants a length-7 word).
            Assert.IsTrue(board.FindableWords.Keys.Any(w => w.Length >= 7),
                "Every board must contain a findable word of length ≥ 7.");

            // When it was a genuinely generated board, the full bar must have been met.
            if (!board.UsedFallback)
            {
                Assert.IsTrue(board.FindableWords.Count >= 12);
                Assert.IsTrue(board.FindableWords.Keys.Any(w => w.Any(LetterDistribution.IsRare)));
            }
        }

        [TestMethod]
        public void Generate_AllCellsAreOnTableLetters()
        {
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            var solver = new TracerySolver(TraceryTrie.BuildFrom(svc, TraceryGameEngineMinFloor));
            var gen = new GridGenerator(solver, new RandomNumberService(), svc, NullLogger.Instance);

            var board = gen.Generate(new TracerySettings()).Value;

            for (int i = 0; i < board.Grid.CellCount; i++)
                Assert.IsTrue(board.Grid[i] is >= 'a' and <= 'z', $"Cell {i} is off-table: '{board.Grid[i]}'.");
        }

        // ── Fallback ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void Generate_UnsatisfiableFindableCount_FiresFallback_AndStillSeedsABigFind()
        {
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            var solver = new TracerySolver(TraceryTrie.BuildFrom(svc, TraceryGameEngineMinFloor));
            var logger = new CapturingLogger();
            var gen = new GridGenerator(solver, new RandomNumberService(), svc, logger);

            // A 3×3 can't hold 999 findable words, so every attempt fails regardless of the
            // letters drawn — generation deterministically exhausts and falls back.
            var settings = new TracerySettings() with
            {
                GridWidth = 3, GridHeight = 3,
                MinWordLength = 3,
                MinFindableWords = 999,
                MinLongWordLength = 7,
                RequireRareLetterWord = false,
                MaxGenerationAttempts = 2,
            };

            var result = gen.Generate(settings);

            Assert.IsTrue(result.IsSuccess);
            var board = result.Value;
            Assert.IsTrue(board.UsedFallback, "Fallback should have fired for the impossible bar.");
            // plantLen = clamp(MinLongWordLength 7, MinWordLength 3, CellCount 9) = 7.
            Assert.IsTrue(board.FindableWords.Keys.Any(w => w.Length >= 7),
                "The fallback must seed a length-7 word that the solver then finds.");
            Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Information),
                "The fallback must log at information level for tuning visibility.");
        }

        // ── Graceful failure ─────────────────────────────────────────────────────

        [TestMethod]
        public void Generate_MinWordLengthExceedsCellCount_ReturnsFailure()
        {
            var gen = MakeGenerator(new SequentialRng(), "car");
            var settings = Settings(2, 2) with { MinWordLength = 5 }; // 5 > 4 cells

            var result = gen.Generate(settings);

            Assert.IsTrue(result.IsFailure);
        }

        // The trie floor the engine builds with; tests mirror it so a length-3 fallback
        // word is recognised by the solver.
        private const int TraceryGameEngineMinFloor = 3;

        /// <summary>Minimal <see cref="ILogger"/> that records its entries for assertions.</summary>
        private sealed class CapturingLogger : ILogger
        {
            public readonly List<(LogLevel Level, string Message)> Entries = new();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
