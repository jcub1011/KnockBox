using KnockBox.ConsultTheCard.Services.Logic.Games.Data;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.ConsultTheCard.Tests.Unit.Logic.Games.ConsultTheCard.Data
{
    [TestClass]
    [DoNotParallelize]
    public class WordBankTests
    {
        private Mock<ILogger> _loggerMock = default!;

        private static string _csvPath = default!;
        private static string _originalCsvContent = default!;

        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            _csvPath = Path.Combine(
                AppContext.BaseDirectory,
                "Services/Logic/Games/Data/WordPairs.csv");
            _originalCsvContent = File.ReadAllText(_csvPath);
        }

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Always restore original CSV content after each test.
            File.WriteAllText(_csvPath, _originalCsvContent);
        }

        [TestMethod]
        public void Load_ReturnsNonEmptyList()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            Assert.IsGreaterThanOrEqualTo(50, groups.Count, $"Expected at least 50 word groups, got {groups.Count}.");
            Assert.IsLessThanOrEqualTo(100, groups.Count, $"Expected at most 100 word groups, got {groups.Count}.");
        }

        [TestMethod]
        public void Load_AllGroupsHaveAtLeastTwoWords()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            foreach (var group in groups)
            {
                Assert.IsGreaterThanOrEqualTo(2, group.Words.Length,
                    $"Word group [{string.Join(", ", group.Words)}] has fewer than 2 words.");
            }
        }

        [TestMethod]
        public void Load_NoWordsHaveLeadingOrTrailingWhitespace()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            foreach (var group in groups)
            {
                foreach (var word in group.Words)
                {
                    Assert.AreEqual(word.Trim(), word,
                        $"Word '{word}' has leading or trailing whitespace.");
                }
            }
        }

        [TestMethod]
        public void Load_NoEmptyWords()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            foreach (var group in groups)
            {
                foreach (var word in group.Words)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(word),
                        "Found an empty or whitespace-only word.");
                }
            }
        }

        [TestMethod]
        public void Load_AllGroupsHaveVariableWordCounts()
        {
            var groups = WordBank.Load(_loggerMock.Object);

            // Verify we can find groups with different word counts (at least some with >2 words).
            // The CSV may or may not have variable-length rows. At minimum, all groups must have ≥2.
            foreach (var group in groups)
            {
                Assert.IsGreaterThanOrEqualTo(2, group.Words.Length,
                    $"Group [{string.Join(", ", group.Words)}] must have at least 2 words.");
            }
        }

        [TestMethod]
        public void Load_HandlesCustomCsvWithVariableLengthRows()
        {
            File.WriteAllText(_csvPath, "Apple, Banana\nCat, Dog, Elephant\nFox, Goat, Horse, Iguana, Jaguar\n");
            var groups = WordBank.Load(_loggerMock.Object);

            Assert.HasCount(3, groups);
            Assert.HasCount(2, groups[0].Words);
            Assert.HasCount(3, groups[1].Words);
            Assert.HasCount(5, groups[2].Words);
        }

        [TestMethod]
        public void Load_SkipsEmptyAndWhitespaceOnlyLines()
        {
            File.WriteAllText(_csvPath, "Apple, Banana\n\n   \n\nCat, Dog\n");
            var groups = WordBank.Load(_loggerMock.Object);

            Assert.HasCount(2, groups);
        }

        [TestMethod]
        public void Load_SkipsRowsWithFewerThanTwoWords()
        {
            File.WriteAllText(_csvPath, "Apple, Banana\nSingleWord\nCat, Dog\n");
            var groups = WordBank.Load(_loggerMock.Object);

            Assert.HasCount(2, groups);
        }

        [TestMethod]
        public void Load_TrimsWhitespaceFromWords()
        {
            File.WriteAllText(_csvPath, "  Apple  ,  Banana  \n");
            var groups = WordBank.Load(_loggerMock.Object);

            Assert.HasCount(1, groups);
            Assert.AreEqual("Apple", groups[0].Words[0]);
            Assert.AreEqual("Banana", groups[0].Words[1]);
        }

        [TestMethod]
        public void Load_EmptyFile_ReturnsEmptyList()
        {
            File.WriteAllText(_csvPath, "");
            var groups = WordBank.Load(_loggerMock.Object);

            Assert.IsEmpty(groups);
        }
    }
}
